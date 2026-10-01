using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Commands.SyncPriceHistory;
using Beacon.Api.Models;
using Beacon.Api.Services.Pricing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beacon.Tests.Services;

public class PriceHistorySyncServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly StubPriceHistorySource _source = new();

    public void Dispose() => _database.Dispose();

    [Theory]
    [InlineData("2026-09-30T10:00:00Z", "2026-09-30T22:00:00Z")]
    [InlineData("2026-09-30T22:00:00Z", "2026-10-01T22:00:00Z")]
    [InlineData("2026-09-30T23:59:00Z", "2026-10-01T22:00:00Z")]
    public void NextRun_IsTheNextRunTimeInUtc(string now, string expected)
    {
        var next = PriceHistorySyncService.NextRun(DateTimeOffset.Parse(now), new TimeOnly(22, 0));

        Assert.Equal(DateTimeOffset.Parse(expected), next);
    }

    [Fact]
    public async Task Disabled_SyncsNothing_AndQueuesNothing()
    {
        await SeedHeldEtfAsync();
        var config = TestPricing.Config(("Prices:Enabled", "false"));
        var (service, queue) = Build(config);

        queue.Enqueue(1);
        await service.StartAsync(CancellationToken.None);
        await (service.ExecuteTask ?? Task.CompletedTask);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(_source.Requests);
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Enabled_SyncsAtStartup_ThenEachQueuedAsset()
    {
        var asset = await SeedHeldEtfAsync();
        var (service, queue) = Build(TestPricing.Config());

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => _source.Requests.Count >= 1);
        queue.Enqueue(asset.Id);
        await WaitForAsync(() => _source.Requests.Count >= 2);
        await service.StopAsync(CancellationToken.None);

        Assert.All(_source.Requests, r => Assert.Equal("VWCE.DE", r.Symbol));
    }

    private async Task<InvestmentAsset> SeedHeldEtfAsync()
    {
        await using var db = _database.CreateContext();
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = "VWCE.DE", Name = "World ETF" };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        db.InvestmentLots.Add(new InvestmentLot { AssetId = asset.Id, Date = new DateOnly(2025, 1, 2), Quantity = 1, PricePerUnit = 100 });
        await db.SaveChangesAsync();
        return asset;
    }

    private (PriceHistorySyncService Service, PriceSyncQueue Queue) Build(IConfiguration config)
    {
        var services = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IPriceHistorySource>(_source)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddScoped(_ => _database.CreateContext())
            .AddScoped<SyncPriceHistoryCommandHandler>()
            .BuildServiceProvider();
        var queue = new PriceSyncQueue(config);
        var service = new PriceHistorySyncService(
            services.GetRequiredService<IServiceScopeFactory>(), queue, config, TimeProvider.System,
            NullLogger<PriceHistorySyncService>.Instance);
        return (service, queue);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The sync service did not get there in time.");
            await Task.Delay(20);
        }
    }
}
