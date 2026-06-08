using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.GroceryCategories.Queries.GetGroceryReceiptCategoryMappings;

public record GroceryReceiptCategoryMappingResponse(int Id, string ReceiptCategoryName, int GroceryCategoryId, string CategoryName);

public class GetGroceryReceiptCategoryMappingsQueryHandler(AppDbContext db)
{
    public async Task<List<GroceryReceiptCategoryMappingResponse>> HandleAsync(CancellationToken ct = default)
    {
        return await db.GroceryReceiptCategoryMappings
            .Include(m => m.Category)
            .Select(m => new GroceryReceiptCategoryMappingResponse(
                m.Id,
                m.ReceiptCategoryName,
                m.GroceryCategoryId,
                m.Category.Name))
            .ToListAsync(ct);
    }
}
