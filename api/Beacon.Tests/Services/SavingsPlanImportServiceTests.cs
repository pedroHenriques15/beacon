using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Services;

public class SavingsPlanImportServiceTests
{
    private const string SavingsDesc =
        "Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- " +
        "World UCITS ETF (USD) Accumulating, quantity: 0.031295";

    private const string SavingsDesc2 =
        "Trade Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All- " +
        "World UCITS ETF (USD) Accumulating, quantity: 0.606281";

    private static DbContextOptions<AppDbContext> DbOptions(string dbName) =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

    private static SavingsPlanImportService MakeService(AppDbContext db) =>
        new(db, NullLogger<SavingsPlanImportService>.Instance);

    private static MonthlyStatement TrStatement(params (string Desc, decimal Amount, DateOnly Date)[] rows) =>
        new()
        {
            Bank = "TRADE REPUBLIC",
            Account = "PT50",
            PeriodFrom = new DateOnly(2026, 8, 1),
            PeriodTo = new DateOnly(2026, 8, 31),
            Currency = "EUR",
            Transactions = rows.Select(r => new Transaction
            {
                Description = r.Desc,
                Amount = r.Amount,
                DatePosting = r.Date,
                DateValue = r.Date,
                Type = "debit",
                Balance = 0,
                IsExcluded = true,
            }).ToList(),
        };

    [Fact]
    public async Task ImportAsync_CreatesEtfAssetAndLot_FromSavingsPlanRow()
    {
        await using var db = new AppDbContext(DbOptions(nameof(ImportAsync_CreatesEtfAssetAndLot_FromSavingsPlanRow)));
        var statement = TrStatement((SavingsDesc, 5.16m, new DateOnly(2026, 8, 3)));

        var count = await MakeService(db).ImportAsync(statement);

        Assert.Equal(1, count);

        var asset = await db.InvestmentAssets.Include(a => a.Lots).SingleAsync();
        Assert.Equal("ETF", asset.AssetType);
        Assert.Equal("IE00BK5BQT80", asset.Isin);
        Assert.Null(asset.Ticker);
        Assert.Contains("Vanguard FTSE", asset.Name);

        var lot = Assert.Single(asset.Lots);
        Assert.Equal(0.031295m, lot.Quantity);
        Assert.Equal(164.8826m, lot.PricePerUnit);
        Assert.Equal(0m, lot.Fees);
        Assert.Equal(new DateOnly(2026, 8, 3), lot.Date);
    }

    [Fact]
    public async Task ImportAsync_IsIdempotent_WhenRunTwice()
    {
        await using var db = new AppDbContext(DbOptions(nameof(ImportAsync_IsIdempotent_WhenRunTwice)));
        var statement = TrStatement((SavingsDesc, 5.16m, new DateOnly(2026, 8, 3)));
        var service = MakeService(db);

        var first  = await service.ImportAsync(statement);
        var second = await service.ImportAsync(statement);

        Assert.Equal(1, first);
        Assert.Equal(0, second);                          // deduped by (asset, date, quantity)
        Assert.Equal(1, await db.InvestmentAssets.CountAsync());
        Assert.Equal(1, await db.InvestmentLots.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_TwoBuysSameIsin_CreateOneAssetTwoLots()
    {
        await using var db = new AppDbContext(DbOptions(nameof(ImportAsync_TwoBuysSameIsin_CreateOneAssetTwoLots)));
        var statement = TrStatement(
            (SavingsDesc,  5.16m,   new DateOnly(2026, 8, 3)),
            (SavingsDesc2, 100.00m, new DateOnly(2026, 8, 3)));

        var count = await MakeService(db).ImportAsync(statement);

        Assert.Equal(2, count);
        Assert.Equal(1, await db.InvestmentAssets.CountAsync());
        Assert.Equal(2, await db.InvestmentLots.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_ReusesExistingAssetMatchedByIsin()
    {
        await using var db = new AppDbContext(DbOptions(nameof(ImportAsync_ReusesExistingAssetMatchedByIsin)));
        db.InvestmentAssets.Add(new InvestmentAsset
        {
            AssetType = "ETF", Isin = "IE00BK5BQT80", Ticker = "VWCE.DEX", Name = "My existing ETF",
        });
        await db.SaveChangesAsync();

        var count = await MakeService(db).ImportAsync(
            TrStatement((SavingsDesc, 5.16m, new DateOnly(2026, 8, 3))));

        Assert.Equal(1, count);
        var asset = await db.InvestmentAssets.Include(a => a.Lots).SingleAsync();
        Assert.Equal("My existing ETF", asset.Name);      // not duplicated
        Assert.Equal("VWCE.DEX", asset.Ticker);
        Assert.Single(asset.Lots);
    }

    [Fact]
    public async Task ImportAsync_IgnoresNonTradeRepublicBank()
    {
        await using var db = new AppDbContext(DbOptions(nameof(ImportAsync_IgnoresNonTradeRepublicBank)));
        var statement = TrStatement((SavingsDesc, 5.16m, new DateOnly(2026, 8, 3)));
        statement.Bank = "REVOLUT";

        var count = await MakeService(db).ImportAsync(statement);

        Assert.Equal(0, count);
        Assert.Equal(0, await db.InvestmentAssets.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_IgnoresNonSavingsPlanRows()
    {
        await using var db = new AppDbContext(DbOptions(nameof(ImportAsync_IgnoresNonSavingsPlanRows)));
        var statement = TrStatement(("MINI MERCADO Card Transaction", 7.30m, new DateOnly(2026, 8, 2)));

        var count = await MakeService(db).ImportAsync(statement);

        Assert.Equal(0, count);
        Assert.Equal(0, await db.InvestmentLots.CountAsync());
    }
}
