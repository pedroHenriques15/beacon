using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Categories.Queries.GetCategories;

public class GetCategoriesQueryHandler(AppDbContext db, ILogger<GetCategoriesQueryHandler> logger)
{
    public async Task<List<GetCategoriesResponse>> HandleAsync(CancellationToken ct = default)
    {
        logger.LogInformation("GetCategories");
        return await db.Categories
            .Include(c => c.Rules)
            .OrderBy(c => EF.Functions.Collate(c.Name, SqliteSetup.DisplayOrder))
            .Select(c => new GetCategoriesResponse(
                c.Id, c.Name, c.Color, c.IsProtected,
                c.Rules.Select(r => new RuleDto(r.Id, r.CategoryId, r.Pattern, r.MatchWholeDescription)).ToList()))
            .ToListAsync(ct);
    }
}
