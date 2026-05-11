using FinanceHub.Api.Data;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Transactions.Commands.SetTransactionCategory;

public class SetTransactionCategoryCommandHandler(AppDbContext db, ILogger<SetTransactionCategoryCommandHandler> logger)
{
    public async Task<Transaction?> HandleAsync(SetTransactionCategoryCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("SetTransactionCategory: txId={TxId} categoryId={CategoryId}", cmd.TransactionId, cmd.CategoryId);
        var tx = await db.Transactions
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Id == cmd.TransactionId, ct);

        if (tx is null) return null;

        tx.CategoryId          = cmd.CategoryId;
        tx.CategorySetManually = true;
        tx.CategoryRuleId      = null;

        if (cmd.DeleteRuleId.HasValue)
        {
            var rule = await db.CategoryRules.FindAsync([cmd.DeleteRuleId.Value], ct);
            if (rule is not null) db.CategoryRules.Remove(rule);
        }

        await db.SaveChangesAsync(ct);
        await db.Entry(tx).Reference(t => t.Category).LoadAsync(ct);
        return tx;
    }
}
