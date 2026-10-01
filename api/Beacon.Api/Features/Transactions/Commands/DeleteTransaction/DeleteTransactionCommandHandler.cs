using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Transactions.Commands.DeleteTransaction;

public class DeleteTransactionCommandHandler(AppDbContext db, ILogger<DeleteTransactionCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteTransactionCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("DeleteTransaction: id={Id}", cmd.Id);
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == cmd.Id, ct);
        if (tx is null) return false;

        var statementId = tx.StatementId;
        db.Transactions.Remove(tx);
        await db.SaveChangesAsync(ct);

        var remaining = await db.Transactions.CountAsync(t => t.StatementId == statementId, ct);
        if (remaining == 0)
        {
            var statement = await db.MonthlyStatements.FindAsync([statementId], ct);
            if (statement is not null)
            {
                db.MonthlyStatements.Remove(statement);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("DeleteTransaction: removed empty statement id={StatementId}", statementId);
            }
        }

        return true;
    }
}
