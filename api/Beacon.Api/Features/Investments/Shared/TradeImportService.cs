using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services.Parsing;
using Beacon.Api.Services.Pricing;
using Microsoft.EntityFrameworkCore;
using static System.FormattableString;

namespace Beacon.Api.Features.Investments.Shared;

/// <summary>
/// Turns the investment trades a parser found (<see cref="ParsedTrade"/>) into
/// <see cref="InvestmentLot"/>s, creating the ETF asset on first sight (ADR-031, ADR-034): matched
/// by ISIN, or by ticker and then by the symbol its prices sync from.
///
/// A statement's rows stay debits and are excluded from spending (see
/// <c>StatementUploadService</c>); a broker's export has no rows at all. Either way this service
/// mirrors the trades into the Investments feature so the holdings show up there. It is
/// idempotent: a trade with an id is skipped when a lot already holds that id in
/// <see cref="InvestmentLot.ExternalId"/>, and when a lot without an id has the same asset, date
/// and quantity (a lot typed by hand before the import) that lot takes the id instead of getting a
/// twin. A trade without an id is skipped when a lot of the same asset, date and quantity exists.
/// A sell is checked against the holdings, as a manual sell is.
/// </summary>
public class TradeImportService(AppDbContext db, PriceSyncQueue priceSyncQueue)
{
    /// <summary>
    /// Creates a lot for each trade not imported yet, in the order given, and returns how many were
    /// added. Saves nothing when a sell would leave more sold than held
    /// (<see cref="InvalidOperationException"/>).
    /// </summary>
    public async Task<int> ImportAsync(IReadOnlyList<ParsedTrade> trades, CancellationToken ct = default)
    {
        var holdings = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenKeys = new HashSet<(string Asset, DateOnly Date, decimal Quantity)>();
        var lots = new List<InvestmentLot>();
        var adopted = new List<(InvestmentLot Lot, string ExternalId)>();

        foreach (var trade in trades)
        {
            if (trade.ExternalId is not null
                && (!seenIds.Add(trade.ExternalId)
                    || await db.InvestmentLots.AnyAsync(l => l.ExternalId == trade.ExternalId, ct)))
                continue;

            var key = AssetKey(trade);
            if (!holdings.TryGetValue(key, out var holding))
            {
                holding = await ResolveAsync(trade, holdings.Values, ct);
                holdings[key] = holding;
            }
            var asset = holding.Asset;

            if (trade.ExternalId is null)
            {
                // A trade without an id dedups within this batch, then against the persisted lots.
                if (!seenKeys.Add((key, trade.Date, trade.Quantity))
                    || (asset.Id != 0 && await db.InvestmentLots.AnyAsync(
                        l => l.AssetId == asset.Id && l.Date == trade.Date && l.Quantity == trade.Quantity, ct)))
                    continue;
            }
            else if (asset.Id != 0)
            {
                var adoptedIds = adopted.Select(a => a.Lot.Id).ToList();
                var typed = await db.InvestmentLots
                    .Where(l => l.AssetId == asset.Id && l.ExternalId == null
                        && l.Date == trade.Date && l.Quantity == trade.Quantity
                        && !adoptedIds.Contains(l.Id))
                    .OrderBy(l => l.Id)
                    .FirstOrDefaultAsync(ct);
                if (typed is not null)
                {
                    adopted.Add((typed, trade.ExternalId));
                    continue;
                }
            }

            if (trade.Quantity < 0 && holding.Held + trade.Quantity < 0)
                throw new InvalidOperationException(Invariant(
                    $"Cannot sell {-trade.Quantity:0.######} {trade.Ticker ?? trade.Isin} on {trade.Date:yyyy-MM-dd} - only {holding.Held:0.######} held."));
            holding.Held += trade.Quantity;

            lots.Add(new InvestmentLot
            {
                Asset = asset,
                Date = trade.Date,
                Quantity = trade.Quantity,
                PricePerUnit = trade.PricePerUnit,
                Fees = trade.Fees,
                Notes = trade.Note,
                ExternalId = trade.ExternalId,
                ImportedAt = DateTime.UtcNow,
            });
        }

        // Only now that every sell is checked, so a refused import leaves nothing behind.
        if (lots.Count == 0 && adopted.Count == 0) return 0;
        foreach (var (lot, externalId) in adopted) lot.ExternalId = externalId;
        db.InvestmentLots.AddRange(lots);
        await db.SaveChangesAsync(ct);

        // A new holding gets its ticker and history now rather than at the next daily run.
        foreach (var holding in holdings.Values.Where(h => h.IsNew && h.Asset.Id != 0).DistinctBy(h => h.Asset.Id))
            priceSyncQueue.Enqueue(holding.Asset.Id);

        return lots.Count;
    }

