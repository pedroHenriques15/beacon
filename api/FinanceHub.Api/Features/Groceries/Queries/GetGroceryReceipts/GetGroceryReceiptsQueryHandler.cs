using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Groceries.Queries.GetGroceryReceipts;

public class GetGroceryReceiptsQueryHandler(AppDbContext db, ILogger<GetGroceryReceiptsQueryHandler> logger)
{
    public async Task<List<GroceryReceiptSummary>> HandleAsync(string? store, CancellationToken ct = default)
    {
        logger.LogInformation("GetGroceryReceipts: store={Store}", store);
        var q = db.GroceryReceipts.AsQueryable();

        if (!string.IsNullOrEmpty(store))
            q = q.Where(r => r.StoreName.Contains(store));

        return await q
            .OrderByDescending(r => r.ReceiptDate)
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
