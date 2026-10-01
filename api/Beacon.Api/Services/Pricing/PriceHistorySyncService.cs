using Beacon.Api.Features.Investments.Commands.SyncPriceHistory;

namespace Beacon.Api.Services.Pricing;

/// <summary>
/// Syncs every held asset's prices at startup and once a day at <c>Prices:DailyRunTime</c> (UTC,
/// after the European close), and any asset queued in <see cref="PriceSyncQueue"/> as it arrives.
/// Does nothing when <c>Prices:Enabled</c> is false. Failures are logged and never stop the host.
/// </summary>
public class PriceHistorySyncService(
    IServiceScopeFactory scopeFactory,
    PriceSyncQueue queue,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<PriceHistorySyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!PriceSyncSettings.Enabled(configuration))
        {
            logger.LogInformation("Price sync is turned off (Prices:Enabled=false).");
            return;
        }

        await SyncAsync(assetId: null, stoppingToken);

        var runTime = PriceSyncSettings.DailyRunTime(configuration);
        var nextRun = NextRun(timeProvider.GetUtcNow(), runTime);
        logger.LogInformation("Next daily price sync at {NextRun:u}", nextRun);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var delay = nextRun - timeProvider.GetUtcNow();
            var timer = Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, timeProvider, wait.Token);
            var queued = queue.Reader.WaitToReadAsync(wait.Token).AsTask();

            try
            {
                await Task.WhenAny(timer, queued);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            await wait.CancelAsync(); // stop whichever wait is still pending
            if (stoppingToken.IsCancellationRequested) break;

            var assetIds = new HashSet<int>();
            while (queue.Reader.TryRead(out var assetId)) assetIds.Add(assetId);
            foreach (var assetId in assetIds)
                await SyncAsync(assetId, stoppingToken);

            if (timeProvider.GetUtcNow() >= nextRun)
            {
                await SyncAsync(assetId: null, stoppingToken);
                nextRun = NextRun(timeProvider.GetUtcNow(), runTime);
                logger.LogInformation("Next daily price sync at {NextRun:u}", nextRun);
            }
        }
    }

    /// <summary>The next <paramref name="runTime"/> (UTC) after <paramref name="now"/>.</summary>
    internal static DateTimeOffset NextRun(DateTimeOffset now, TimeOnly runTime)
    {
        var utc = now.ToUniversalTime();
        var today = new DateTimeOffset(DateOnly.FromDateTime(utc.UtcDateTime).ToDateTime(runTime), TimeSpan.Zero);
        return today > utc ? today : today.AddDays(1);
    }

    private async Task SyncAsync(int? assetId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<SyncPriceHistoryCommandHandler>();
            await handler.HandleAsync(new SyncPriceHistoryCommand(assetId), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Price sync failed.");
        }
    }
}
