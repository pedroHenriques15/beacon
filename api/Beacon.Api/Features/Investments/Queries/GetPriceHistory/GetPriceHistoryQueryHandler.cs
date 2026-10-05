using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Queries.GetPriceHistory;

/// <summary>Every asset's prices from <see cref="From"/> on, or all of them.</summary>
public record GetPriceHistoryQuery(DateOnly? From);

/// <summary>One asset's prices, oldest first, as two parallel lists: compact for years of daily closes.</summary>
public record AssetPriceSeries(int AssetId, List<DateOnly> Dates, List<decimal> Prices);

/// <summary>
/// The portfolio value chart's prices. With a start date, each asset's series also starts with its
/// latest price before that date, so a position priced only now and then is valued from the start
/// of the range rather than from its first price inside it.
/// </summary>
public class GetPriceHistoryQueryHandler(AppDbContext db)
{
    public async Task<List<AssetPriceSeries>> HandleAsync(GetPriceHistoryQuery query, CancellationToken ct = default)
    {
        var prices = db.InvestmentPriceSnapshots.AsQueryable();
        if (query.From is DateOnly from)
            prices = prices.Where(p => p.Date >= from
                || p.Date == db.InvestmentPriceSnapshots
                    .Where(q => q.AssetId == p.AssetId && q.Date < from)
                    .Max(q => (DateOnly?)q.Date));

        var rows = await prices
            .OrderBy(p => p.AssetId).ThenBy(p => p.Date)
            .Select(p => new { p.AssetId, p.Date, p.PricePerUnit })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.AssetId)
            .Select(g => new AssetPriceSeries(g.Key, g.Select(r => r.Date).ToList(), g.Select(r => r.PricePerUnit).ToList()))
            .ToList();
    }
}
