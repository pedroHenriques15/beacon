using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Commands.SyncPriceHistory;
using Beacon.Api.Models;
using Beacon.Tests.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beacon.Tests.Handlers;

public class SyncPriceHistoryHandlerTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 9, 30);

    private readonly SqliteTestDatabase _database = new();
    private readonly StubPriceHistorySource _source = new();

    public void Dispose() => _database.Dispose();

    private AppDbContext CreateDb() => _database.CreateContext();

    private SyncPriceHistoryCommandHandler MakeHandler(AppDbContext db, ILogger<SyncPriceHistoryCommandHandler>? logger = null) =>
        new(db, _source, TestPricing.Config(),
            new FixedTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(22, 0)), TimeSpan.Zero)),
            logger ?? NullLogger<SyncPriceHistoryCommandHandler>.Instance);

    private static async Task<InvestmentAsset> SeedHeldAsync(
        AppDbContext db, string type = "ETF", string? ticker = "VWCE.DE", string? isin = null, string name = "World ETF",
        decimal quantity = 10)
    {
        var asset = new InvestmentAsset { AssetType = type, Ticker = ticker, Isin = isin, Name = name };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        db.InvestmentLots.Add(new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2024, 1, 2), Quantity = quantity, PricePerUnit = 100 });
        await db.SaveChangesAsync();
        return asset;
    }

    private static async Task AddPriceAsync(AppDbContext db, int assetId, DateOnly date, decimal price, string source)
    {
        db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot { AssetId = assetId, Date = date, PricePerUnit = price, Source = source });
        await db.SaveChangesAsync();
    }

    private void SetSeries(string symbol, params (DateOnly Date, decimal Close)[] closes) =>
        _source.Series[symbol] = closes.ToDictionary(c => c.Date, c => c.Close);

    [Fact]
    public async Task FirstSync_StoresFifteenYearsAndNothingOlder()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db);
        var windowStart = Today.AddYears(-15);
        SetSeries("VWCE.DE", (windowStart.AddDays(-1), 50m), (windowStart, 51m), (Today.AddDays(-1), 160m), (Today, 161m));

        var result = await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand());

        var request = Assert.Single(_source.Requests);
        Assert.Equal(("VWCE.DE", windowStart, Today), request);
        var synced = Assert.Single(result!.Assets);
        Assert.Equal(3, synced.Added);
        Assert.Null(synced.Error);
        Assert.Equal(Today, synced.LatestClose);

        await using var check = CreateDb();
        var prices = await check.InvestmentPriceSnapshots.OrderBy(p => p.Date).ToListAsync();
        Assert.Equal([windowStart, Today.AddDays(-1), Today], prices.Select(p => p.Date));
        Assert.All(prices, p => Assert.Equal(PriceSources.Synced, p.Source));
        var stored = await check.InvestmentAssets.SingleAsync(a => a.Id == asset.Id);
        Assert.NotNull(stored.PricesSyncedAt);
        Assert.Null(stored.PriceSyncError);
        Assert.Equal("VWCE.DE", stored.PricesSymbol);
    }

    [Fact]
    public async Task LaterSync_StartsAWeekBeforeTheLatestSyncedCloseAndFillsTheGap()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db);
        var latest = Today.AddDays(-10);
        await AddPriceAsync(db, asset.Id, latest, 150m, PriceSources.Synced);
        SetSeries("VWCE.DE", (latest, 150m), (Today.AddDays(-5), 152m), (Today, 155m));

        var result = await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand());

        Assert.Equal(latest.AddDays(-7), Assert.Single(_source.Requests).From);
        var synced = Assert.Single(result!.Assets);
        Assert.Equal(2, synced.Added);
        Assert.Equal(0, synced.Replaced);
        await using var check = CreateDb();
        Assert.Equal(3, await check.InvestmentPriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task Sync_ReplacesSyncedAndLegacyPricesButKeepsManualOnes()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db);
        var d1 = Today.AddDays(-3);
        var d2 = Today.AddDays(-2);
        var d3 = Today.AddDays(-1);
        await AddPriceAsync(db, asset.Id, d1, 99m, PriceSources.Legacy);
        await AddPriceAsync(db, asset.Id, d2, 98m, PriceSources.Manual);
        await AddPriceAsync(db, asset.Id, d3, 97m, PriceSources.Synced);
        SetSeries("VWCE.DE", (d1, 101m), (d2, 102m), (d3, 103m));

        var result = await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand(asset.Id));

        var synced = Assert.Single(result!.Assets);
        Assert.Equal(2, synced.Replaced);
        Assert.Equal(1, synced.Kept);
        await using var check = CreateDb();
        var prices = await check.InvestmentPriceSnapshots.OrderBy(p => p.Date).ToListAsync();
        Assert.Equal([(101m, PriceSources.Synced), (98m, PriceSources.Manual), (103m, PriceSources.Synced)],
            prices.Select(p => (p.PricePerUnit, p.Source)));
    }

    [Fact]
    public async Task ChangedSymbol_FetchesTheWholeWindow_AndRemovesTheOldSymbolsCloses()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db, ticker: "NEW.DE");
        asset.PricesSymbol = "OLD.DE";
        await db.SaveChangesAsync();
        var ancient = Today.AddYears(-20);
        await AddPriceAsync(db, asset.Id, ancient, 10m, PriceSources.Synced);
        await AddPriceAsync(db, asset.Id, Today.AddDays(-4), 50m, PriceSources.Synced);
        await AddPriceAsync(db, asset.Id, Today.AddDays(-3), 51m, PriceSources.Synced);
        await AddPriceAsync(db, asset.Id, Today.AddDays(-2), 52m, PriceSources.Manual);
        SetSeries("NEW.DE", (Today.AddDays(-3), 150m), (Today.AddDays(-2), 151m), (Today, 152m));
        var logger = new ListLogger<SyncPriceHistoryCommandHandler>();

        var result = await MakeHandler(db, logger).HandleAsync(new SyncPriceHistoryCommand(asset.Id));

        Assert.Equal(Today.AddYears(-15), Assert.Single(_source.Requests).From);
        var synced = Assert.Single(result!.Assets);
        Assert.Equal((1, 1, 2, 1), (synced.Added, synced.Replaced, synced.Removed, synced.Kept));
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning); // no jump from the old symbol
        await using var check = CreateDb();
        var prices = await check.InvestmentPriceSnapshots.OrderBy(p => p.Date).ToListAsync();
        Assert.Equal(
            [(Today.AddDays(-3), 150m, PriceSources.Synced), (Today.AddDays(-2), 52m, PriceSources.Manual),
                (Today, 152m, PriceSources.Synced)],
            prices.Select(p => (p.Date, p.PricePerUnit, p.Source)));
        Assert.Equal("NEW.DE", (await check.InvestmentAssets.SingleAsync(a => a.Id == asset.Id)).PricesSymbol);
    }

    [Fact]
    public async Task ChangedSymbol_WhoseSyncFails_KeepsTheOldHistory()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db, ticker: "NEW.DE");
        asset.PricesSymbol = "OLD.DE";
        await db.SaveChangesAsync();
        await AddPriceAsync(db, asset.Id, Today.AddDays(-1), 50m, PriceSources.Synced);
        _source.Failures["NEW.DE"] = "No prices found for NEW.DE.";

        await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand(asset.Id));

        await using var check = CreateDb();
        Assert.Equal(1, await check.InvestmentPriceSnapshots.CountAsync());
        var stored = await check.InvestmentAssets.SingleAsync(a => a.Id == asset.Id);
        Assert.Equal("OLD.DE", stored.PricesSymbol);
        Assert.Equal("No prices found for NEW.DE.", stored.PriceSyncError);
    }

    [Fact]
    public async Task HistoryWithoutARecordedSymbol_IsTakenToBeTheTickers()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db);
        await AddPriceAsync(db, asset.Id, Today.AddDays(-10), 150m, PriceSources.Synced);

        await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand(asset.Id));

        Assert.Equal(Today.AddDays(-17), Assert.Single(_source.Requests).From);
    }

    [Fact]
    public async Task Sync_LegacyRowsAloneStillTriggerTheFullHistory()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db);
        await AddPriceAsync(db, asset.Id, Today.AddDays(-1), 99m, PriceSources.Legacy);

        await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand(asset.Id));

        Assert.Equal(Today.AddYears(-15), Assert.Single(_source.Requests).From);
    }

    [Fact]
    public async Task SyncAll_SkipsAssetsNoLongerHeld()
    {
        await using var db = CreateDb();
        var sold = await SeedHeldAsync(db, ticker: "SOLD.DE", name: "Sold");
        db.InvestmentLots.Add(new InvestmentLot { AssetId = sold.Id, Date = new DateOnly(2025, 1, 2), Quantity = -10, PricePerUnit = 120 });
        await db.SaveChangesAsync();
        await SeedHeldAsync(db, ticker: "HELD.DE", name: "Held");

        var result = await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand());

        Assert.Equal(["Held"], result!.Assets.Select(a => a.Name));
        Assert.Equal(["HELD.DE"], _source.Requests.Select(r => r.Symbol));
    }

    [Fact]
    public async Task Gold_IsPricedThroughTheConfiguredProxy()
    {
        await using var db = CreateDb();
        await SeedHeldAsync(db, type: "Gold", ticker: null, name: "Gold");
        var handler = new SyncPriceHistoryCommandHandler(
            db, _source, TestPricing.Config(("Prices:GoldProxySymbol", "EWG2.DE")),
            new FixedTimeProvider(new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)),
            NullLogger<SyncPriceHistoryCommandHandler>.Instance);

        await handler.HandleAsync(new SyncPriceHistoryCommand());

        Assert.Equal("EWG2.DE", Assert.Single(_source.Requests).Symbol);
    }

    [Fact]
    public async Task EtfWithOnlyAnIsin_GetsItsTickerFromTheLookup()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db, ticker: null, isin: "IE00BK5BQT80");
        _source.SymbolsByIsin["IE00BK5BQT80"] = "VWCE.DE";
        SetSeries("VWCE.DE", (Today, 160m));

        var result = await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand());

        Assert.Equal("VWCE.DE", Assert.Single(result!.Assets).Symbol);
        await using var check = CreateDb();
        Assert.Equal("VWCE.DE", (await check.InvestmentAssets.SingleAsync(a => a.Id == asset.Id)).Ticker);
        Assert.Equal(1, await check.InvestmentPriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task EtfWithATicker_IsNeverLookedUpByIsin()
    {
        await using var db = CreateDb();
        await SeedHeldAsync(db, ticker: "MINE.DE", isin: "IE00BK5BQT80");
        _source.SymbolsByIsin["IE00BK5BQT80"] = "VWCE.DE";

        await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand());

        Assert.Empty(_source.IsinLookups);
        Assert.Equal("MINE.DE", Assert.Single(_source.Requests).Symbol);
    }

    [Fact]
    public async Task IsinWithoutAListing_RecordsTheError()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db, ticker: null, isin: "IE00BK5BQT80");

        var result = await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand());

        Assert.Contains("No EUR listing found", Assert.Single(result!.Assets).Error);
        Assert.Empty(_source.Requests);
        await using var check = CreateDb();
        var stored = await check.InvestmentAssets.SingleAsync(a => a.Id == asset.Id);
        Assert.Null(stored.Ticker);
        Assert.Contains("No EUR listing found", stored.PriceSyncError);
    }

    [Fact]
    public async Task EtfWithNeitherTickerNorIsin_RecordsTheError()
    {
        await using var db = CreateDb();
        await SeedHeldAsync(db, ticker: null);

        var result = await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand());

        Assert.Contains("No ticker set", Assert.Single(result!.Assets).Error);
    }

    [Fact]
    public async Task SourceFailure_ForOneAsset_DoesNotStopTheOthers()
    {
        await using var db = CreateDb();
        var broken = await SeedHeldAsync(db, ticker: "BROKEN.DE", name: "A broken");
        await SeedHeldAsync(db, ticker: "VWCE.DE", name: "B working");
        _source.Failures["BROKEN.DE"] = "No prices found for BROKEN.DE.";
        SetSeries("VWCE.DE", (Today, 160m));

        var result = await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand());

        Assert.Equal("No prices found for BROKEN.DE.", result!.Assets[0].Error);
        Assert.Null(result.Assets[1].Error);
        Assert.Equal(1, result.Assets[1].Added);
        await using var check = CreateDb();
        Assert.Equal("No prices found for BROKEN.DE.",
            (await check.InvestmentAssets.SingleAsync(a => a.Id == broken.Id)).PriceSyncError);
    }

    [Fact]
    public async Task SuccessfulSync_ClearsTheLastError()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db);
        asset.PriceSyncError = "The price source did not answer.";
        await db.SaveChangesAsync();

        await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand(asset.Id));

        await using var check = CreateDb();
        Assert.Null((await check.InvestmentAssets.SingleAsync(a => a.Id == asset.Id)).PriceSyncError);
    }

    [Fact]
    public async Task PriceJump_IsStoredAndLogged()
    {
        await using var db = CreateDb();
        var asset = await SeedHeldAsync(db);
        SetSeries("VWCE.DE", (Today.AddDays(-2), 100m), (Today.AddDays(-1), 101m), (Today, 130m));
        var logger = new ListLogger<SyncPriceHistoryCommandHandler>();

        await MakeHandler(db, logger).HandleAsync(new SyncPriceHistoryCommand(asset.Id));

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("Price jump", warning.Message);
        Assert.Contains("130", warning.Message);
        await using var check = CreateDb();
        Assert.Equal(3, await check.InvestmentPriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task UnknownAsset_ReturnsNull()
    {
        await using var db = CreateDb();

        Assert.Null(await MakeHandler(db).HandleAsync(new SyncPriceHistoryCommand(9999)));
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