    /// <summary>
    /// The asset a ticker names: the one with that <see cref="InvestmentAsset.Ticker"/>, or else the
    /// one whose prices sync from that symbol (an asset created from an ISIN has no ticker).
    /// </summary>
    public async Task<InvestmentAsset?> FindByTickerAsync(string ticker, CancellationToken ct = default)
    {
        var symbol = ticker.ToLowerInvariant();
        return await db.InvestmentAssets
                   .Where(a => a.Ticker != null && a.Ticker.ToLower() == symbol)
                   .OrderBy(a => a.Id)
                   .FirstOrDefaultAsync(ct)
               ?? await db.InvestmentAssets
                   .Where(a => a.PricesSymbol != null && a.PricesSymbol.ToLower() == symbol)
                   .OrderBy(a => a.Id)
                   .FirstOrDefaultAsync(ct);
    }

    private static string AssetKey(ParsedTrade trade) =>
        trade.Isin is not null ? $"isin:{trade.Isin}"
        : trade.Ticker is not null ? $"ticker:{trade.Ticker}"
        : throw new ArgumentException("A trade names its asset by ISIN or by ticker.", nameof(trade));

    private async Task<Holding> ResolveAsync(ParsedTrade trade, IEnumerable<Holding> known, CancellationToken ct)
    {
        var asset = trade.Isin is not null
            ? await db.InvestmentAssets.FirstOrDefaultAsync(a => a.Isin == trade.Isin, ct)
            : await FindByTickerAsync(trade.Ticker!, ct);

        if (asset is null)
            return new Holding(trade.Isin is not null
                ? new InvestmentAsset
                {
                    AssetType = "ETF",
                    Isin = trade.Isin,
                    Ticker = null, // the price sync finds it from the ISIN
                    Name = trade.AssetName,
                    Notes = $"Auto-created from an imported buy ({trade.Isin}). " +
                            "Its ticker is found from the ISIN; if prices don't appear, set it by hand.",
                    ImportedAt = DateTime.UtcNow,
                }
                : new InvestmentAsset
                {
                    AssetType = "ETF",
                    Ticker = trade.Ticker,
                    Name = trade.AssetName,
                    Notes = $"Auto-created from an imported trade ({trade.Ticker}). " +
                            "If prices don't appear, correct its ticker by hand.",
                    ImportedAt = DateTime.UtcNow,
                }, held: 0, isNew: true);

        // Two names for one asset (an ISIN and a ticker) share its holdings.
        if (known.FirstOrDefault(h => h.Asset.Id == asset.Id) is { } same) return same;

        var held = await db.InvestmentLots
            .Where(l => l.AssetId == asset.Id)
            .SumAsync(l => (decimal?)l.Quantity, ct) ?? 0;
        return new Holding(asset, held, isNew: false);
    }

    /// <summary>An asset this import touches, and how much of it is held so far.</summary>
    private sealed class Holding(InvestmentAsset asset, decimal held, bool isNew)
    {
        public InvestmentAsset Asset { get; } = asset;
        public decimal Held { get; set; } = held;
        public bool IsNew { get; } = isNew;
    }
}
