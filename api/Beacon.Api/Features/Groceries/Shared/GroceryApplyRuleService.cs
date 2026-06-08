using Beacon.Api.Data;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Groceries.Shared;

public class GroceryApplyRuleService(AppDbContext db)
{
    public async Task ApplyAsync(GroceryCategoryRule rule)
    {
        var hasPattern = !string.IsNullOrEmpty(rule.Pattern);
        var hasValue   = rule.Value.HasValue;

        if (!hasPattern && !hasValue)
            return;

        var uncategorized = await db.GroceryItems
            .Where(i => i.CategoryId == null)
            .ToListAsync();

        var matches = uncategorized.Where(i =>
        {
            var patternOk = !hasPattern || i.Description.Contains(rule.Pattern!, StringComparison.Ordinal);
            var valueOk   = !hasValue   || i.Amount == rule.Value!.Value;
            return patternOk && valueOk;
        }).ToList();

        foreach (var item in matches)
        {
            item.CategoryId          = rule.CategoryId;
            item.CategoryRuleId      = rule.Id;
            item.CategorySetManually = false;
        }

        if (matches.Count > 0)
            await db.SaveChangesAsync();
    }
}
