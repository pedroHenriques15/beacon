using Beacon.Api.Data;
using Beacon.Api.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Groceries.Commands.MarkGroceryItemsExcluded;

public class MarkGroceryItemsExcludedCommandHandler(AppDbContext db, ILogger<MarkGroceryItemsExcludedCommandHandler> logger)
{
    public async Task HandleAsync(MarkGroceryItemsExcludedCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("MarkGroceryItemsExcluded: ids=[{Ids}] unmark={Unmark}", string.Join(',', cmd.ItemIds), cmd.Unmark);

        var items = await db.GroceryItems
            .Where(i => cmd.ItemIds.Contains(i.Id))
            .ToListAsync(ct);

        var excludedCategoryId = await ExcludedCategory.GetGroceryIdAsync(db, ct);

        foreach (var item in items)
        {
            item.IsExcluded = !cmd.Unmark;
            item.CategoryId = cmd.Unmark ? null : excludedCategoryId;
            item.CategorySetManually = false;
            item.CategoryRuleId = null;
        }

        await db.SaveChangesAsync(ct);
    }
}
