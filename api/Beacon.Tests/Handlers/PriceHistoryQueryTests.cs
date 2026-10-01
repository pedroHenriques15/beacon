using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Queries.GetAssetPrices;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Beacon.Api.Features.Investments.Queries.GetPriceHistory;
using Beacon.Api.Features.Investments.Queries.GetPriceSyncStatus;
using Beacon.Api.Models;
using Beacon.Tests.Services;

namespace Beacon.Tests.Handlers;

/// <summary>Years of daily closes: the asset list carries what the metrics need, the rest is asked for.</summary>
public class PriceHistoryQueryTests : IDisposable
{
    private static readonly DateOnly Latest = new(2026, 9, 30);

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private AppDbContext CreateDb() => _database.CreateContext();

    /// <summary>An asset with a lot on <paramref name="firstLot"/> and a close every day for two years.</summary>
    private static async Task<InvestmentAsset> SeedDailyAsync(AppDbContext db, string ticker, DateOnly firstLot)
    {
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = ticker, Name = ticker };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        db.InvestmentLots.Add(new InvestmentLot { AssetId = asset.Id, Date = firstLot, Quantity = 1, PricePerUnit = 100 });
        for (var date = Latest.AddYears(-2); date <= Latest; date = date.AddDays(1))
            db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot
            {
                AssetId = asset.Id,
                Date = date,
                PricePerUnit = 100 + date.DayNumber % 50,
                Source = PriceSources.Synced,
            });
        await db.SaveChangesAsync();
        return asset;
    }

    [Fact]
    public async Task AssetList_CarriesTheLastFortyDaysAndThePriceAtPurchase()
    {
        await using var db = CreateDb();
        var firstLot = Latest.AddYears(-1);
        await SeedDailyAsync(db, "VWCE.DE", firstLot);

        var asset = Assert.Single(await new GetInvestmentAssetsQueryHandler(db).HandleAsync());

        Assert.Equal(731, asset.PriceCount);
        Assert.Equal(RecentPrices.Days + 2, asset.PriceSnapshots.Count);
        Assert.Equal(Latest, asset.PriceSnapshots[0].Date);
        Assert.Equal(Latest.AddDays(-RecentPrices.Days), asset.PriceSnapshots[^2].Date);
        Assert.Equal(firstLot, asset.PriceSnapshots[^1].Date);
    }

    [Fact]
    public async Task RecentPrices_ReachBackAcrossAGap_ForEachReferenceDate()
    {
        // Prices on the latest day and two months earlier only: the 1-week, 1-month and
        // since-purchase changes all read the older one.
        await using var db = CreateDb();
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = "GAP.DE", Name = "Gap" };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        db.InvestmentLots.Add(new InvestmentLot { AssetId = asset.Id, Date = Latest.AddDays(-50), Quantity = 1, PricePerUnit = 100 });
        foreach (var (date, price) in new[] { (Latest.AddDays(-90), 90m), (Latest.AddDays(-60), 100m), (Latest, 120m) })
            db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot
            {
                AssetId = asset.Id,
                Date = date,
                PricePerUnit = price,
                Source = PriceSources.Synced,
            });
        await db.SaveChangesAsync();

        var listed = Assert.Single(await new GetInvestmentAssetsQueryHandler(db).HandleAsync());

        Assert.Equal(3, listed.PriceCount);
        Assert.Equal([Latest, Latest.AddDays(-60)], listed.PriceSnapshots.Select(p => p.Date));
    }

    [Fact]
    public async Task AssetPrices_ReturnEveryPriceNewestFirst()
    {
        await using var db = CreateDb();
        var asset = await SeedDailyAsync(db, "VWCE.DE", Latest.AddYears(-1));

        var prices = await new GetAssetPricesQueryHandler(db).HandleAsync(new GetAssetPricesQuery(asset.Id));

        Assert.Equal(731, prices!.Count);
        Assert.Equal(Latest, prices[0].Date);
        Assert.Equal(Latest.AddYears(-2), prices[^1].Date);
    }

    [Fact]
    public async Task AssetPrices_UnknownAsset_IsNull()
    {
        await using var db = CreateDb();

        Assert.Null(await new GetAssetPricesQueryHandler(db).HandleAsync(new GetAssetPricesQuery(9999)));
    }

    [Fact]
    public async Task PriceHistory_GroupsByAssetOldestFirst_FromTheGivenDate()
    {
        await using var db = CreateDb();
        var a = await SeedDailyAsync(db, "A.DE", Latest.AddYears(-1));
        var b = await SeedDailyAsync(db, "B.DE", Latest.AddYears(-1));
        var from = Latest.AddDays(-9);

        var series = await new GetPriceHistoryQueryHandler(db).HandleAsync(new GetPriceHistoryQuery(from));

        Assert.Equal([a.Id, b.Id], series.Select(s => s.AssetId));
        Assert.All(series, s =>
        {
            // The day before the range carries its price in, then every day of the range.
            Assert.Equal(11, s.Dates.Count);
            Assert.Equal(from.AddDays(-1), s.Dates[0]);
            Assert.Equal(from, s.Dates[1]);
            Assert.Equal(s.Dates.Count, s.Prices.Count);
        });
    }

    [Fact]
    public async Task PriceHistory_StartsEachAssetWithItsLatestPriceBeforeTheRange()
    {
        await using var db = CreateDb();
        var daily = await SeedDailyAsync(db, "A.DE", Latest.AddYears(-1));
        var sparse = new InvestmentAsset { AssetType = "Gold", Name = "Gold" };
        db.InvestmentAssets.Add(sparse);
        await db.SaveChangesAsync();
        foreach (var (date, price) in new[] { (Latest.AddDays(-80), 60m), (Latest.AddDays(-50), 61m), (Latest.AddDays(-5), 62m) })
            db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot
            {
                AssetId = sparse.Id,
                Date = date,
                PricePerUnit = price,
                Source = PriceSources.Manual,
            });
        await db.SaveChangesAsync();
        var from = Latest.AddDays(-30);

        var series = await new GetPriceHistoryQueryHandler(db).HandleAsync(new GetPriceHistoryQuery(from));

        var gold = Assert.Single(series, s => s.AssetId == sparse.Id);
        Assert.Equal([Latest.AddDays(-50), Latest.AddDays(-5)], gold.Dates);
        Assert.Equal([61m, 62m], gold.Prices);
        Assert.Equal(from.AddDays(-1), Assert.Single(series, s => s.AssetId == daily.Id).Dates[0]);
    }

    [Fact]
    public async Task PriceHistory_WithoutADate_ReturnsEverything()
    {
        await using var db = CreateDb();
        await SeedDailyAsync(db, "A.DE", Latest.AddYears(-1));

        var series = await new GetPriceHistoryQueryHandler(db).HandleAsync(new GetPriceHistoryQuery(null));

        Assert.Equal(731, Assert.Single(series).Dates.Count);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("false", false)]
    public void SyncStatus_FollowsTheSetting(string? enabled, bool expected)
    {
        var config = enabled is null ? TestPricing.Config() : TestPricing.Config(("Prices:Enabled", enabled));

        Assert.Equal(expected, new GetPriceSyncStatusQueryHandler(config).Handle(new GetPriceSyncStatusQuery()).Enabled);
    }
}
