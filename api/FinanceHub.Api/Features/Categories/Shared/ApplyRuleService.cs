using FinanceHub.Api.Data;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Categories.Shared;

public class ApplyRuleService(AppDbContext db)
{
    public async Task ApplyAsync(CategoryRule rule)
    {
        var hasPattern = !string.IsNullOrEmpty(rule.Pattern);
        var hasValue   = rule.Value.HasValue;

        if (!hasPattern && !hasValue)
            return;

        var uncategorized = await db.Transactions
            .Where(t => t.CategoryId == null)
            .ToListAsync();

        var matches = uncategorized.Where(t =>
        {
            var patternOk = !hasPattern || t.Description.Contains(rule.Pattern!, StringComparison.Ordinal);
            var valueOk   = !hasValue   || t.Amount == rule.Value!.Value;
            return patternOk && valueOk;
        }).ToList();

        foreach (var tx in matches)
        {
            tx.CategoryId          = rule.CategoryId;
            tx.CategoryRuleId      = rule.Id;
            tx.CategorySetManually = false;
        }

        if (matches.Count > 0)
            await db.SaveChangesAsync();
    }
}
