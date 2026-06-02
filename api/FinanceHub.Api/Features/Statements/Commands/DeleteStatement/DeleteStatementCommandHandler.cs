using FinanceHub.Api.Data;
using FinanceHub.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Statements.Commands.DeleteStatement;

public class DeleteStatementCommandHandler(AppDbContext db, FileStorageService fileStorage, ILogger<DeleteStatementCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteStatementCommand cmd, CancellationToken ct = default)
    {
        var statement = await db.MonthlyStatements
            .Include(s => s.Transactions)
            .FirstOrDefaultAsync(s => s.Id == cmd.Id, ct);

        if (statement is null) return false;

        var counterpartStatementIds = new HashSet<int>();
        var transferTxs = statement.Transactions.Where(t => t.IsExcluded).ToList();

        foreach (var tx in transferTxs)
        {
            var absAmount = Math.Abs(tx.Amount);
            var dateFrom = tx.DatePosting.AddDays(-7);
            var dateTo = tx.DatePosting.AddDays(7);

            var counterparts = await db.Transactions
                .Where(t => t.StatementId != statement.Id
                    && t.IsExcluded
                    && t.DatePosting >= dateFrom
                    && t.DatePosting <= dateTo
                    && (t.Amount == absAmount || t.Amount == -absAmount))
                .ToListAsync(ct);

            foreach (var cp in counterparts)
            {
                counterpartStatementIds.Add(cp.StatementId);
                db.Transactions.Remove(cp);
            }
        }

        if (statement.PdfPath is not null)
        {
            try { fileStorage.Delete(statement.PdfPath); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete PDF for statement {Id}", statement.Id); }
        }

        db.MonthlyStatements.Remove(statement);
        await db.SaveChangesAsync(ct);

        foreach (var csId in counterpartStatementIds)
        {
            var cs = await db.MonthlyStatements
                .Include(s => s.Transactions)
                .FirstOrDefaultAsync(s => s.Id == csId, ct);

            if (cs is not null && cs.Transactions.Count == 0)
            {
                if (cs.PdfPath is not null)
                {
                    try { fileStorage.Delete(cs.PdfPath); }
                    catch (Exception ex) { logger.LogWarning(ex, "Could not delete PDF for statement {Id}", csId); }
                }
                db.MonthlyStatements.Remove(cs);
                await db.SaveChangesAsync(ct);
            }
        }

        return true;
    }
}
