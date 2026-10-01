using Beacon.Api.Data;
using Beacon.Api.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Transactions.Commands.MarkTransfers;

public class MarkTransfersCommandHandler(AppDbContext db, ILogger<MarkTransfersCommandHandler> logger)
{
    public async Task HandleAsync(MarkTransfersCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("MarkTransfers: ids=[{Ids}] unmark={Unmark}", string.Join(',', cmd.TxIds), cmd.Unmark);

        var txs = await db.Transactions
            .Where(t => cmd.TxIds.Contains(t.Id))
            .ToListAsync(ct);

        var excludedCategoryId = await ExcludedCategory.GetIdAsync(db, ct);

        foreach (var tx in txs)
        {
            tx.IsExcluded = !cmd.Unmark;
            tx.CategoryId = cmd.Unmark ? null : excludedCategoryId;
            tx.CategorySetManually = false;
            tx.CategoryRuleId = null;
        }

        await db.SaveChangesAsync(ct);
    }
}
