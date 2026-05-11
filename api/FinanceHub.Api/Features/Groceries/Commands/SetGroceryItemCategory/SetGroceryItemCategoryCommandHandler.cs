using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Groceries.Commands.SetGroceryItemCategory;

public class SetGroceryItemCategoryCommandHandler(AppDbContext db, ILogger<SetGroceryItemCategoryCommandHandler> logger)
{
    public async Task<SetGroceryItemCategoryResponse?> HandleAsync(SetGroceryItemCategoryCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("SetGroceryItemCategory: itemId={ItemId} categoryId={CategoryId}", cmd.ItemId, cmd.CategoryId);
        var item = await db.GroceryItems.FirstOrDefaultAsync(i => i.Id == cmd.ItemId, ct);
        if (item is null) return null;

        item.CategoryId          = cmd.CategoryId;
        item.CategorySetManually = true;
        item.CategoryRuleId      = null;

        if (cmd.DeleteRuleId.HasValue)
        {
            var rule = await db.GroceryCategoryRules.FindAsync([cmd.DeleteRuleId.Value], ct);
            if (rule is not null) db.GroceryCategoryRules.Remove(rule);
        }

        await db.SaveChangesAsync(ct);
        return new SetGroceryItemCategoryResponse(item.Id, item.CategoryId);
    }
}
