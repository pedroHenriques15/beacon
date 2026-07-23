using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Commands.FetchInvestmentPrice;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Services;

public class InvestmentPriceRefreshService(
    IServiceScopeFactory scopeFactory,
    ILogger<InvestmentPriceRefreshService> logger,
    IConfiguration configuration) : BackgroundService
{
    private static readonly TimeZoneInfo EasternZone = GetEasternZone();
    private static readonly TimeOnly MarketOpen  = new(9, 30);
    private static readonly TimeOnly MarketClose = new(16, 0);
    private static readonly double MarketMinutes = (MarketClose - MarketOpen).TotalMinutes; // 390

    private int DailyQuota      => configuration.GetValue<int>("AlphaVantage:DailyQuota", 25);
    private int ReservedForUser => configuration.GetValue<int>("AlphaVantage:ReservedForManual", 5);
    private int AutoQuota       => Math.Max(1, DailyQuota - ReservedForUser);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["AlphaVantage:ApiKey"]))
        {
            logger.LogWarning("AlphaVantage:ApiKey is not configured - investment price auto-refresh disabled.");
            return;
        }

        var (covered, numAssets) = await TodayCoverageAsync(stoppingToken);
        if (!covered)
            numAssets = await RefreshAllAsync(stoppingToken);
        else if (numAssets > 0)
            logger.LogInformation("All investment assets already have today's snapshot - skipping startup refresh.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeNextDelay(numAssets);
            logger.LogInformation("Next investment price refresh in {Minutes:F0} min", delay.TotalMinutes);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            numAssets = await RefreshAllAsync(stoppingToken);
        }
    }

    private async Task<int> RefreshAllAsync(CancellationToken ct)
    {
        try
        {
            using var scope   = scopeFactory.CreateScope();
            var db            = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fetchHandler  = scope.ServiceProvider.GetRequiredService<FetchInvestmentPriceCommandHandler>();

            var assets = await db.InvestmentAssets.ToListAsync(ct);
            if (assets.Count == 0)
            {
                logger.LogDebug("No investment assets to refresh.");
                return 0;
            }

            logger.LogInformation("Refreshing prices for {Count} investment asset(s).", assets.Count);

            for (var i = 0; i < assets.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                if (i > 0) await Task.Delay(TimeSpan.FromSeconds(13), ct);

                var asset = assets[i];
                var (result, error) = await fetchHandler.HandleAsync(new FetchInvestmentPriceCommand(asset.Id), ct);
                if (error is not null)
                    logger.LogWarning("Price fetch failed for {Name}: {Error}", asset.Name, error);
                else if (result is null)
                    logger.LogWarning("Price fetch skipped for {Name}: asset no longer exists.", asset.Name);
                else
                    logger.LogInformation("Updated price for {Name} ({Type})", asset.Name, asset.AssetType);
            }

            return assets.Count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Investment price refresh failed.");
            return 0;
        }
    }

    private async Task<(bool AllCovered, int Count)> TodayCoverageAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var count = await db.InvestmentAssets.CountAsync(ct);
            if (count == 0) return (true, 0);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var covered = await db.InvestmentPriceSnapshots
                .Where(p => p.Date == today)
                .Select(p => p.AssetId)
                .Distinct()
                .CountAsync(ct);

            return (covered >= count, count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Investment snapshot coverage check failed.");
            return (true, 0);
        }
    }

    private TimeSpan ComputeNextDelay(int numAssets)
    {
        var now        = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternZone);
        var todayOpen  = now.Date.Add(MarketOpen.ToTimeSpan());
        var todayClose = now.Date.Add(MarketClose.ToTimeSpan());

        bool isWeekday    = now.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
        bool isMarketOpen = isWeekday && now >= todayOpen && now < todayClose;

        if (isMarketOpen && numAssets > 0)
        {
            var maxRounds      = Math.Max(1, AutoQuota / numAssets);
            var intervalMins   = MarketMinutes / maxRounds;
            return TimeSpan.FromMinutes(intervalMins);
        }

        if (isMarketOpen)
            return TimeSpan.FromMinutes(60);

        var nextOpen = isWeekday && now < todayOpen ? todayOpen : todayOpen.AddDays(1);
        while (nextOpen.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            nextOpen = nextOpen.AddDays(1);

        var waitUntilOpen = nextOpen - now;
        return waitUntilOpen > TimeSpan.Zero ? waitUntilOpen : TimeSpan.FromMinutes(1);
    }

    private static TimeZoneInfo GetEasternZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }
}
