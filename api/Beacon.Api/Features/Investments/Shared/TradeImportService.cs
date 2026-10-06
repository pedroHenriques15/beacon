using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services.Parsing;
using Beacon.Api.Services.Pricing;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Shared;

/// <summary>
/// Turns the investment buys a parser found on an imported statement (<see cref="ParsedTrade"/>)
/// into <see cref="InvestmentLot"/>s, creating the ETF asset, matched by ISIN, on first sight
/// (ADR-031).
///
/// The statement rows themselves stay debits and are excluded from spending (see
/// <c>StatementUploadService</c>); this service mirrors them into the Investments feature so the
/// holdings show up there. It is idempotent: a trade with an id is skipped when a lot already
/// holds that id in <see cref="InvestmentLot.ExternalId"/>, and one without an id is skipped when
/// a lot of the same asset, date and quantity exists.
/// </summary>
public class TradeImportService(AppDbContext db, PriceSyncQueue priceSyncQueue)
{
    /// <summary>Creates a lot for each trade not imported yet and returns how many were added.</summary>
    public async Task<int> ImportAsync(IReadOnlyList<ParsedTrade> trades, CancellationToken ct = default)
    {
        var assetsByIsin = new Dictionary<string, InvestmentAsset>(StringComparer.OrdinalIgnoreCase);
        var created = new List<InvestmentAsset>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenKeys = new HashSet<(string Isin, DateOnly Date, decimal Quantity)>();
        var imported = 0;

        foreach (var trade in trades)
        {
            if (trade.ExternalId is not null
                && (!seenIds.Add(trade.ExternalId)
                    || await db.InvestmentLots.AnyAsync(l => l.ExternalId == trade.ExternalId, ct)))
                continue;

            if (!assetsByIsin.TryGetValue(trade.Isin, out var asset))
            {
                asset = await db.InvestmentAssets.FirstOrDefaultAsync(a => a.Isin == trade.Isin, ct);
                if (asset is null)
                {
                    asset = new InvestmentAsset
                    {
                        AssetType = "ETF",
                        Isin = trade.Isin,
                        Ticker = null, // the price sync finds it from the ISIN
                        Name = trade.AssetName,
                        Notes = $"Auto-created from an imported buy ({trade.Isin}). " +
                                "Its ticker is found from the ISIN; if prices don't appear, set it by hand.",
                        ImportedAt = DateTime.UtcNow,
                    };
                    db.InvestmentAssets.Add(asset);
                    created.Add(asset);
                }
                assetsByIsin[trade.Isin] = asset;
            }

            // A trade without an id dedups within this batch, then against the persisted lots.
            if (trade.ExternalId is null
                && (!seenKeys.Add((trade.Isin, trade.Date, trade.Quantity))
                    || (asset.Id != 0 && await db.InvestmentLots.AnyAsync(
                        l => l.AssetId == asset.Id && l.Date == trade.Date && l.Quantity == trade.Quantity, ct))))
                continue;

            db.InvestmentLots.Add(new InvestmentLot
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
            imported++;
        }

        if (imported > 0) await db.SaveChangesAsync(ct);

        // A new holding gets its ticker and history now rather than at the next daily run.
        foreach (var asset in created.Where(a => a.Id != 0))
            priceSyncQueue.Enqueue(asset.Id);

        return imported;
    }
}
