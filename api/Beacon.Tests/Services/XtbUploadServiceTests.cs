using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Services;

public class XtbUploadServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private AppDbContext CreateDb() => _database.CreateContext();

    private static XtbUploadService MakeService(AppDbContext db) =>
        new(db, new TradeImportService(db, TestPricing.Queue()), NullLogger<XtbUploadService>.Instance);

    private static readonly DateTime Generated = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static ParsedTrade Buy(string ticker, decimal quantity, string id, DateOnly? date = null) =>
        new(Isin: null, ticker, "Example ETF", date ?? new DateOnly(2026, 6, 4), quantity, 100m, 0m, id, "XTB buy");

    private static XtbExport June(IReadOnlyList<ParsedTrade> trades, params XtbHolding[] holdings) =>
        new(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), Generated, trades, holdings);

    [Fact]
    public async Task ImportAsync_BooksTheTrades_AndSaysHowManyWereNew()
    {
        await using var db = CreateDb();
        var service = MakeService(db);
        var export = June([Buy("SXR8.DE", 0.5m, "XTB:1"), Buy("SXR8.DE", 0.25m, "XTB:2")]);

        var first = await service.ImportAsync(export);
        var again = await service.ImportAsync(export);

        Assert.Equal(("XTB", 2, 2), (first.Broker, first.TradeCount, first.Added));
        Assert.Equal(0, again.Added);
    }

    [Fact]
    public async Task CheckHoldingsAsync_HoldingsAsXtbLists_WarnOfNothing()
    {
        await using var db = CreateDb();
        var service = MakeService(db);
        var export = June([Buy("SXR8.DE", 0.5m, "XTB:1"), Buy("SXR8.DE", 0.25m, "XTB:2")],
            new XtbHolding("SXR8.DE", 0.75m));
        await service.ImportAsync(export);

        Assert.Empty(await service.CheckHoldingsAsync(export));
    }

    [Fact]
    public async Task CheckHoldingsAsync_AHoldingThatDiffers_NamesItAndBothQuantities()
    {
        await using var db = CreateDb();
        var service = MakeService(db);
        var export = June([Buy("SXR8.DE", 0.5m, "XTB:1")], new XtbHolding("SXR8.DE", 0.75m));
        await service.ImportAsync(export);

        var warning = Assert.Single(await service.CheckHoldingsAsync(export));

        Assert.StartsWith("SXR8.DE: XTB lists 0.75 held on 6 Oct 2026, Beacon has 0.5.", warning);
    }

    [Fact]
    public async Task CheckHoldingsAsync_AHoldingBeaconHasNoAssetFor_Warns()
    {
        await using var db = CreateDb();

        var warning = Assert.Single(await MakeService(db).CheckHoldingsAsync(June([], new XtbHolding("EXSA.DE", 3m))));

        Assert.StartsWith("EXSA.DE: XTB lists 3 held", warning);
    }

    [Fact]
    public async Task CheckHoldingsAsync_AnExportOlderThanTheLatestXtbTrade_IsNotCompared()
    {
        await using var db = CreateDb();
        var service = MakeService(db);
        await service.ImportAsync(new XtbExport(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), Generated,
            [Buy("SXR8.DE", 0.5m, "XTB:9", new DateOnly(2026, 8, 7))], []));

        // June's Open Positions, made in October, holds August's buy, which June's trades don't.
        var june = June([Buy("SXR8.DE", 0.25m, "XTB:1")], new XtbHolding("SXR8.DE", 3m));
        await service.ImportAsync(june);

        Assert.Empty(await service.CheckHoldingsAsync(june));
    }

    [Fact]
    public async Task CheckHoldingsAsync_LeavesOutLotsAnotherSourceImported_ButCountsTypedOnes()
    {
        await using var db = CreateDb();
        var world = new InvestmentAsset
        {
            AssetType = "ETF",
            Isin = "IE00BK5BQT80",
            PricesSymbol = "VWCE.DE",
            Name = "Example World ETF",
        };
        db.InvestmentLots.AddRange(
            new InvestmentLot { Asset = world, Date = new DateOnly(2026, 6, 2), Quantity = 4m, PricePerUnit = 100m, ExternalId = "tr-1" },
            new InvestmentLot { Asset = world, Date = new DateOnly(2026, 6, 3), Quantity = 1m, PricePerUnit = 100m });
        await db.SaveChangesAsync();
        var service = MakeService(db);
        var export = June([Buy("VWCE.DE", 2m, "XTB:1")], new XtbHolding("VWCE.DE", 3m));
        await service.ImportAsync(export);

        // XTB's 3 are its buy of 2 and the lot typed by hand; Trade Republic's 4 are not XTB's.
        Assert.Empty(await service.CheckHoldingsAsync(export));
    }
}
