using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Transactions.Commands.DeleteTransaction;

public class DeleteTransactionCommandHandler(AppDbContext db, ILogger<DeleteTransactionCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteTransactionCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("DeleteTransaction: id={Id}", cmd.Id);
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == cmd.Id, ct);
        if (tx is null) return false;

        db.Transactions.Remove(tx);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
