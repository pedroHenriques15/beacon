using Beacon.Api.Data;
using Beacon.Api.Features.Shared;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Groceries.Shared;

public class GroceryApplyRuleService(AppDbContext db)
{
    public async Task ApplyAsync(GroceryCategoryRule rule)
    {
        if (string.IsNullOrEmpty(rule.Pattern) && rule.Value is null)
            return;

        var uncategorized = await db.GroceryItems
            .Where(i => i.CategoryId == null)
            .ToListAsync();

        var matches = uncategorized
            .Where(i => RuleMatch.Matches(rule.Pattern, rule.MatchWholeDescription, rule.Value, i.Description, i.Amount))
            .ToList();

        var excludedCategoryId = await ExcludedCategory.GetGroceryIdAsync(db);

        foreach (var item in matches)
        {
            ExcludedCategory.ApplyCategory(item, rule.CategoryId, excludedCategoryId);
            item.CategoryRuleId = rule.Id;
            item.CategorySetManually = false;
        }

        if (matches.Count > 0)
            await db.SaveChangesAsync();
    }
}
