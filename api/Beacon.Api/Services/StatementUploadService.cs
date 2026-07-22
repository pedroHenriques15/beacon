using System.Security.Cryptography;
using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services.Parsing;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Services;

public record TransferCandidate(
    int NewTxId, string NewDescription, string NewType, string NewBank,
    int ExistingTxId, string ExistingDescription, string ExistingType, string ExistingBank,
    DateOnly Date, decimal Amount);

public record UploadResult(
    bool Imported, string Bank, DateOnly PeriodFrom,
    int TransactionCount, int UnknownCount, string? Message,
    List<TransferCandidate>? TransferCandidates = null,
    IReadOnlyList<string>? Warnings = null);

public class StatementUploadService(
    AppDbContext db,
    IPdfExtractor extractor,
    BankStatementParserFactory parserFactory,
    FileStorageService fileStorage,
    ILogger<StatementUploadService> logger)
{
    public async Task<UploadResult> ImportAsync(IFormFile file, IReadOnlyList<string>? preExtractedPages = null, CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"beacon_{Guid.NewGuid():N}.pdf");
        string? savedPath = null;
        try
        {
            await using (var fs = File.Create(tempPath))
                await file.CopyToAsync(fs);

            var fileBytes = await File.ReadAllBytesAsync(tempPath);
            var hashBytes = SHA256.HashData(fileBytes);
            var fileHash  = Convert.ToHexString(hashBytes);

            if (await db.MonthlyStatements.AnyAsync(s => s.FileHash == fileHash))
                return new UploadResult(false, string.Empty, DateOnly.MinValue,
                    0, 0, "This exact file has already been imported.");

            var pages = preExtractedPages ?? await extractor.ExtractPagesAsync(tempPath, ct);
            var fullText = string.Join("\n", pages);
            var parser = parserFactory.DetectParser(fullText);
            var parsed = parser.Parse(file.FileName, pages);

            if (!string.Equals(parsed.Currency, "EUR", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException(
                    $"Only EUR statements are supported — this statement is in {parsed.Currency}.");

            var warnings = ParseVerifier.VerifyStatement(parsed);

            if (await db.MonthlyStatements.AnyAsync(s =>
                    s.Bank == parsed.Bank && s.PeriodFrom == parsed.PeriodFrom))
                return new UploadResult(false, parsed.Bank, parsed.PeriodFrom,
                    0, 0, "Statement already exists for this bank and period.");

            var rules = await db.CategoryRules.ToListAsync();

            file.OpenReadStream().Seek(0, SeekOrigin.Begin);
            savedPath = await fileStorage.SaveAsync(file);

            var transactions = parsed.Transactions.Select(tx =>
            {
                var matchedRule = rules
                    .OrderBy(r => r.Id)
                    .FirstOrDefault(r => tx.Description.Contains(r.Pattern, StringComparison.Ordinal));

                return new Transaction
                {
                    DatePosting = tx.DatePosting,
                    DateValue = tx.DateValue,
                    Description = tx.Description,
                    Amount = tx.Amount,
                    Type = tx.Type,
                    Balance = tx.Balance,
                    CategoryId = matchedRule?.CategoryId,
                    CategoryRuleId = matchedRule?.Id,
                    CategorySetManually = false
                };
            }).ToList();

            var unknownCount = transactions.Count(t => t.CategoryId is null);

            if (parsed.PprBalance.HasValue)
            {
                var prevStatement = await db.MonthlyStatements
                    .Where(s => s.Bank == "BPI" && s.PprBalance.HasValue && s.PeriodFrom < parsed.PeriodFrom)
                    .OrderByDescending(s => s.PeriodFrom)
                    .FirstOrDefaultAsync();

                if (prevStatement is not null)
                {
                    var delta = parsed.PprBalance.Value - prevStatement.PprBalance!.Value;
                    if (delta != 0)
                    {
                        var matchedRule = rules
                            .OrderBy(r => r.Id)
                            .FirstOrDefault(r => "BPI Reforma - Ganhos".Contains(r.Pattern, StringComparison.Ordinal));

                        transactions.Add(new Transaction
                        {
                            DatePosting = parsed.PeriodTo,
                            DateValue = parsed.PeriodTo,
                            Description = "BPI Reforma - Ganhos",
                            Amount = Math.Abs(delta),
                            Type = delta >= 0 ? "credit" : "debit",
                            Balance = parsed.PprBalance.Value,
                            CategoryId = matchedRule?.CategoryId,
                            CategoryRuleId = matchedRule?.Id,
                            CategorySetManually = false
                        });
                    }
                }
            }

            var statement = new MonthlyStatement
            {
                Bank = parsed.Bank,
                Account = parsed.Account ?? string.Empty,
                PeriodFrom = parsed.PeriodFrom,
                PeriodTo = parsed.PeriodTo,
                Currency = parsed.Currency,
                OpeningBalance = parsed.OpeningBalance,
                ClosingBalance = parsed.ClosingBalance,
                SourceFile = parsed.SourceFile,
                PdfPath = savedPath,
                FileHash = fileHash,
                PprBalance = parsed.PprBalance,
                Transactions = transactions
            };

            db.MonthlyStatements.Add(statement);
            await db.SaveChangesAsync();

            logger.LogInformation("Imported {Count} transactions for {Bank} {Period} ({Unknown} uncategorised)",
                parsed.Transactions.Count, parsed.Bank, parsed.PeriodFrom, unknownCount);

            var newTxIds = transactions.Select(t => t.Id).ToHashSet();
            var existingTxs = new List<Transaction>();
            if (transactions.Count > 0)
            {
                var minDate = transactions.Min(t => t.DatePosting);
                var maxDate = transactions.Max(t => t.DatePosting);
                var amounts = transactions.Select(t => t.Amount).Distinct().ToList();
                existingTxs = await db.Transactions
                    .Include(t => t.Statement)
                    .Where(t => !newTxIds.Contains(t.Id) && !t.IsExcluded
                        && t.DatePosting >= minDate && t.DatePosting <= maxDate
                        && amounts.Contains(t.Amount))
                    .AsNoTracking()
                    .ToListAsync();
            }

            var candidates = new List<TransferCandidate>();
            var usedExisting = new HashSet<int>();

            foreach (var newTx in transactions)
            {
                var opposite = existingTxs.FirstOrDefault(e =>
                    !usedExisting.Contains(e.Id) &&
                    e.DatePosting == newTx.DatePosting &&
                    e.Amount == newTx.Amount &&
                    e.Type != newTx.Type);

                if (opposite is not null)
                {
                    usedExisting.Add(opposite.Id);
                    candidates.Add(new TransferCandidate(
                        newTx.Id, newTx.Description, newTx.Type, parsed.Bank,
                        opposite.Id, opposite.Description, opposite.Type, opposite.Statement.Bank,
                        newTx.DatePosting, newTx.Amount));
                }
            }

            // Backfill correction runs AFTER the transfer-candidate scan so a synthetic
            // created on a later statement can never be proposed as a transfer counterpart.
            // The import itself is already committed — a recompute failure must not fail
            // the upload (or delete the stored PDF of a persisted statement).
            if (parsed.PprBalance.HasValue)
            {
                try
                {
                    var recomputed = await RecomputeNextPprSyntheticAsync(
                        db, rules, parsed.PeriodFrom, parsed.PprBalance.Value);
                    if (recomputed is not null)
                        logger.LogInformation(
                            "Recomputed synthetic PPR transaction for BPI {Period} after backfill of {NewPeriod}",
                            recomputed, parsed.PeriodFrom);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to recompute the next BPI statement's synthetic PPR transaction after importing {Period}",
                        parsed.PeriodFrom);
                }
            }

            return new UploadResult(true, parsed.Bank, parsed.PeriodFrom,
                parsed.Transactions.Count, unknownCount, null,
                candidates.Count > 0 ? candidates : null,
                warnings.Count > 0 ? warnings : null);
        }
        catch
        {
            if (savedPath is not null) fileStorage.Delete(savedPath);
            throw;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Backfill correction (audit D1): when a BPI statement is inserted between two existing
    /// ones, the chronologically next statement's synthetic "BPI Reforma - Ganhos" transaction
    /// was computed against an older baseline — recompute it against the new statement.
    /// Returns the period of the recomputed statement, or null when there was nothing to do.
    /// </summary>
    internal static async Task<DateOnly?> RecomputeNextPprSyntheticAsync(
        AppDbContext db,
        IReadOnlyList<CategoryRule> rules,
        DateOnly uploadedPeriodFrom,
        decimal uploadedPprBalance)
    {
        var nextStatement = await db.MonthlyStatements
            .Include(s => s.Transactions)
            .Where(s => s.Bank == "BPI" && s.PprBalance.HasValue && s.PeriodFrom > uploadedPeriodFrom)
            .OrderBy(s => s.PeriodFrom)
            .FirstOrDefaultAsync();

        if (nextStatement is null) return null;

        var newDelta = nextStatement.PprBalance!.Value - uploadedPprBalance;
        var synthetic = nextStatement.Transactions
            .FirstOrDefault(t => t.Description == "BPI Reforma - Ganhos");

        if (synthetic is not null)
        {
            if (newDelta == 0)
            {
                // Respect user-touched rows: a manually categorised or excluded synthetic
                // is left in place rather than silently destroyed.
                if (synthetic.CategorySetManually || synthetic.IsExcluded) return null;
                db.Transactions.Remove(synthetic);
            }
            else
            {
                synthetic.Amount = Math.Abs(newDelta);
                synthetic.Type = newDelta >= 0 ? "credit" : "debit";
            }
        }
        else if (newDelta != 0)
        {
            var matchedRule = rules
                .OrderBy(r => r.Id)
                .FirstOrDefault(r => "BPI Reforma - Ganhos".Contains(r.Pattern, StringComparison.Ordinal));

            nextStatement.Transactions.Add(new Transaction
            {
                DatePosting = nextStatement.PeriodTo,
                DateValue = nextStatement.PeriodTo,
                Description = "BPI Reforma - Ganhos",
                Amount = Math.Abs(newDelta),
                Type = newDelta >= 0 ? "credit" : "debit",
                Balance = nextStatement.PprBalance.Value,
                CategoryId = matchedRule?.CategoryId,
                CategoryRuleId = matchedRule?.Id,
                CategorySetManually = false
            });
        }
        else
        {
            return null;
        }

        await db.SaveChangesAsync();
        return nextStatement.PeriodFrom;
    }
}
