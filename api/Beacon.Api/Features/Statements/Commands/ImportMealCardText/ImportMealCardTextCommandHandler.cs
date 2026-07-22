using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Statements.Commands.ImportMealCardText;

public class ImportMealCardTextCommandHandler(
    AppDbContext db,
    ILogger<ImportMealCardTextCommandHandler> logger)
{
    public async Task<UploadResult> HandleAsync(ImportMealCardTextCommand command, CancellationToken ct)
    {
        ParsedStatement parsed;
        try
        {
            parsed = MealCardTextParser.Parse(command.RawText);
        }
        catch (FormatException ex)
        {
            throw new NotSupportedException(ex.Message);
        }

        var periodFrom = command.PeriodFrom ?? parsed.PeriodFrom;
        var periodTo   = command.PeriodTo   ?? parsed.PeriodTo;

        if (await db.MonthlyStatements.AnyAsync(
                s => s.Bank == parsed.Bank && s.PeriodFrom == periodFrom, ct))
            return new UploadResult(false, parsed.Bank, periodFrom,
                0, 0, "Statement already exists for MEAL CARD for this period.");

        decimal openingBalance = parsed.OpeningBalance;
        decimal closingBalance;
        if (command.ClosingBalance is not null)
        {
            closingBalance = command.ClosingBalance.Value;
        }
        else
        {
            var previous = await db.MonthlyStatements
                .Where(s => s.Bank == parsed.Bank && s.PeriodFrom < periodFrom)
                .OrderByDescending(s => s.PeriodFrom)
                .FirstOrDefaultAsync(ct);

            if (previous is null)
                throw new NotSupportedException(
                    "No previous MEAL CARD statement exists to derive the balance from — please fill in the current balance.");

            // Deriving across a gap would silently assume zero activity in the missing
            // months — only derive from a period-adjacent statement.
            if (periodFrom > previous.PeriodTo.AddDays(45))
                throw new NotSupportedException(
                    $"The previous MEAL CARD statement ends {previous.PeriodTo:yyyy-MM-dd}, leaving a gap before " +
                    $"{periodFrom:yyyy-MM-dd} — please fill in the current balance (or import the missing months first).");

            var credits = parsed.Transactions.Where(t => t.Type == "credit").Sum(t => t.Amount);
            var debits  = parsed.Transactions.Where(t => t.Type == "debit").Sum(t => t.Amount);
            openingBalance = previous.ClosingBalance;
            closingBalance = previous.ClosingBalance + credits - debits;
        }

        // Backfilling before an existing statement cannot fix that statement's balance —
        // surface it instead of leaving Total Balance silently stale.
        var laterExists = await db.MonthlyStatements
            .AnyAsync(s => s.Bank == parsed.Bank && s.PeriodFrom > periodFrom, ct);
        var balanceWarnings = laterExists
            ? new List<string>
            {
                "A later MEAL CARD statement already exists — its balance was not recomputed and may need updating.",
            }
            : null;

        var rules = await db.CategoryRules.ToListAsync(ct);

        var transactions = parsed.Transactions.Select(tx =>
        {
            var matchedRule = rules
                .OrderBy(r => r.Id)
                .FirstOrDefault(r => tx.Description.Contains(r.Pattern, StringComparison.Ordinal));

            return new Transaction
            {
                DatePosting        = tx.DatePosting,
                DateValue          = tx.DateValue,
                Description        = tx.Description,
                Amount             = tx.Amount,
                Type               = tx.Type,
                Balance            = tx.Balance,
                CategoryId         = matchedRule?.CategoryId,
                CategoryRuleId     = matchedRule?.Id,
                CategorySetManually = false
            };
        }).ToList();

        var unknownCount = transactions.Count(t => t.CategoryId is null);

        var statement = new MonthlyStatement
        {
            Bank           = parsed.Bank,
            Account        = parsed.Account ?? string.Empty,
            PeriodFrom     = periodFrom,
            PeriodTo       = periodTo,
            Currency       = parsed.Currency,
            OpeningBalance = openingBalance,
            ClosingBalance = closingBalance,
            SourceFile     = parsed.SourceFile,
            PdfPath        = null,
            FileHash       = null,
            Transactions   = transactions
        };

        db.MonthlyStatements.Add(statement);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Imported {Count} meal card transactions ({PeriodFrom}–{PeriodTo}, {Unknown} uncategorised)",
            parsed.Transactions.Count, periodFrom, periodTo, unknownCount);

        var newTxIds    = transactions.Select(t => t.Id).ToHashSet();
        var existingTxs = await db.Transactions
            .Include(t => t.Statement)
            .Where(t => !newTxIds.Contains(t.Id) && !t.IsExcluded)
            .ToListAsync(ct);

        var candidates  = new List<TransferCandidate>();
        var usedExisting = new HashSet<int>();

        foreach (var newTx in transactions)
        {
            var opposite = existingTxs.FirstOrDefault(e =>
                !usedExisting.Contains(e.Id) &&
                e.DatePosting == newTx.DatePosting &&
                e.Amount      == newTx.Amount &&
                e.Type        != newTx.Type);

            if (opposite is not null)
            {
                usedExisting.Add(opposite.Id);
                candidates.Add(new TransferCandidate(
                    newTx.Id,    newTx.Description,    newTx.Type,    parsed.Bank,
                    opposite.Id, opposite.Description, opposite.Type, opposite.Statement.Bank,
                    newTx.DatePosting, newTx.Amount));
            }
        }

        return new UploadResult(true, parsed.Bank, periodFrom,
            parsed.Transactions.Count, unknownCount, null,
            candidates.Count > 0 ? candidates : null,
            balanceWarnings);
    }
}
