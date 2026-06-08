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

        if (!string.IsNullOrEmpty(query.Store))
            q = q.Where(i => i.Receipt.StoreName.Contains(query.Store));

        if (!string.IsNullOrEmpty(query.Month) && DateOnly.TryParse(query.Month + "-01", out var md))
            q = q.Where(i => i.Receipt.ReceiptDate.Year == md.Year && i.Receipt.ReceiptDate.Month == md.Month);

        if (query.CategoryId.HasValue)
            q = q.Where(i => i.CategoryId == query.CategoryId.Value);

        if (!string.IsNullOrEmpty(query.Search))
            q = q.Where(i => i.Description.Contains(query.Search));

        var take       = Math.Clamp(query.Take, 1, 5000);
        var totalCount = await q.CountAsync(ct);
        var totalAmount = await q.SumAsync(i => i.Amount, ct);

        var ordered = (query.SortCol?.ToLower(), query.SortDir?.ToLower()) switch
        {
            ("store", "asc")       => q.OrderBy(i => i.Receipt.StoreName).ThenByDescending(i => i.Id),
            ("store", _)           => q.OrderByDescending(i => i.Receipt.StoreName).ThenByDescending(i => i.Id),
            ("description", "asc") => q.OrderBy(i => i.Description).ThenByDescending(i => i.Id),
            ("description", _)     => q.OrderByDescending(i => i.Description).ThenByDescending(i => i.Id),
            ("category", "asc")    => q.OrderBy(i => i.Category == null ? "zzz" : i.Category.Name).ThenByDescending(i => i.Id),
            ("category", _)        => q.OrderByDescending(i => i.Category == null ? "" : i.Category.Name).ThenByDescending(i => i.Id),
            ("amount", "asc")      => q.OrderBy(i => i.Amount).ThenByDescending(i => i.Id),
            ("amount", _)          => q.OrderByDescending(i => i.Amount).ThenByDescending(i => i.Id),
            ("date", "asc")        => q.OrderBy(i => i.Receipt.ReceiptDate).ThenBy(i => i.Id),
            _                      => q.OrderByDescending(i => i.Receipt.ReceiptDate).ThenByDescending(i => i.Id),
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
                i.CategorySetManually))
            .ToListAsync(ct);

        return new PagedGroceryItemsResult(items, totalCount, totalAmount);
    }
}
