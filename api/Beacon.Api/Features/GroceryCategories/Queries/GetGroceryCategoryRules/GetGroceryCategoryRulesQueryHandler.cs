using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.GroceryCategories.Queries.GetGroceryCategoryRules;

public class GetGroceryCategoryRulesQueryHandler(AppDbContext db, ILogger<GetGroceryCategoryRulesQueryHandler> logger)
{
    public async Task<List<GetGroceryCategoryRulesResponse>> HandleAsync(CancellationToken ct = default)
    {
        logger.LogInformation("GetGroceryCategoryRules");
        return await db.GroceryCategoryRules
            .Include(r => r.Category)
            .OrderBy(r => EF.Functions.Collate(r.Category.Name, SqliteSetup.DisplayOrder)).ThenBy(r => EF.Functions.Collate(r.Pattern, SqliteSetup.DisplayOrder))
            .Select(r => new GetGroceryCategoryRulesResponse(
                r.Id, r.CategoryId, r.Pattern, r.Category.Name, r.Category.Color, r.Value, r.MatchWholeDescription))
            .ToListAsync(ct);
    }
}
