using Beacon.Api.Data;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Statements.Commands.DeleteStatement;

public class DeleteStatementCommandHandler(AppDbContext db, FileStorageService fileStorage, ILogger<DeleteStatementCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteStatementCommand cmd, CancellationToken ct = default)
    {
        var statement = await db.MonthlyStatements
            .Include(s => s.Transactions)
            .FirstOrDefaultAsync(s => s.Id == cmd.Id, ct);

        if (statement is null) return false;

        var counterpartStatementIds = new HashSet<int>();
        var removedCounterpartIds = new HashSet<int>();
        var transferTxs = statement.Transactions.Where(t => t.IsExcluded).ToList();

        foreach (var tx in transferTxs)
        {
            var dateFrom = tx.DatePosting.AddDays(-7);
            var dateTo = tx.DatePosting.AddDays(7);
            var absAmount = Math.Abs(tx.Amount);

            var counterpart = (await db.Transactions
                .Where(t => t.StatementId != statement.Id
                    && t.IsExcluded
                    && t.DatePosting >= dateFrom
                    && t.DatePosting <= dateTo
                    && (t.Amount == absAmount || t.Amount == -absAmount))
                .ToListAsync(ct))
                .Where(t => !removedCounterpartIds.Contains(t.Id))
                .Where(t => IsMirrorDirection(tx.Type, t.Type))
                .OrderBy(t => Math.Abs(t.DatePosting.DayNumber - tx.DatePosting.DayNumber))
                .ThenBy(t => t.Id)
                .FirstOrDefault();

            if (counterpart is not null)
            {
                removedCounterpartIds.Add(counterpart.Id);
                counterpartStatementIds.Add(counterpart.StatementId);
                db.Transactions.Remove(counterpart);
            }
        }

        db.MonthlyStatements.Remove(statement);

        // Counterpart statements left empty once the removals apply are deleted too.
        var emptiedStatements = new List<Models.MonthlyStatement>();
        foreach (var csId in counterpartStatementIds)
        {
            var cs = await db.MonthlyStatements
                .Include(s => s.Transactions)
                .FirstOrDefaultAsync(s => s.Id == csId, ct);

            if (cs is not null && cs.Transactions.All(t => removedCounterpartIds.Contains(t.Id)))
            {
                emptiedStatements.Add(cs);
                db.MonthlyStatements.Remove(cs);
            }
        }

        await db.SaveChangesAsync(ct);

        DeletePdf(statement.PdfPath, statement.Id);
        foreach (var cs in emptiedStatements)
            DeletePdf(cs.PdfPath, cs.Id);

        return true;
    }

    private static bool IsMirrorDirection(string sourceType, string candidateType) =>
        (sourceType == "debit" && candidateType == "credit")
        || (sourceType == "credit" && candidateType == "debit")
        || sourceType == "unknown"
        || candidateType == "unknown";

    private void DeletePdf(string? pdfPath, int statementId)
    {
        if (pdfPath is null) return;
        try { fileStorage.Delete(pdfPath); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not delete PDF for statement {Id}", statementId); }
    }
}
