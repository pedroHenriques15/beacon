using System.Globalization;
using System.Security.Cryptography;
using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Features.Shared;
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
    TradeImportService tradeImport,
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
            var fileHash = Convert.ToHexString(hashBytes);

            if (await db.MonthlyStatements.AnyAsync(s => s.FileHash == fileHash))
                return new UploadResult(false, string.Empty, DateOnly.MinValue,
                    0, 0, "This exact file has already been imported.");

            ParsedStatement parsed;
            try
            {
                // A CSV is its own text, passed as the single page (ADR-031).
                var pages = preExtractedPages
                    ?? (CsvText.IsCsvFile(file.FileName)
                        ? [CsvText.Decode(fileBytes)]
                        : await extractor.ExtractPagesAsync(tempPath, ct));
                var fullText = string.Join("\n", pages);
                var parser = parserFactory.DetectParser(fullText);
                parsed = parser.Parse(file.FileName, pages);
            }
            catch (FormatException ex)
            {
                // A file the parser refuses (a CSV row of an unknown type, say) is the user's to fix.
                throw new NotSupportedException(ex.Message, ex);
            }

            if (!string.Equals(parsed.Currency, "EUR", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException(
                    $"Only EUR statements are supported - this statement is in {parsed.Currency}.");

            // A statement without balances can't overlap another of its bank at all: the second
            // would book the same rows again on top of the first's balances.
            if (await db.MonthlyStatements.AnyAsync(s => s.Bank == parsed.Bank && (parsed.BalancesRelative
                    ? s.PeriodFrom <= parsed.PeriodTo && s.PeriodTo >= parsed.PeriodFrom
                    : s.PeriodFrom == parsed.PeriodFrom), ct))
                return new UploadResult(false, parsed.Bank, parsed.PeriodFrom,
                    0, 0, "Statement already exists for this bank and period.");

            var warnings = new List<string>();
            if (parsed.BalancesRelative)
                parsed = await ChainBalancesAsync(parsed, warnings, ct);
            warnings.InsertRange(0, ParseVerifier.VerifyStatement(parsed));
            warnings.AddRange(parsed.Warnings ?? []);

            var rules = await db.CategoryRules.ToListAsync();
            var excludedCategoryId = await ExcludedCategory.GetIdAsync(db, ct);

            file.OpenReadStream().Seek(0, SeekOrigin.Begin);
            savedPath = await fileStorage.SaveAsync(file);

            var transactions = parsed.Transactions.Select(tx =>
            {
                var transaction = new Transaction
                {
                    DatePosting = tx.DatePosting,
                    DateValue = tx.DateValue,
                    Description = tx.Description,
                    Amount = tx.Amount,
                    Type = tx.Type,
                    Balance = tx.Balance,
                    CategorySetManually = false,
                };

                // A buy is cash moved into an investment, not spending: excluded, with no
                // category, and its trade becomes a lot below (ADR-031).
                if (tx.Trade is not null)
                {
                    transaction.IsExcluded = true;
                    return transaction;
                }

                var matchedRule = rules
                    .OrderBy(r => r.Id)
                    .FirstOrDefault(r => tx.Description.Contains(r.Pattern, StringComparison.Ordinal));
                transaction.CategoryRuleId = matchedRule?.Id;
                ExcludedCategory.ApplyCategory(transaction, matchedRule?.CategoryId, excludedCategoryId);
                return transaction;
            }).ToList();

            var unknownCount = transactions.Count(t => t.CategoryId is null && !t.IsExcluded);

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

                        var synthetic = new Transaction
                        {
                            DatePosting = parsed.PeriodTo,
                            DateValue = parsed.PeriodTo,
                            Description = "BPI Reforma - Ganhos",
                            Amount = Math.Abs(delta),
                            Type = delta >= 0 ? "credit" : "debit",
                            Balance = parsed.PprBalance.Value,
                            CategoryRuleId = matchedRule?.Id,
                            CategorySetManually = false
                        };
                        ExcludedCategory.ApplyCategory(synthetic, matchedRule?.CategoryId, excludedCategoryId);
                        transactions.Add(synthetic);
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

            // The statement is committed: a lot that fails to import must not fail the upload.
            var trades = parsed.Transactions.Where(t => t.Trade is not null).Select(t => t.Trade!).ToList();
            if (trades.Count > 0)
            {
                try
                {
                    var lots = await tradeImport.ImportAsync(trades, ct);
                    logger.LogInformation("Imported {Count} of {Trades} investment buy(s) as lots for {Bank} {Period}",
                        lots, trades.Count, parsed.Bank, parsed.PeriodFrom);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to import the investment buys of {Bank} {Period} as lots",
                        parsed.Bank, parsed.PeriodFrom);
                    warnings.Add($"The investment buys were not added to Invest: {ex.Message}");
                }
            }

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
                if (newTx.IsExcluded) continue;

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
            // The import itself is already committed - a recompute failure must not fail
            // the upload (or delete the stored PDF of a persisted statement).
            if (parsed.PprBalance.HasValue)
            {
                try
                {
                    var recomputed = await RecomputeNextPprSyntheticAsync(
                        db, rules, excludedCategoryId, parsed.PeriodFrom, parsed.PprBalance.Value);
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
    /// A statement without balances (a CSV export, ADR-031) opens at the closing balance of its
    /// bank's statement for the previous month, and every balance moves up by it. With no earlier
    /// statement it opens at 0.00 and says so; a month missing in between is refused, since the
    /// balances would chain from the wrong month.
    /// </summary>
    private async Task<ParsedStatement> ChainBalancesAsync(
        ParsedStatement parsed, List<string> warnings, CancellationToken ct)
    {
        var previous = await db.MonthlyStatements
            .Where(s => s.Bank == parsed.Bank && s.PeriodFrom < parsed.PeriodFrom)
            .OrderByDescending(s => s.PeriodFrom)
            .FirstOrDefaultAsync(ct);

        var opening = 0m;
        if (previous is null)
        {
            warnings.Add($"No earlier {parsed.Bank} statement, so the opening balance was taken as 0.00.");
        }
        else
        {
            var month = new DateOnly(parsed.PeriodFrom.Year, parsed.PeriodFrom.Month, 1);
            if (previous.PeriodTo < month.AddMonths(-1))
            {
                var missing = new DateOnly(previous.PeriodTo.Year, previous.PeriodTo.Month, 1).AddMonths(1);
                throw new NotSupportedException(
                    $"Import the {parsed.Bank} statement for {missing.ToString("MMMM yyyy", CultureInfo.InvariantCulture)} first: " +
                    "each month's balances follow on from the month before.");
            }
            opening = previous.ClosingBalance;
        }

        if (await db.MonthlyStatements.AnyAsync(s => s.Bank == parsed.Bank && s.PeriodFrom > parsed.PeriodFrom, ct))
            warnings.Add($"A later {parsed.Bank} statement already exists: its balances were not recomputed.");

        return parsed with
        {
            OpeningBalance = parsed.OpeningBalance + opening,
            ClosingBalance = parsed.ClosingBalance + opening,
            Transactions = parsed.Transactions.Select(t => t with { Balance = t.Balance + opening }).ToList(),
            BalancesRelative = false,
        };
    }

    /// <summary>
    /// Backfill correction (audit D1): when a BPI statement is inserted between two existing
    /// ones, the chronologically next statement's synthetic "BPI Reforma - Ganhos" transaction
    /// was computed against an older baseline - recompute it against the new statement.
    /// Returns the period of the recomputed statement, or null when there was nothing to do.
    /// </summary>
    internal static async Task<DateOnly?> RecomputeNextPprSyntheticAsync(
        AppDbContext db,
        IReadOnlyList<CategoryRule> rules,
        int? excludedCategoryId,
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

            var created = new Transaction
            {
                DatePosting = nextStatement.PeriodTo,
                DateValue = nextStatement.PeriodTo,
                Description = "BPI Reforma - Ganhos",
                Amount = Math.Abs(newDelta),
                Type = newDelta >= 0 ? "credit" : "debit",
                Balance = nextStatement.PprBalance.Value,
                CategoryRuleId = matchedRule?.Id,
                CategorySetManually = false
            };
            ExcludedCategory.ApplyCategory(created, matchedRule?.CategoryId, excludedCategoryId);
            nextStatement.Transactions.Add(created);
        }
        else
        {
            return null;
        }

        await db.SaveChangesAsync();
        return nextStatement.PeriodFrom;
    }
}
