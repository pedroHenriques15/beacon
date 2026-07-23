using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Commands.CreateInvestmentAsset;
using Beacon.Api.Features.Investments.Commands.CreateInvestmentLot;
using Beacon.Api.Features.Investments.Commands.DeleteInvestmentAsset;
using Beacon.Api.Features.Investments.Commands.DeleteInvestmentLot;
using Beacon.Api.Features.Investments.Commands.DeleteInvestmentPriceSnapshot;
using Beacon.Api.Features.Investments.Commands.UpdateInvestmentAsset;
using Beacon.Api.Features.Investments.Commands.UpdateInvestmentLot;
using Beacon.Api.Features.Investments.Commands.UpsertInvestmentPrice;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Tests.Handlers;

public class InvestmentHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<InvestmentAsset> SeedEtfAsync(AppDbContext db, string ticker = "VWCE", string name = "Vanguard FTSE All-World")
    {
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = ticker, Name = name };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    private static async Task<InvestmentAsset> SeedGoldAsync(AppDbContext db, string name = "Physical Gold")
    {
        var asset = new InvestmentAsset { AssetType = "Gold", Name = name };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    // ---- Asset: Create ----

    [Fact]
    public async Task CreateAsset_ETF_PersistsWithTicker()
    {
        await using var db = CreateDb(nameof(CreateAsset_ETF_PersistsWithTicker));
        var handler = new CreateInvestmentAssetCommandHandler(db);

        var (result, error) = await handler.HandleAsync(new CreateInvestmentAssetCommand("ETF", "vwce", "Vanguard FTSE All-World", null));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("ETF", result.AssetType);
        Assert.Equal("VWCE", result.Ticker); // uppercased
        Assert.Equal("Vanguard FTSE All-World", result.Name);
    }

    [Fact]
    public async Task CreateAsset_Gold_PersistsWithoutTicker()
    {
        await using var db = CreateDb(nameof(CreateAsset_Gold_PersistsWithoutTicker));
        var handler = new CreateInvestmentAssetCommandHandler(db);

        var (result, error) = await handler.HandleAsync(new CreateInvestmentAssetCommand("Gold", null, "Physical Gold", "Stored at home"));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("Gold", result.AssetType);
        Assert.Null(result.Ticker);
        Assert.Equal("Stored at home", result.Notes);
    }

    [Fact]
    public async Task CreateAsset_ETF_DuplicateTicker_ReturnsError()
    {
        await using var db = CreateDb(nameof(CreateAsset_ETF_DuplicateTicker_ReturnsError));
        await SeedEtfAsync(db, "VWCE");
        var handler = new CreateInvestmentAssetCommandHandler(db);

        var (result, error) = await handler.HandleAsync(new CreateInvestmentAssetCommand("ETF", "VWCE", "Another Fund", null));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsset_ETF_MissingTicker_ReturnsError()
    {
        await using var db = CreateDb(nameof(CreateAsset_ETF_MissingTicker_ReturnsError));
        var handler = new CreateInvestmentAssetCommandHandler(db);

        var (result, error) = await handler.HandleAsync(new CreateInvestmentAssetCommand("ETF", null, "Some ETF", null));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsset_InvalidAssetType_ReturnsError()
    {
        await using var db = CreateDb(nameof(CreateAsset_InvalidAssetType_ReturnsError));
        var handler = new CreateInvestmentAssetCommandHandler(db);

        var (result, error) = await handler.HandleAsync(new CreateInvestmentAssetCommand("Crypto", "BTC", "Bitcoin", null));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    // ---- Asset: Update ----

    [Fact]
    public async Task UpdateAsset_ChangesNameAndNotes()
    {
        await using var db = CreateDb(nameof(UpdateAsset_ChangesNameAndNotes));
        var asset = await SeedEtfAsync(db);
        var handler = new UpdateInvestmentAssetCommandHandler(db);

        var (result, error) = await handler.HandleAsync(new UpdateInvestmentAssetCommand(asset.Id, "VWCE", "Updated Name", "new notes"));

        Assert.Null(error);
        Assert.Equal("Updated Name", result!.Name);
        Assert.Equal("new notes", result.Notes);
    }

    [Fact]
    public async Task UpdateAsset_NotFound_ReturnsNullNull()
    {
        await using var db = CreateDb(nameof(UpdateAsset_NotFound_ReturnsNullNull));
        var handler = new UpdateInvestmentAssetCommandHandler(db);

        var (result, error) = await handler.HandleAsync(new UpdateInvestmentAssetCommand(9999, "X", "Name", null));

        Assert.Null(result);
        Assert.Null(error);
    }

    // ---- Asset: Delete ----

    [Fact]
    public async Task DeleteAsset_RemovesAsset()
    {
        await using var db = CreateDb(nameof(DeleteAsset_RemovesAsset));
        var asset = await SeedEtfAsync(db);
        var handler = new DeleteInvestmentAssetCommandHandler(db);

        var found = await handler.HandleAsync(new DeleteInvestmentAssetCommand(asset.Id));

        Assert.True(found);
        Assert.Null(await db.InvestmentAssets.FindAsync(asset.Id));
    }

    [Fact]
    public async Task DeleteAsset_CascadesLots()
    {
        await using var db = CreateDb(nameof(DeleteAsset_CascadesLots));
        var asset = await SeedEtfAsync(db);
        db.InvestmentLots.Add(new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2025, 1, 1), Quantity = 10, PricePerUnit = 100 });
        await db.SaveChangesAsync();

        await new DeleteInvestmentAssetCommandHandler(db).HandleAsync(new DeleteInvestmentAssetCommand(asset.Id));

        Assert.Empty(await db.InvestmentLots.ToListAsync());
    }

    [Fact]
    public async Task DeleteAsset_CascadesPriceSnapshots()
    {
        await using var db = CreateDb(nameof(DeleteAsset_CascadesPriceSnapshots));
        var asset = await SeedEtfAsync(db);
        db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot { AssetId = asset.Id, Date = new DateOnly(2025, 1, 1), PricePerUnit = 98 });
        await db.SaveChangesAsync();

        await new DeleteInvestmentAssetCommandHandler(db).HandleAsync(new DeleteInvestmentAssetCommand(asset.Id));

        Assert.Empty(await db.InvestmentPriceSnapshots.ToListAsync());
    }

    [Fact]
    public async Task DeleteAsset_NotFound_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(DeleteAsset_NotFound_ReturnsFalse));
        var found = await new DeleteInvestmentAssetCommandHandler(db).HandleAsync(new DeleteInvestmentAssetCommand(9999));
        Assert.False(found);
    }

    // ---- Lot: Create ----

    [Fact]
    public async Task CreateLot_Buy_PersistsPositiveQuantity()
    {
        await using var db = CreateDb(nameof(CreateLot_Buy_PersistsPositiveQuantity));
        var asset = await SeedEtfAsync(db);
        var handler = new CreateInvestmentLotCommandHandler(db);

        var (result, error) = await handler.HandleAsync(
            new CreateInvestmentLotCommand(asset.Id, new DateOnly(2025, 3, 1), 5.5m, 98.40m, 1.50m, "First buy"));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(5.5m, result.Quantity);
        Assert.Equal(98.40m, result.PricePerUnit);
        Assert.Equal(1.50m, result.Fees);
    }

    [Fact]
    public async Task CreateLot_Sell_PersistsNegativeQuantity()
    {
        await using var db = CreateDb(nameof(CreateLot_Sell_PersistsNegativeQuantity));
        var asset = await SeedEtfAsync(db);
        db.InvestmentLots.Add(new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2025, 5, 1), Quantity = 5, PricePerUnit = 100 });
        await db.SaveChangesAsync();
        var handler = new CreateInvestmentLotCommandHandler(db);

        var (result, error) = await handler.HandleAsync(
            new CreateInvestmentLotCommand(asset.Id, new DateOnly(2025, 6, 1), -2m, 110m, null, null));

        Assert.Null(error);
        Assert.Equal(-2m, result!.Quantity);
    }

    [Fact]
    public async Task CreateLot_ZeroQuantity_ReturnsError()
    {
        await using var db = CreateDb(nameof(CreateLot_ZeroQuantity_ReturnsError));
        var asset = await SeedEtfAsync(db);
        var handler = new CreateInvestmentLotCommandHandler(db);

        var (result, error) = await handler.HandleAsync(
            new CreateInvestmentLotCommand(asset.Id, new DateOnly(2025, 1, 1), 0, 100m, null, null));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateLot_InvalidAssetId_ReturnsError()
    {
        await using var db = CreateDb(nameof(CreateLot_InvalidAssetId_ReturnsError));
        var handler = new CreateInvestmentLotCommandHandler(db);

        var (result, error) = await handler.HandleAsync(
            new CreateInvestmentLotCommand(9999, new DateOnly(2025, 1, 1), 1, 100m, null, null));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    // ---- Lot: Update ----

    [Fact]
    public async Task UpdateLot_UpdatesFields()
    {
        await using var db = CreateDb(nameof(UpdateLot_UpdatesFields));
        var asset = await SeedEtfAsync(db);
        var lot = new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2025, 1, 1), Quantity = 3, PricePerUnit = 90 };
        db.InvestmentLots.Add(lot);
        await db.SaveChangesAsync();

        var (result, error) = await new UpdateInvestmentLotCommandHandler(db).HandleAsync(
            new UpdateInvestmentLotCommand(lot.Id, new DateOnly(2025, 2, 1), 5, 95m, 2m, "updated"));

        Assert.Null(error);
        Assert.Equal(5m, result!.Quantity);
        Assert.Equal(95m, result.PricePerUnit);
        Assert.Equal("updated", result.Notes);
    }

    [Fact]
    public async Task UpdateLot_NotFound_ReturnsNullNull()
    {
        await using var db = CreateDb(nameof(UpdateLot_NotFound_ReturnsNullNull));
        var (result, error) = await new UpdateInvestmentLotCommandHandler(db).HandleAsync(
            new UpdateInvestmentLotCommand(9999, new DateOnly(2025, 1, 1), 1, 100m, null, null));

        Assert.Null(result);
        Assert.Null(error);
    }

    // ---- Lot: Delete ----

    [Fact]
    public async Task DeleteLot_RemovesLot()
    {
        await using var db = CreateDb(nameof(DeleteLot_RemovesLot));
        var asset = await SeedEtfAsync(db);
        var lot = new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2025, 1, 1), Quantity = 1, PricePerUnit = 100 };
        db.InvestmentLots.Add(lot);
        await db.SaveChangesAsync();

        var found = await new DeleteInvestmentLotCommandHandler(db).HandleAsync(new DeleteInvestmentLotCommand(lot.Id));

        Assert.True(found);
        Assert.Null(await db.InvestmentLots.FindAsync(lot.Id));
    }

    [Fact]
    public async Task DeleteLot_NotFound_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(DeleteLot_NotFound_ReturnsFalse));
        var found = await new DeleteInvestmentLotCommandHandler(db).HandleAsync(new DeleteInvestmentLotCommand(9999));
        Assert.False(found);
    }

    // ---- Price: Upsert ----

    [Fact]
    public async Task UpsertPrice_CreatesNewSnapshot_WhenNoneExists()
    {
        await using var db = CreateDb(nameof(UpsertPrice_CreatesNewSnapshot_WhenNoneExists));
        var asset = await SeedEtfAsync(db);
        var handler = new UpsertInvestmentPriceCommandHandler(db);

        var (result, error) = await handler.HandleAsync(
            new UpsertInvestmentPriceCommand(asset.Id, new DateOnly(2025, 6, 1), 105.50m));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(105.50m, result.PricePerUnit);
        Assert.Equal(1, await db.InvestmentPriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task UpsertPrice_UpdatesExistingSnapshot_WhenSameDateExists()
    {
        await using var db = CreateDb(nameof(UpsertPrice_UpdatesExistingSnapshot_WhenSameDateExists));
        var asset = await SeedEtfAsync(db);
        var handler = new UpsertInvestmentPriceCommandHandler(db);
        var date = new DateOnly(2025, 6, 1);

        await handler.HandleAsync(new UpsertInvestmentPriceCommand(asset.Id, date, 100m));
        var (result, error) = await handler.HandleAsync(new UpsertInvestmentPriceCommand(asset.Id, date, 110m));

        Assert.Null(error);
        Assert.Equal(110m, result!.PricePerUnit);
        Assert.Equal(1, await db.InvestmentPriceSnapshots.CountAsync()); // still one row
    }

    [Fact]
    public async Task UpsertPrice_InvalidAssetId_ReturnsError()
    {
        await using var db = CreateDb(nameof(UpsertPrice_InvalidAssetId_ReturnsError));
        var (result, error) = await new UpsertInvestmentPriceCommandHandler(db).HandleAsync(
            new UpsertInvestmentPriceCommand(9999, new DateOnly(2025, 1, 1), 100m));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    // ---- Price: Delete snapshot ----

    [Fact]
    public async Task DeletePriceSnapshot_RemovesSnapshot()
    {
        await using var db = CreateDb(nameof(DeletePriceSnapshot_RemovesSnapshot));
        var asset = await SeedEtfAsync(db);
        var snap = new InvestmentPriceSnapshot { AssetId = asset.Id, Date = new DateOnly(2025, 1, 1), PricePerUnit = 100 };
        db.InvestmentPriceSnapshots.Add(snap);
        await db.SaveChangesAsync();

        var found = await new DeleteInvestmentPriceSnapshotCommandHandler(db).HandleAsync(
            new DeleteInvestmentPriceSnapshotCommand(snap.Id));

        Assert.True(found);
        Assert.Empty(await db.InvestmentPriceSnapshots.ToListAsync());
    }

    [Fact]
    public async Task DeletePriceSnapshot_NotFound_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(DeletePriceSnapshot_NotFound_ReturnsFalse));
        var found = await new DeleteInvestmentPriceSnapshotCommandHandler(db).HandleAsync(
            new DeleteInvestmentPriceSnapshotCommand(9999));
        Assert.False(found);
    }

    // ---- Query ----

    [Fact]
    public async Task GetInvestmentAssets_ReturnsAllAssetsWithLotsAndSnapshots()
    {
        await using var db = CreateDb(nameof(GetInvestmentAssets_ReturnsAllAssetsWithLotsAndSnapshots));
        var etf  = await SeedEtfAsync(db);
        var gold = await SeedGoldAsync(db);

        db.InvestmentLots.Add(new InvestmentLot { AssetId = etf.Id, Date = new DateOnly(2025, 1, 1), Quantity = 10, PricePerUnit = 90 });
        db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot { AssetId = etf.Id, Date = new DateOnly(2025, 6, 1), PricePerUnit = 105 });
        await db.SaveChangesAsync();

        var result = await new GetInvestmentAssetsQueryHandler(db).HandleAsync();

        Assert.Equal(2, result.Count);
        var etfResult = result.First(a => a.AssetType == "ETF");
        Assert.Single(etfResult.Lots);
        Assert.Single(etfResult.PriceSnapshots);
        var goldResult = result.First(a => a.AssetType == "Gold");
        Assert.Empty(goldResult.Lots);
        Assert.Empty(goldResult.PriceSnapshots);
    }

    [Fact]
    public async Task GetInvestmentAssets_LotsOrderedByDateDescending()
    {
        await using var db = CreateDb(nameof(GetInvestmentAssets_LotsOrderedByDateDescending));
        var asset = await SeedEtfAsync(db);

        db.InvestmentLots.AddRange(
            new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2025, 1, 1), Quantity = 1, PricePerUnit = 80 },
            new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2025, 6, 1), Quantity = 2, PricePerUnit = 100 },
            new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2025, 3, 1), Quantity = 3, PricePerUnit = 90 });
        await db.SaveChangesAsync();

        var result = await new GetInvestmentAssetsQueryHandler(db).HandleAsync();
        var lots = result[0].Lots;

        Assert.Equal(new DateOnly(2025, 6, 1), lots[0].Date);
        Assert.Equal(new DateOnly(2025, 3, 1), lots[1].Date);
        Assert.Equal(new DateOnly(2025, 1, 1), lots[2].Date);
    }

    [Fact]
    public async Task GetInvestmentAssets_SnapshotsOrderedByDateDescending()
    {
        await using var db = CreateDb(nameof(GetInvestmentAssets_SnapshotsOrderedByDateDescending));
        var asset = await SeedEtfAsync(db);

        db.InvestmentPriceSnapshots.AddRange(
            new InvestmentPriceSnapshot { AssetId = asset.Id, Date = new DateOnly(2025, 1, 1), PricePerUnit = 80 },
            new InvestmentPriceSnapshot { AssetId = asset.Id, Date = new DateOnly(2025, 6, 1), PricePerUnit = 105 });
        await db.SaveChangesAsync();

        var result = await new GetInvestmentAssetsQueryHandler(db).HandleAsync();
        var snaps = result[0].PriceSnapshots;

        Assert.Equal(new DateOnly(2025, 6, 1), snaps[0].Date);
        Assert.Equal(new DateOnly(2025, 1, 1), snaps[1].Date);
    }
}
