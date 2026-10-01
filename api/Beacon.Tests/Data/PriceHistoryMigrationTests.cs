using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Beacon.Tests.Data;

/// <summary>The migration that records where prices came from, run over data from before it.</summary>
public class PriceHistoryMigrationTests : IDisposable
{
    private const string Before = "20260930131841_InitialCreate";
    private const string Migration = "20261001200740_PriceHistorySync";

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ExistingPricesBecomeLegacy_AndAlphaVantageTickersLoseTheirSuffix()
    {
        await using var db = _database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "InvestmentAssets" ("Id", "AssetType", "Ticker", "Name", "ImportedAt")
            VALUES (1, 'ETF', 'VWCE.DEX', 'World', '2026-01-01 00:00:00'),
                   (2, 'ETF', 'EUNL.DE', 'Already Yahoo', '2026-01-01 00:00:00'),
                   (3, 'Gold', NULL, 'Gold', '2026-01-01 00:00:00');
            INSERT INTO "InvestmentPriceSnapshots" ("AssetId", "Date", "PricePerUnit", "ImportedAt")
            VALUES (1, '2026-09-29', '150.0', '2026-09-29 15:00:00');
            """);

        await migrator.MigrateAsync(Migration);

        db.ChangeTracker.Clear();
        var tickers = await db.InvestmentAssets.OrderBy(a => a.Id).Select(a => a.Ticker).ToListAsync();
        Assert.Equal(["VWCE.DE", "EUNL.DE", null], tickers);
        Assert.Equal("Legacy", (await db.InvestmentPriceSnapshots.SingleAsync()).Source);
    }
}
