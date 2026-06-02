using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Transactions.Commands.BulkDeleteTransactions;

public class BulkDeleteTransactionsCommandHandler(AppDbContext db, ILogger<BulkDeleteTransactionsCommandHandler> logger)
{
    public async Task HandleAsync(BulkDeleteTransactionsCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("BulkDeleteTransactions: ids={Ids}", string.Join(",", cmd.Ids));

        var transactions = await db.Transactions
            .Where(t => cmd.Ids.Contains(t.Id))
            .ToListAsync(ct);

        if (transactions.Count == 0) return;

        var statementIds = transactions.Select(t => t.StatementId).Distinct().ToList();

        db.Transactions.RemoveRange(transactions);
        await db.SaveChangesAsync(ct);

        foreach (var statementId in statementIds)
        {
            var remaining = await db.Transactions.CountAsync(t => t.StatementId == statementId, ct);
            if (remaining == 0)
            {
                var statement = await db.MonthlyStatements.FindAsync([statementId], ct);
                if (statement is not null)
                {
                    db.MonthlyStatements.Remove(statement);
                    logger.LogInformation("BulkDeleteTransactions: removed empty statement id={StatementId}", statementId);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
