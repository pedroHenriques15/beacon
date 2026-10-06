using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Services.Parsing;
using Microsoft.EntityFrameworkCore;
using static System.FormattableString;

namespace Beacon.Api.Services;

/// <summary>What a broker's export added to Invest.</summary>
/// <param name="TradeCount">The buys and sells in the file.</param>
/// <param name="Added">The lots created; the other trades were imported before or typed by hand.</param>
/// <param name="Warnings">Holdings that differ from the broker's own list.</param>
public record TradesUploadResult(
    string Broker,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    int TradeCount,
    int Added,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Imports XTB's account exports as investment lots (ADR-034): XTB has no statement in Beacon,
/// so a file's only trace is the lots its trades become, each holding the operation's id.
/// </summary>
public class XtbUploadService(AppDbContext db, TradeImportService tradeImport, ILogger<XtbUploadService> logger)
{
    public const string Broker = "XTB";

    /// <summary>
    /// Books the export's trades not imported yet. Refuses the whole file when a sell would leave
    /// more sold than held (<see cref="InvalidOperationException"/>), saving none of it.
    /// </summary>
    public async Task<TradesUploadResult> ImportAsync(XtbExport export, CancellationToken ct = default)
    {
        var added = await tradeImport.ImportAsync(export.Trades, ct);
        logger.LogInformation("Imported {Added} of {Trades} XTB trade(s) as lots for {From}..{To}",
            added, export.Trades.Count, export.PeriodFrom, export.PeriodTo);
        return new TradesUploadResult(Broker, export.PeriodFrom, export.PeriodTo, export.Trades.Count, added, []);
    }

    /// <summary>
    /// Compares what Beacon holds from XTB with the export's Open Positions, one warning per asset
    /// that differs. XTB lists what is held when the file is generated, not at the period's end, so
    /// the check runs only when no XTB trade in Beacon is later than the export's period: an older
    /// month's file would be compared against trades it does not contain.
    /// </summary>
    public async Task<IReadOnlyList<string>> CheckHoldingsAsync(XtbExport export, CancellationToken ct = default)
    {
        var lastTrade = await XtbLots().MaxAsync(l => (DateOnly?)l.Date, ct);
        if (lastTrade > export.PeriodTo) return [];

        var listed = new Dictionary<int, decimal>();
        var labels = new Dictionary<int, string>();
        var warnings = new List<string>();
        var asOf = XtbExportParser.LisbonDate(export.GeneratedAtUtc);

        foreach (var holding in export.Holdings)
        {
            var asset = await tradeImport.FindByTickerAsync(holding.Ticker, ct);
            if (asset is null)
            {
                warnings.Add(Differs(holding.Ticker, holding.Quantity, 0m, asOf));
                continue;
            }
            listed[asset.Id] = listed.GetValueOrDefault(asset.Id) + holding.Quantity;
            labels.TryAdd(asset.Id, holding.Ticker);
        }

        // Lots another source imported (a Trade Republic buy of the same ETF) are not XTB's; a lot
        // typed by hand may be.
        var assetIds = listed.Keys.Union(await XtbLots().Select(l => l.AssetId).Distinct().ToListAsync(ct)).ToList();
        var lots = await db.InvestmentLots
            .AsNoTracking()
            .Where(l => assetIds.Contains(l.AssetId)
                && (l.ExternalId == null || l.ExternalId.StartsWith(XtbExportParser.ExternalIdPrefix)))
            .Select(l => new { l.AssetId, l.Quantity, l.Asset.Ticker, l.Asset.PricesSymbol, l.Asset.Name })
            .ToListAsync(ct);

        foreach (var asset in lots.GroupBy(l => l.AssetId))
        {
            var held = asset.Sum(l => l.Quantity);
            var expected = listed.GetValueOrDefault(asset.Key);
            if (held == expected) continue;
            var first = asset.First();
            var label = labels.GetValueOrDefault(asset.Key) ?? first.Ticker ?? first.PricesSymbol ?? first.Name;
            warnings.Add(Differs(label, expected, held, asOf));
        }
        // An asset XTB lists that has no lot left to count.
        foreach (var (assetId, expected) in listed.Where(l => lots.All(lot => lot.AssetId != l.Key) && l.Value != 0))
            warnings.Add(Differs(labels[assetId], expected, 0m, asOf));

        return warnings;
    }

    private IQueryable<Models.InvestmentLot> XtbLots() =>
        db.InvestmentLots.Where(l => l.ExternalId != null && l.ExternalId.StartsWith(XtbExportParser.ExternalIdPrefix));

    private static string Differs(string label, decimal atXtb, decimal inBeacon, DateOnly asOf) =>
        Invariant($"{label}: XTB lists {atXtb:0.######} held on {asOf:d MMM yyyy}, Beacon has {inBeacon:0.######}. ") +
        "Is a month not imported yet? XTB's Open Positions shows what is held when the file is made.";
}
