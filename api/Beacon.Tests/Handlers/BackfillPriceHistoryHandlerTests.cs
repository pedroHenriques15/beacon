using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Commands.BackfillPriceHistory;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Beacon.Tests.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Beacon.Tests.Handlers;

public class BackfillPriceHistoryHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static BackfillPriceHistoryCommandHandler MakeHandler(AppDbContext db, HttpMessageHandler httpHandler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AlphaVantage:ApiKey"] = "test-key" })
            .Build();
        return new BackfillPriceHistoryCommandHandler(db, new AlphaVantageService(new FakeHttpClientFactory(httpHandler), config));
    }

    private static async Task<InvestmentAsset> SeedEtfWithLotAsync(AppDbContext db, DateOnly lotDate)
    {
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = "VWCE", Name = "Vanguard FTSE All-World" };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        db.InvestmentLots.Add(new InvestmentLot { AssetId = asset.Id, Date = lotDate, Quantity = 10, PricePerUnit = 100 });
        await db.SaveChangesAsync();
        return asset;
    }

    private static string EtfSeries(params (string date, string close)[] days) =>
        """{"Time Series (Daily)": {""" +
        string.Join(",", days.Select(d => $"\"{d.date}\": {{\"4. close\": \"{d.close}\"}}")) +
        "}}";

    [Fact]
    public async Task BackfillHistory_AssetNotFound_ReturnsNullNull()
    {
        await using var db = CreateDb(nameof(BackfillHistory_AssetNotFound_ReturnsNullNull));

        var (result, error) = await MakeHandler(db, new ThrowingHttpMessageHandler())
            .HandleAsync(new BackfillPriceHistoryCommand(9999));

        Assert.Null(result);
        Assert.Null(error);
    }

    [Fact]
    public async Task BackfillHistory_NoLots_ReturnsError()
    {
        await using var db = CreateDb(nameof(BackfillHistory_NoLots_ReturnsError));
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = "VWCE", Name = "Vanguard" };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();

        var (result, error) = await MakeHandler(db, new ThrowingHttpMessageHandler())
            .HandleAsync(new BackfillPriceHistoryCommand(asset.Id));

        Assert.Null(result);
        Assert.Contains("at least one lot", error);
    }

    [Fact]
    public async Task BackfillHistory_Etf_InsertsCloses()
    {
        await using var db = CreateDb(nameof(BackfillHistory_Etf_InsertsCloses));
        var asset = await SeedEtfWithLotAsync(db, new DateOnly(2026, 1, 5));
        var body = EtfSeries(("2026-01-05", "100.00"), ("2026-01-06", "101.50"), ("2026-01-07", "99.75"));

        var (result, error) = await MakeHandler(db, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body))
            .HandleAsync(new BackfillPriceHistoryCommand(asset.Id));

        Assert.Null(error);
        Assert.Equal(3, result!.SnapshotsAdded);
        Assert.Equal(new DateOnly(2026, 1, 5), result.EarliestDate);
        Assert.Equal(new DateOnly(2026, 1, 7), result.LatestDate);
        Assert.Equal(3, await db.InvestmentPriceSnapshots.CountAsync());
        var snap = await db.InvestmentPriceSnapshots.SingleAsync(p => p.Date == new DateOnly(2026, 1, 6));
        Assert.Equal(101.50m, snap.PricePerUnit);
    }

    [Fact]
    public async Task BackfillHistory_Gold_ConvertsTroyOunceToGrams()
    {
        await using var db = CreateDb(nameof(BackfillHistory_Gold_ConvertsTroyOunceToGrams));
        var asset = new InvestmentAsset { AssetType = "Gold", Name = "Physical Gold" };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        db.InvestmentLots.Add(new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2026, 1, 5), Quantity = 50, PricePerUnit = 70 });
        await db.SaveChangesAsync();
        var body = """{"Time Series FX (Daily)": {"2026-01-06": {"4. close": "3110.35"}}}""";

        var (result, error) = await MakeHandler(db, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body))
            .HandleAsync(new BackfillPriceHistoryCommand(asset.Id));

        Assert.Null(error);
        Assert.Equal(1, result!.SnapshotsAdded);
        var snap = await db.InvestmentPriceSnapshots.SingleAsync();
        Assert.Equal(100.0000m, snap.PricePerUnit);
    }

    [Fact]
    public async Task BackfillHistory_SkipsExistingDates()
    {
        await using var db = CreateDb(nameof(BackfillHistory_SkipsExistingDates));
        var asset = await SeedEtfWithLotAsync(db, new DateOnly(2026, 3, 1));
        db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot
        {
            AssetId = asset.Id, Date = new DateOnly(2026, 3, 20), PricePerUnit = 105m
        });
        await db.SaveChangesAsync();
        var body = EtfSeries(("2026-03-19", "104.00"), ("2026-03-20", "999.99"), ("2026-03-21", "106.00"));

        var (result, error) = await MakeHandler(db, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body))
            .HandleAsync(new BackfillPriceHistoryCommand(asset.Id));

        Assert.Null(error);
        Assert.Equal(2, result!.SnapshotsAdded);
        Assert.Equal(1, result.SnapshotsSkipped);
        var existing = await db.InvestmentPriceSnapshots.SingleAsync(p => p.Date == new DateOnly(2026, 3, 20));
        Assert.Equal(105m, existing.PricePerUnit);
    }

    [Fact]
    public async Task BackfillHistory_SkipsDatesBeforeFirstLot()
    {
        await using var db = CreateDb(nameof(BackfillHistory_SkipsDatesBeforeFirstLot));
        var asset = await SeedEtfWithLotAsync(db, new DateOnly(2026, 2, 10));
        var body = EtfSeries(("2026-02-08", "95.00"), ("2026-02-09", "96.00"), ("2026-02-10", "97.00"), ("2026-02-11", "98.00"));

        var (result, error) = await MakeHandler(db, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body))
            .HandleAsync(new BackfillPriceHistoryCommand(asset.Id));

        Assert.Null(error);
        Assert.Equal(2, result!.SnapshotsAdded);
        Assert.Equal(new DateOnly(2026, 2, 10), result.EarliestDate);
        Assert.False(await db.InvestmentPriceSnapshots.AnyAsync(p => p.Date < new DateOnly(2026, 2, 10)));
    }

    [Fact]
    public async Task BackfillHistory_SkipsTodayAndFutureDates()
    {
        await using var db = CreateDb(nameof(BackfillHistory_SkipsTodayAndFutureDates));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var asset = await SeedEtfWithLotAsync(db, today.AddDays(-3));
        var body = EtfSeries(
            (today.AddDays(-1).ToString("yyyy-MM-dd"), "100.00"),
            (today.ToString("yyyy-MM-dd"), "101.00"),
            (today.AddDays(1).ToString("yyyy-MM-dd"), "102.00"));

        var (result, error) = await MakeHandler(db, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body))
            .HandleAsync(new BackfillPriceHistoryCommand(asset.Id));

        Assert.Null(error);
        Assert.Equal(1, result!.SnapshotsAdded);
        Assert.False(await db.InvestmentPriceSnapshots.AnyAsync(p => p.Date >= today));
    }

    [Fact]
    public async Task BackfillHistory_RateLimitResponse_ReturnsError()
    {
        await using var db = CreateDb(nameof(BackfillHistory_RateLimitResponse_ReturnsError));
        var asset = await SeedEtfWithLotAsync(db, new DateOnly(2026, 1, 5));
        var body = """{"Note": "API call frequency reached"}""";

        var (result, error) = await MakeHandler(db, new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, body))
            .HandleAsync(new BackfillPriceHistoryCommand(asset.Id));

        Assert.Null(result);
        Assert.Contains("rate limit", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BackfillHistory_AlreadyCovered_MakesNoApiCall()
    {
        await using var db = CreateDb(nameof(BackfillHistory_AlreadyCovered_MakesNoApiCall));
        var asset = await SeedEtfWithLotAsync(db, new DateOnly(2026, 1, 5));
        db.InvestmentPriceSnapshots.AddRange(
            new InvestmentPriceSnapshot { AssetId = asset.Id, Date = new DateOnly(2026, 1, 6), PricePerUnit = 100m },
            new InvestmentPriceSnapshot { AssetId = asset.Id, Date = new DateOnly(2026, 2, 1), PricePerUnit = 104m });
        await db.SaveChangesAsync();

        var (result, error) = await MakeHandler(db, new ThrowingHttpMessageHandler())
            .HandleAsync(new BackfillPriceHistoryCommand(asset.Id));

        Assert.Null(error);
        Assert.Equal(0, result!.SnapshotsAdded);
        Assert.Contains("already covers", result.Message);
    }
}
