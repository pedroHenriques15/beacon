using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services.Pricing;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.SyncPriceHistory;

/// <summary>
/// Brings an asset's daily closes up to date with one request to the price source. The first sync
/// stores <c>Prices:HistoryYears</c> of history; later ones start a week before the latest synced
/// close, which also fills the days the server was off. When the asset's symbol is no longer the
/// one its synced closes came from (a ticker changed, another gold proxy), the whole window is
/// fetched again and synced closes the new symbol lacks are removed. Hand-entered prices are never
/// replaced.
/// </summary>
public class SyncPriceHistoryCommandHandler(
    AppDbContext db,
    IPriceHistorySource source,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SyncPriceHistoryCommandHandler> logger)
{
    // The daily run, a queued asset and a click can overlap; the (AssetId, Date) index allows one writer.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private const int OverlapDays = 7;

    /// <returns>Null when <see cref="SyncPriceHistoryCommand.AssetId"/> names no asset.</returns>
    public async Task<SyncPriceHistoryResponse?> HandleAsync(SyncPriceHistoryCommand command, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            List<InvestmentAsset> assets;
            if (command.AssetId is int id)
            {
                var asset = await db.InvestmentAssets.FindAsync([id], ct);
                if (asset is null) return null;
                assets = [asset];
            }
            else
            {
                assets = await HeldAssetsAsync(ct);
            }

            var results = new List<AssetPriceSyncResult>();
            foreach (var asset in assets)
                results.Add(await SyncAssetAsync(asset, ct));

            return new SyncPriceHistoryResponse(results);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<List<InvestmentAsset>> HeldAssetsAsync(CancellationToken ct)
    {
        // Summed in memory: a handful of assets and lots.
        var lots = await db.InvestmentLots.Select(l => new { l.AssetId, l.Quantity }).ToListAsync(ct);
        var held = lots.GroupBy(l => l.AssetId).Where(g => g.Sum(l => l.Quantity) > 0).Select(g => g.Key).ToList();

        return (await db.InvestmentAssets.Where(a => held.Contains(a.Id)).ToListAsync(ct))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<AssetPriceSyncResult> SyncAssetAsync(InvestmentAsset asset, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        string? symbol = null;

        try
        {
            symbol = await ResolveSymbolAsync(asset, ct);

            // Null for history synced before the symbol was recorded: taken to be this symbol's.
            var symbolChanged = asset.PricesSymbol is { } previous
                && !string.Equals(previous, symbol, StringComparison.OrdinalIgnoreCase);
            var latestSynced = symbolChanged
                ? null
                : await db.InvestmentPriceSnapshots
                    .Where(p => p.AssetId == asset.Id && p.Source == PriceSources.Synced)
                    .MaxAsync(p => (DateOnly?)p.Date, ct);
            var from = latestSynced?.AddDays(-OverlapDays)
                ?? today.AddYears(-PriceSyncSettings.HistoryYears(configuration));

            var closes = await source.GetDailyClosesAsync(symbol, from, today, ct);
            var (added, replaced, kept, removed) = await StoreAsync(asset, symbol, closes, from, symbolChanged, now, ct);

            asset.PricesSymbol = symbol;
            asset.PricesSyncedAt = now;
            asset.PriceSyncError = null;
            await db.SaveChangesAsync(ct);

            var latestClose = closes.Count > 0 ? closes.Keys.Max() : (DateOnly?)null;
            logger.LogInformation(
                "Synced prices for {Name} ({Symbol}): {Added} added, {Replaced} replaced, {Removed} removed, {Kept} hand-entered kept",
                asset.Name, symbol, added, replaced, removed, kept);
            return new AssetPriceSyncResult(
                asset.Id, asset.Name, symbol, added, replaced, removed, kept, latestClose, null);
        }
        catch (Exception ex) when (ex is PriceSourceException or SymbolException)
        {
            return await FailAsync(asset, symbol, ex.Message, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Price sync failed for {Name}", asset.Name);
            return await FailAsync(asset, symbol, "The price sync failed unexpectedly; see the server log.", ct);
        }
    }

    /// <summary>Gold uses the proxy; an ETF its ticker, found from its ISIN when it has none.</summary>
    private async Task<string> ResolveSymbolAsync(InvestmentAsset asset, CancellationToken ct)
    {
        if (asset.AssetType == "Gold")
            return PriceSyncSettings.GoldProxySymbol(configuration);

        if (!string.IsNullOrWhiteSpace(asset.Ticker))
            return asset.Ticker;

        if (string.IsNullOrWhiteSpace(asset.Isin))
            throw new SymbolException("No ticker set. Set the ETF's ticker to price it.");

        var found = await source.FindSymbolByIsinAsync(asset.Isin, ct)
            ?? throw new SymbolException(
                $"No EUR listing found for ISIN {asset.Isin}. Set the ETF's ticker by hand (on Xetra, ending in .DE).");

        var taken = await db.InvestmentAssets.AnyAsync(a => a.AssetType == "ETF" && a.Ticker == found && a.Id != asset.Id, ct);
        if (taken)
            throw new SymbolException($"ISIN {asset.Isin} maps to {found}, which another ETF already uses.");

        logger.LogInformation("Found ticker {Symbol} for {Name} from ISIN {Isin}", found, asset.Name, asset.Isin);
        asset.Ticker = found;
        return found;
    }

    /// <param name="replaceHistory">
    /// The symbol changed: every synced close the new symbol doesn't have is removed, and no jump
    /// is measured against the old symbol's prices.
    /// </param>
    private async Task<(int Added, int Replaced, int Kept, int Removed)> StoreAsync(
        InvestmentAsset asset, string symbol, IReadOnlyDictionary<DateOnly, decimal> closes, DateOnly from,
        bool replaceHistory, DateTime now, CancellationToken ct)
    {
        var existing = await db.InvestmentPriceSnapshots
            .Where(p => p.AssetId == asset.Id && p.Date >= from)
            .ToDictionaryAsync(p => p.Date, ct);
        var previous = replaceHistory
            ? null
            : await db.InvestmentPriceSnapshots
                .Where(p => p.AssetId == asset.Id && p.Date < from)
                .OrderByDescending(p => p.Date)
                .Select(p => (decimal?)p.PricePerUnit)
                .FirstOrDefaultAsync(ct);
        var jumpPercent = PriceSyncSettings.JumpWarningPercent(configuration);

        int added = 0, replaced = 0, kept = 0;
        foreach (var (date, close) in closes.OrderBy(kv => kv.Key))
        {
            // Stored all the same: a real move must not be lost. The log is the hint to look.
            if (previous is > 0 && Math.Abs(close - previous.Value) / previous.Value * 100 > jumpPercent)
                logger.LogWarning(
                    "Price jump for {Name} ({Symbol}) on {Date}: {Previous} to {Close}",
                    asset.Name, symbol, date, previous, close);
            previous = close;

            if (!existing.TryGetValue(date, out var row))
            {
                db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot
                {
                    AssetId = asset.Id,
                    Date = date,
                    PricePerUnit = close,
                    Source = PriceSources.Synced,
                    ImportedAt = now,
                });
                added++;
            }
            else if (row.Source == PriceSources.Manual)
            {
                kept++;
            }
            else if (row.Source == PriceSources.Legacy || row.PricePerUnit != close)
            {
                row.PricePerUnit = close;
                row.Source = PriceSources.Synced;
                row.ImportedAt = now;
                replaced++;
            }
        }

        var removed = 0;
        if (replaceHistory)
        {
            var orphans = await db.InvestmentPriceSnapshots
                .Where(p => p.AssetId == asset.Id && p.Source == PriceSources.Synced)
                .ToListAsync(ct);
            foreach (var row in orphans.Where(p => !closes.ContainsKey(p.Date)))
            {
                db.InvestmentPriceSnapshots.Remove(row);
                removed++;
            }
        }

        return (added, replaced, kept, removed);
    }

    private async Task<AssetPriceSyncResult> FailAsync(InvestmentAsset asset, string? symbol, string error, CancellationToken ct)
    {
        logger.LogWarning("Price sync failed for {Name}: {Error}", asset.Name, error);

        // Drop half-made price changes; keep a ticker found from the ISIN and record the failure.
        foreach (var entry in db.ChangeTracker.Entries<InvestmentPriceSnapshot>().ToList())
        {
            if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
            else if (entry.State is EntityState.Modified or EntityState.Deleted) entry.State = EntityState.Unchanged;
        }

        // A failed save may have left the success path's values on the asset.
        var tracked = db.Entry(asset);
        tracked.Property(a => a.PricesSyncedAt).CurrentValue = tracked.Property(a => a.PricesSyncedAt).OriginalValue;
        tracked.Property(a => a.PricesSymbol).CurrentValue = tracked.Property(a => a.PricesSymbol).OriginalValue;

        asset.PriceSyncError = error.Length > 500 ? error[..500] : error;
        await db.SaveChangesAsync(ct);
        return new AssetPriceSyncResult(asset.Id, asset.Name, symbol, 0, 0, 0, 0, null, error);
    }

    /// <summary>The asset has no symbol to price it with.</summary>
    private sealed class SymbolException(string message) : Exception(message);
}
