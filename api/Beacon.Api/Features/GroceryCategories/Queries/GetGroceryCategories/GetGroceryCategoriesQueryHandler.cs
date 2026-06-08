using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.GroceryCategories.Queries.GetGroceryCategories;

public class GetGroceryCategoriesQueryHandler(AppDbContext db, ILogger<GetGroceryCategoriesQueryHandler> logger)
{
    public async Task<List<GetGroceryCategoriesResponse>> HandleAsync(CancellationToken ct = default)
    {
        logger.LogInformation("GetGroceryCategories");
        return await db.GroceryCategories
            .Include(c => c.Rules)
            .OrderBy(c => c.Name)
            .Select(c => new GetGroceryCategoriesResponse(
                c.Id, c.Name, c.Color, c.IsProtected,
                c.Rules.Select(r => new GroceryRuleDto(r.Id, r.CategoryId, r.Pattern, r.Value)).ToList()))
            .ToListAsync(ct);
    }
}
