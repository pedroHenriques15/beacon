using Beacon.Api.Data;
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

        int? excludedCategoryId = null;
        if (!cmd.Unmark)
        {
            var cat = await db.GroceryCategories.FirstOrDefaultAsync(c => c.Name == "Excluded", ct);
            excludedCategoryId = cat?.Id;
        }

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
