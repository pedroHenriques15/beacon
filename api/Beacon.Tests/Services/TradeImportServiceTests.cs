using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Models;
using Beacon.Api.Services.Parsing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests.Services;

public class TradeImportServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private const string Isin = "IE00BK5BQT80";

    private AppDbContext CreateDb() => _database.CreateContext();

    private static TradeImportService MakeService(AppDbContext db) => new(db, TestPricing.Queue());

    private static ParsedTrade Trade(
        decimal quantity = 0.5m, string? id = "trade-1", DateOnly? date = null, decimal fees = 0m) =>
        new(Isin, "Example World ETF", date ?? new DateOnly(2026, 8, 3), quantity, 166.66m, fees, id,
            "Trade Republic savings plan");

    [Fact]
    public async Task ImportAsync_CreatesTheEtfAssetAndALotWithItsFees()
    {
        await using var db = CreateDb();

        var count = await MakeService(db).ImportAsync([Trade(quantity: 1.8m, fees: 1.02m)]);

        Assert.Equal(1, count);
        await using var fresh = CreateDb();
        var asset = await fresh.InvestmentAssets.Include(a => a.Lots).SingleAsync();
        Assert.Equal("ETF", asset.AssetType);
        Assert.Equal(Isin, asset.Isin);
        Assert.Null(asset.Ticker);
        Assert.Equal("Example World ETF", asset.Name);

        var lot = Assert.Single(asset.Lots);
        Assert.Equal(new DateOnly(2026, 8, 3), lot.Date);
        Assert.Equal(1.8m, lot.Quantity);
        Assert.Equal(166.66m, lot.PricePerUnit);
        Assert.Equal(1.02m, lot.Fees);
        Assert.Equal("trade-1", lot.ExternalId);
        Assert.Equal("Trade Republic savings plan", lot.Notes);
    }

    [Fact]
    public async Task ImportAsync_ATradeAlreadyImported_IsNotAddedTwice()
    {
        await using var db = CreateDb();
        var service = MakeService(db);

        var first = await service.ImportAsync([Trade()]);
        var second = await service.ImportAsync([Trade()]);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(1, await db.InvestmentLots.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_TheSameIdTwiceInOneBatch_AddsOneLot()
    {
        await using var db = CreateDb();

        var count = await MakeService(db).ImportAsync([Trade(), Trade()]);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ImportAsync_TwoTradesAlikeButForTheirIds_AreTwoLots()
    {
        await using var db = CreateDb();

        var count = await MakeService(db).ImportAsync([Trade(id: "trade-1"), Trade(id: "trade-2")]);

        Assert.Equal(2, count);
        Assert.Equal(1, await db.InvestmentAssets.CountAsync());
        Assert.Equal(2, await db.InvestmentLots.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_TradesWithoutIds_DedupByAssetDateAndQuantity()
    {
        await using var db = CreateDb();
        var service = MakeService(db);

        var first = await service.ImportAsync([Trade(id: null), Trade(id: null), Trade(id: null, quantity: 0.7m)]);
        var second = await service.ImportAsync([Trade(id: null)]);

        Assert.Equal(2, first);
        Assert.Equal(0, second);
        Assert.Equal(2, await db.InvestmentLots.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_ReusesTheAssetWithTheSameIsin()
    {
        await using var db = CreateDb();
        db.InvestmentAssets.Add(new InvestmentAsset
        {
            AssetType = "ETF",
            Isin = Isin,
            Ticker = "VWCE.DE",
            Name = "My existing ETF",
        });
        await db.SaveChangesAsync();

        var count = await MakeService(db).ImportAsync([Trade()]);

        Assert.Equal(1, count);
        var asset = await db.InvestmentAssets.Include(a => a.Lots).SingleAsync();
        Assert.Equal("My existing ETF", asset.Name);
        Assert.Equal("VWCE.DE", asset.Ticker);
        Assert.Single(asset.Lots);
    }

    [Fact]
    public async Task ImportAsync_QueuesAPriceSyncForEachNewAsset()
    {
        await using var db = CreateDb();
        var queue = TestPricing.Queue();
        var service = new TradeImportService(db, queue);

        await service.ImportAsync([Trade()]);
        var asset = await db.InvestmentAssets.SingleAsync();
        Assert.Equal([asset.Id], TestPricing.Drain(queue));

        // The asset exists now: a later buy adds a lot without queuing it again.
        await service.ImportAsync([Trade(id: "trade-2", date: new DateOnly(2026, 8, 17))]);
        Assert.Empty(TestPricing.Drain(queue));
    }

    [Fact]
    public async Task ExternalId_IsUnique_ButLotsWithoutOneAreNot()
    {
        await using var db = CreateDb();
        var asset = new InvestmentAsset { AssetType = "ETF", Name = "ETF" };
        db.InvestmentLots.AddRange(
            new InvestmentLot { Asset = asset, Quantity = 1, PricePerUnit = 1 },
            new InvestmentLot { Asset = asset, Quantity = 1, PricePerUnit = 1 },
            new InvestmentLot { Asset = asset, Quantity = 1, PricePerUnit = 1, ExternalId = "trade-1" });
        await db.SaveChangesAsync();

        db.InvestmentLots.Add(new InvestmentLot { Asset = asset, Quantity = 1, PricePerUnit = 1, ExternalId = "trade-1" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
