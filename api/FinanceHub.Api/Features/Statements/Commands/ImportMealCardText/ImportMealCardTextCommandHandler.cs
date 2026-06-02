using FinanceHub.Api.Data;
using FinanceHub.Api.Models;
using FinanceHub.Api.Services;
using FinanceHub.Api.Services.Parsing;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Statements.Commands.ImportMealCardText;

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

        var periodFrom     = command.PeriodFrom    ?? parsed.PeriodFrom;
        var periodTo       = command.PeriodTo      ?? parsed.PeriodTo;
        var closingBalance = command.ClosingBalance ?? parsed.ClosingBalance;

        if (await db.MonthlyStatements.AnyAsync(
                s => s.Bank == parsed.Bank && s.PeriodFrom == periodFrom, ct))
            return new UploadResult(false, parsed.Bank, periodFrom,
                0, 0, "Statement already exists for MEAL CARD for this period.");

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
            OpeningBalance = parsed.OpeningBalance,
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
            candidates.Count > 0 ? candidates : null);
    }
}
