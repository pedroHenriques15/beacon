using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Transactions.Commands.UpdateTransaction;

public class UpdateTransactionCommandHandler(AppDbContext db, ILogger<UpdateTransactionCommandHandler> logger)
{
    public async Task<UpdateTransactionResponse?> HandleAsync(UpdateTransactionCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("UpdateTransaction: id={Id}", cmd.Id);
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == cmd.Id, ct);
        if (tx is null) return null;

        if (cmd.DatePosting.HasValue)  tx.DatePosting  = cmd.DatePosting.Value;
        if (cmd.DateValue.HasValue)    tx.DateValue     = cmd.DateValue.Value;
        if (cmd.Description is not null && cmd.Description.Trim().Length > 0)
            tx.Description = cmd.Description.Trim();
        if (cmd.Amount.HasValue && cmd.Amount > 0)     tx.Amount  = cmd.Amount.Value;
        if (cmd.Type is not null)                      tx.Type    = cmd.Type.ToLower();
        if (cmd.Balance.HasValue)                      tx.Balance = cmd.Balance.Value;

        if (cmd.UnlinkTransfer)
            tx.IsExcluded = false;

        if (cmd.UnlinkCategory)
        {
            tx.CategoryId          = null;
            tx.CategoryRuleId      = null;
            tx.CategorySetManually = false;
        }
        else if (cmd.CategoryId.HasValue)
        {
            tx.CategoryId          = cmd.CategoryId.Value == 0 ? null : cmd.CategoryId.Value;
            tx.CategorySetManually = true;
            tx.CategoryRuleId      = null;
        }

        await db.SaveChangesAsync(ct);

        return new UpdateTransactionResponse(
            tx.Id, tx.StatementId, tx.DatePosting, tx.DateValue,
            tx.Description, tx.Amount, tx.Type, tx.Balance,
            tx.CategoryId, tx.CategoryRuleId, tx.CategorySetManually, tx.IsExcluded);
    }
}
