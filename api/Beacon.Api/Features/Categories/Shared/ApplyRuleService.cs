using Beacon.Api.Data;
using Beacon.Api.Features.Shared;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Categories.Shared;

public class ApplyRuleService(AppDbContext db)
{
    public async Task ApplyAsync(CategoryRule rule)
    {
        if (string.IsNullOrEmpty(rule.Pattern) && rule.Value is null)
            return;

        var uncategorized = await db.Transactions
            .Where(t => t.CategoryId == null)
            .ToListAsync();

        var matches = uncategorized
            .Where(t => RuleMatch.Matches(rule.Pattern, rule.MatchWholeDescription, rule.Value, t.Description, t.Amount))
            .ToList();

        var excludedCategoryId = await ExcludedCategory.GetIdAsync(db);

        foreach (var tx in matches)
        {
            ExcludedCategory.ApplyCategory(tx, rule.CategoryId, excludedCategoryId);
            tx.CategoryRuleId = rule.Id;
            tx.CategorySetManually = false;
        }

        if (matches.Count > 0)
            await db.SaveChangesAsync();
    }
}
