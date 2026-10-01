using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Groceries.Queries.GetGroceryItems;

public class GetGroceryItemsQueryHandler(AppDbContext db, ILogger<GetGroceryItemsQueryHandler> logger)
{
    public async Task<PagedGroceryItemsResult> HandleAsync(GetGroceryItemsQuery query, CancellationToken ct = default)
    {
        logger.LogInformation("GetGroceryItems: receiptId={ReceiptId} store={Store} month={Month} skip={Skip} take={Take}",
            query.ReceiptId, query.Store, query.Month, query.Skip, query.Take);

        var q = db.GroceryItems
            .Include(i => i.Receipt)
            .Include(i => i.Category)
            .AsQueryable();

        if (query.ReceiptId.HasValue)
            q = q.Where(i => i.ReceiptId == query.ReceiptId.Value);

        // Case-insensitive, accented letters included: lower() is .NET's (SqliteSetup).
        if (!string.IsNullOrEmpty(query.Store))
        {
            var store = query.Store.ToLowerInvariant();
            q = q.Where(i => i.Receipt.StoreName.ToLower().Contains(store));
        }

        if (!string.IsNullOrEmpty(query.Month) && DateOnly.TryParse(query.Month + "-01", out var md))
            q = q.Where(i => i.Receipt.ReceiptDate.Year == md.Year && i.Receipt.ReceiptDate.Month == md.Month);

        if (query.CategoryId.HasValue)
            q = q.Where(i => i.CategoryId == query.CategoryId.Value);

        if (!string.IsNullOrEmpty(query.Search))
        {
            var search = query.Search.ToLowerInvariant();
            q = q.Where(i => i.Description.ToLower().Contains(search));
        }

        var take = Math.Clamp(query.Take, 1, 5000);
        var totalCount = await q.CountAsync(ct);
        var totalAmount = await q.SumAsync(i => i.Amount, ct);

        // Text sorts use the display collation, so accented letters sit beside their base letter.
        var ordered = (query.SortCol?.ToLower(), query.SortDir?.ToLower()) switch
        {
            ("store", "asc") => q.OrderBy(i => EF.Functions.Collate(i.Receipt.StoreName, SqliteSetup.DisplayOrder)).ThenByDescending(i => i.Id),
            ("store", _) => q.OrderByDescending(i => EF.Functions.Collate(i.Receipt.StoreName, SqliteSetup.DisplayOrder)).ThenByDescending(i => i.Id),
            ("description", "asc") => q.OrderBy(i => EF.Functions.Collate(i.Description, SqliteSetup.DisplayOrder)).ThenByDescending(i => i.Id),
            ("description", _) => q.OrderByDescending(i => EF.Functions.Collate(i.Description, SqliteSetup.DisplayOrder)).ThenByDescending(i => i.Id),
            ("category", "asc") => q.OrderBy(i => EF.Functions.Collate(i.Category == null ? "zzz" : i.Category.Name, SqliteSetup.DisplayOrder)).ThenByDescending(i => i.Id),
            ("category", _) => q.OrderByDescending(i => EF.Functions.Collate(i.Category == null ? "" : i.Category.Name, SqliteSetup.DisplayOrder)).ThenByDescending(i => i.Id),
            ("amount", "asc") => q.OrderBy(i => i.Amount).ThenByDescending(i => i.Id),
            ("amount", _) => q.OrderByDescending(i => i.Amount).ThenByDescending(i => i.Id),
            ("date", "asc") => q.OrderBy(i => i.Receipt.ReceiptDate).ThenBy(i => i.Id),
            _ => q.OrderByDescending(i => i.Receipt.ReceiptDate).ThenByDescending(i => i.Id),
        };

        var items = await ordered
            .Skip(query.Skip)
            .Take(take)
            .Select(i => new GroceryItemResponse(
                i.Id,
                i.ReceiptId,
                i.Receipt.StoreName,
                i.Receipt.ReceiptDate,
                i.Description,
                i.Amount,
                i.Quantity,
                i.CategoryId,
                i.Category == null ? null : i.Category.Name,
                i.Category == null ? null : i.Category.Color,
                i.CategorySetManually,
                i.IsExcluded))
            .ToListAsync(ct);

        return new PagedGroceryItemsResult(items, totalCount, totalAmount);
    }
}
