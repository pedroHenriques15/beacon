using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Transactions.Commands.MarkTransfers;

public class MarkTransfersCommandHandler(AppDbContext db, ILogger<MarkTransfersCommandHandler> logger)
{
    public async Task HandleAsync(MarkTransfersCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("MarkTransfers: ids=[{Ids}] unmark={Unmark}", string.Join(',', cmd.TxIds), cmd.Unmark);

        var txs = await db.Transactions
            .Where(t => cmd.TxIds.Contains(t.Id))
            .ToListAsync(ct);

        int? internalTransferCategoryId = null;
        if (!cmd.Unmark)
        {
            var cat = await db.Categories.FirstOrDefaultAsync(c => c.Name == "Excluded", ct);
            internalTransferCategoryId = cat?.Id;
        }

        foreach (var tx in txs)
        {
            tx.IsExcluded = !cmd.Unmark;
            tx.CategoryId = cmd.Unmark ? null : internalTransferCategoryId;
            tx.CategorySetManually = false;
            tx.CategoryRuleId = null;
        }

        await db.SaveChangesAsync(ct);
    }
}
