using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Salary.Queries.GetSalaryItemCategories;

public record SalaryItemCategoryResponse(int Id, int ProfileId, string Name, string Color, string ItemType, bool IsProtected);

public class GetSalaryItemCategoriesQueryHandler(AppDbContext db)
{
    public async Task<List<SalaryItemCategoryResponse>> HandleAsync(GetSalaryItemCategoriesQuery query, CancellationToken ct = default) =>
        await db.SalaryItemCategories
            .Where(c => c.SalaryProfileId == query.ProfileId)
            .OrderBy(c => c.ItemType)
            .ThenBy(c => c.Name)
            .Select(c => new SalaryItemCategoryResponse(c.Id, c.SalaryProfileId, c.Name, c.Color, c.ItemType, c.IsProtected))
            .ToListAsync(ct);
}
