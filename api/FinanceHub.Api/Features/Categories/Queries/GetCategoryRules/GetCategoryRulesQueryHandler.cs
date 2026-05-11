using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Categories.Queries.GetCategoryRules;

public class GetCategoryRulesQueryHandler(AppDbContext db, ILogger<GetCategoryRulesQueryHandler> logger)
{
    public async Task<List<GetCategoryRulesResponse>> HandleAsync(CancellationToken ct = default)
    {
        logger.LogInformation("GetCategoryRules");
        return await db.CategoryRules
            .Include(r => r.Category)
            .OrderBy(r => r.Category.Name).ThenBy(r => r.Pattern)
            .Select(r => new GetCategoryRulesResponse(
                r.Id, r.CategoryId, r.Pattern, r.Category.Name, r.Category.Color, r.Value))
            .ToListAsync(ct);
    }
}
