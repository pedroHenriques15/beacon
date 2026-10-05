using System.Reflection;
using Beacon.Api.Data;
using Beacon.Api.Features.Health.Queries.GetHealth;
using Beacon.Api.Models;
using Beacon.Tests.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Handlers;

public class HealthHandlerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    // A missing or damaged database is a file: an in-memory one has nothing to lose or break.
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), $"beacon_health_tests_{Guid.NewGuid()}");

    public HealthHandlerTests() => Directory.CreateDirectory(_tempFolder);

    public void Dispose()
    {
        _database.Dispose();
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, recursive: true);
    }

    private static GetHealthQueryHandler CreateHandler(AppDbContext db) =>
        new(db, TestPricing.Config(), TimeProvider.System, NullLogger<GetHealthQueryHandler>.Instance);

    // Without pooling no connection keeps the file open, so Dispose can delete the folder.
    private static AppDbContext CreateFileDb(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseBeaconSqlite($"Data Source={path};Pooling=False").Options);

    [Fact]
    public async Task MigratedDatabase_IsOkWithTheReleaseAndNewestMigration()
    {
        await using var db = _database.CreateContext();

        var health = await CreateHandler(db).HandleAsync();

        Assert.Equal(GetHealthResponse.Ok, health.Status);
        Assert.Null(health.Reason);
        Assert.Equal(db.Database.GetMigrations().Last(), health.NewestMigration);
        var stamped = typeof(GetHealthQueryHandler).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(stamped, health.Commit is null ? health.Version : $"{health.Version}+{health.Commit}");
    }

    [Fact]
    public async Task PendingMigration_IsDegraded()
    {
        await using var db = _database.CreateContext();
        var migrations = db.Database.GetMigrations().ToList();
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {migrations[^1]}");

        var health = await CreateHandler(db).HandleAsync();

        Assert.Equal(GetHealthResponse.Degraded, health.Status);
        Assert.Equal("migrations pending", health.Reason);
        Assert.Equal(migrations.SkipLast(1).LastOrDefault(), health.NewestMigration);
    }

    [Fact]
    public async Task DamagedDatabaseFile_IsDegraded()
    {
        var path = Path.Combine(_tempFolder, "beacon.db");
        await File.WriteAllTextAsync(path, new string('x', 4096));
        await using var db = CreateFileDb(path);

        var health = await CreateHandler(db).HandleAsync();

        Assert.Equal(GetHealthResponse.Degraded, health.Status);
        Assert.Equal("database unreachable", health.Reason);
        Assert.Null(health.NewestMigration);
    }

    [Fact]
    public async Task MissingDatabaseFile_IsDegradedAndNotCreated()
    {
        var path = Path.Combine(_tempFolder, "beacon.db");
        await using var db = CreateFileDb(path);

        var health = await CreateHandler(db).HandleAsync();

        Assert.Equal(GetHealthResponse.Degraded, health.Status);
        Assert.Equal("database unreachable", health.Reason);
        Assert.False(File.Exists(path));
    }

    // ---- Prices ----

    private static readonly DateOnly Today = new(2026, 9, 30);

    private static GetHealthQueryHandler CreatePricesHandler(AppDbContext db, params (string Key, string Value)[] settings) =>
        new(db, TestPricing.Config(settings),
            new FixedTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)),
            NullLogger<GetHealthQueryHandler>.Instance);

    private static async Task SeedHeldAsync(AppDbContext db, string name, DateOnly? latestPrice, string? error = null)
    {
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = $"{name}.DE", Name = name, PriceSyncError = error };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        db.InvestmentLots.Add(new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2025, 1, 2), Quantity = 1, PricePerUnit = 100 });
        if (latestPrice is DateOnly date)
            db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot
            {
                AssetId = asset.Id,
                Date = date,
                PricePerUnit = 100,
                Source = PriceSources.Synced,
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Prices_CurrentForEveryHeldAsset_AreOk()
    {
        await using var db = _database.CreateContext();
        // Today is a Wednesday: Monday's close is one trading day behind, still current.
        await SeedHeldAsync(db, "A", Today.AddDays(-1));
        await SeedHeldAsync(db, "B", Today.AddDays(-2));

        var health = await CreatePricesHandler(db).HandleAsync();

        Assert.Equal(GetHealthResponse.PricesOk, health.Prices);
    }

    [Fact]
    public async Task Prices_TooOldOrFailing_AreStale_ButTheStatusStaysOk()
    {
        await using var db = _database.CreateContext();
        await SeedHeldAsync(db, "Old", Today.AddDays(-5)); // the Friday before
        await SeedHeldAsync(db, "Failing", Today, error: "The price source did not answer.");
        await SeedHeldAsync(db, "Fresh", Today);

        var health = await CreatePricesHandler(db).HandleAsync();

        Assert.Equal(GetHealthResponse.Ok, health.Status);
        Assert.Equal(GetHealthResponse.PricesStale, health.Prices);
    }

    [Fact]
    public async Task Prices_MissingForAHeldAsset_AreStale()
    {
        await using var db = _database.CreateContext();
        await SeedHeldAsync(db, "Unpriced", latestPrice: null);

        var health = await CreatePricesHandler(db).HandleAsync();

        Assert.Equal(GetHealthResponse.PricesStale, health.Prices);
    }

    [Fact]
    public async Task Prices_WhenSyncIsTurnedOff_AreDisabled()
    {
        await using var db = _database.CreateContext();
        await SeedHeldAsync(db, "Old", Today.AddDays(-30));

        var health = await CreatePricesHandler(db, ("Prices:Enabled", "false")).HandleAsync();

        Assert.Equal(GetHealthResponse.PricesDisabled, health.Prices);
    }

    [Theory]
    [InlineData("1.0.0+0123456789abcdef0123456789abcdef01234567", "1.0.0", "0123456789abcdef0123456789abcdef01234567")]
    [InlineData("1.0.0", "1.0.0", null)]
    public void ParseRelease_SplitsTheVersionFromTheCommit(string informationalVersion, string version, string? commit) =>
        Assert.Equal((version, commit), GetHealthQueryHandler.ParseRelease(informationalVersion));
}
