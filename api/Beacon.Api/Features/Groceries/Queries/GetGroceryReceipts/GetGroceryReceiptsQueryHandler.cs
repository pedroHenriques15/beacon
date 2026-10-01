using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Groceries.Queries.GetGroceryReceipts;

public class GetGroceryReceiptsQueryHandler(AppDbContext db, ILogger<GetGroceryReceiptsQueryHandler> logger)
{
    public async Task<List<GroceryReceiptSummary>> HandleAsync(string? store, CancellationToken ct = default)
    {
        logger.LogInformation("GetGroceryReceipts: store={Store}", store);
        var q = db.GroceryReceipts.AsQueryable();

        if (!string.IsNullOrEmpty(store))
        {
            // Case-insensitive, accented letters included: lower() is .NET's (SqliteSetup).
            var storeLower = store.ToLowerInvariant();
            q = q.Where(r => r.StoreName.ToLower().Contains(storeLower));
        }

        return await q
            .OrderByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.Id)
            .Select(r => new GroceryReceiptSummary(
                r.Id,
                r.StoreName,
                r.ReceiptDate,
                r.Total,
                r.Items.Count,
                r.SourceFile))
            .ToListAsync(ct);
    }
}
