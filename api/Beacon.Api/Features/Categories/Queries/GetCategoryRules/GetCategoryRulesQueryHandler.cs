using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Categories.Queries.GetCategoryRules;

public class GetCategoryRulesQueryHandler(AppDbContext db, ILogger<GetCategoryRulesQueryHandler> logger)
{
    public async Task<List<GetCategoryRulesResponse>> HandleAsync(CancellationToken ct = default)
    {
        logger.LogInformation("GetCategoryRules");
        return await db.CategoryRules
            .Include(r => r.Category)
            .OrderBy(r => EF.Functions.Collate(r.Category.Name, SqliteSetup.DisplayOrder)).ThenBy(r => EF.Functions.Collate(r.Pattern, SqliteSetup.DisplayOrder))
            .Select(r => new GetCategoryRulesResponse(
                r.Id, r.CategoryId, r.Pattern, r.Category.Name, r.Category.Color, r.Value, r.MatchWholeDescription))
            .ToListAsync(ct);
    }
}
