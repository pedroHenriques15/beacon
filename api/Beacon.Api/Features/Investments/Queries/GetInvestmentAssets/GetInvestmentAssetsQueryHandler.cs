using Beacon.Api.Data;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;

public record InvestmentLotResponse(
    int Id,
    int AssetId,
    DateOnly Date,
    decimal Quantity,
    decimal PricePerUnit,
    decimal? Fees,
    string? Notes);

public record InvestmentPriceSnapshotResponse(
    int Id,
    int AssetId,
    DateOnly Date,
    decimal PricePerUnit,
    string Source);

/// <summary>
/// An asset with its lots and only the prices its metrics need (see <see cref="RecentPrices"/>);
/// <see cref="PriceCount"/> counts them all. The full history is served by GetAssetPrices and
/// GetPriceHistory.
/// </summary>
public record InvestmentAssetResponse(
    int Id,
    string AssetType,
    string? Ticker,
    string? Isin,
    string Name,
    string? Notes,
    DateTime? PricesSyncedAt,
    string? PriceSyncError,
    int PriceCount,
    List<InvestmentLotResponse> Lots,
    List<InvestmentPriceSnapshotResponse> PriceSnapshots);

public class GetInvestmentAssetsQueryHandler(AppDbContext db)
{
    public async Task<List<InvestmentAssetResponse>> HandleAsync(CancellationToken ct = default)
    {
        var assets = await db.InvestmentAssets
            .AsSplitQuery()
            .OrderBy(a => EF.Functions.Collate(a.Name, SqliteSetup.DisplayOrder))
            .Select(a => new
            {
                a.Id,
                a.AssetType,
                a.Ticker,
                a.Isin,
                a.Name,
                a.Notes,
                a.PricesSyncedAt,
                a.PriceSyncError,
                Lots = a.Lots
                    .OrderByDescending(l => l.Date)
                    .Select(l => new InvestmentLotResponse(l.Id, l.AssetId, l.Date, l.Quantity, l.PricePerUnit, l.Fees, l.Notes))
                    .ToList(),
            })
            .ToListAsync(ct);

        var prices = await RecentPrices.LoadAsync(
            db, assets.ToDictionary(a => a.Id, a => a.Lots.Select(l => (DateOnly?)l.Date).Min()), ct);

        return assets
            .Select(a =>
            {
                var (count, recent) = prices.GetValueOrDefault(a.Id, (0, []));
                return new InvestmentAssetResponse(
                    a.Id, a.AssetType, a.Ticker, a.Isin, a.Name, a.Notes, a.PricesSyncedAt, a.PriceSyncError,
                    count, a.Lots, recent);
            })
            .ToList();
    }
}

/// <summary>
/// The prices an asset's metrics read, out of years of daily closes: the last <see cref="Days"/>
/// days before its latest price, and the latest price at or before each date a change is measured
/// from (a week and a month before the latest price, and the first lot) when that one is older.
/// </summary>
public static class RecentPrices
{
    public const int Days = 40;

    /// <param name="firstLotByAsset">The assets to load, each with the date of its first lot.</param>
    /// <returns>Per asset with prices: how many it has, and the recent ones, newest first.</returns>
    public static async Task<Dictionary<int, (int Count, List<InvestmentPriceSnapshotResponse> Recent)>> LoadAsync(
        AppDbContext db, IReadOnlyDictionary<int, DateOnly?> firstLotByAsset, CancellationToken ct = default)
    {
        var ids = firstLotByAsset.Keys.ToList();
        var stats = await db.InvestmentPriceSnapshots
            .Where(p => ids.Contains(p.AssetId))
            .GroupBy(p => p.AssetId)
            .Select(g => new { AssetId = g.Key, Count = g.Count(), Latest = g.Max(p => p.Date) })
            .ToListAsync(ct);

        // A query or two per asset: a handful of assets, each reading its own index range.
        var result = new Dictionary<int, (int, List<InvestmentPriceSnapshotResponse>)>();
        foreach (var asset in stats)
        {
            var since = asset.Latest.AddDays(-Days);
            var recent = await Project(db.InvestmentPriceSnapshots
                    .Where(p => p.AssetId == asset.AssetId && p.Date >= since))
                .ToListAsync(ct);

            var references = new List<DateOnly> { asset.Latest.AddDays(-7), asset.Latest.AddDays(-30) };
            if (firstLotByAsset[asset.AssetId] is DateOnly firstLot) references.Add(firstLot);

            foreach (var date in references.Distinct())
            {
                if (recent.Any(p => p.Date <= date)) continue; // the window already holds it
                var price = await Project(db.InvestmentPriceSnapshots
                        .Where(p => p.AssetId == asset.AssetId && p.Date <= date))
                    .FirstOrDefaultAsync(ct);
                if (price is not null && !recent.Contains(price)) recent.Add(price);
            }

            result[asset.AssetId] = (asset.Count, recent.OrderByDescending(p => p.Date).ToList());
        }

        return result;
    }

    private static IQueryable<InvestmentPriceSnapshotResponse> Project(IQueryable<InvestmentPriceSnapshot> prices) =>
        prices
            .OrderByDescending(p => p.Date)
            .Select(p => new InvestmentPriceSnapshotResponse(p.Id, p.AssetId, p.Date, p.PricePerUnit, p.Source));
}
