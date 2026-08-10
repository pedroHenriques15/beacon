using System.Globalization;
using System.Text.RegularExpressions;
using Beacon.Api.Data;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Shared;

/// <summary>
/// Turns a persisted Trade Republic statement's "Savings plan execution" ETF buys into
/// <see cref="InvestmentLot"/>s, creating the ETF asset (matched by ISIN) on first sight.
///
/// The bank rows themselves stay debits and are excluded from spending (see
/// <c>StatementUploadService</c>); this service mirrors them into the Investments feature so the
/// holdings show up there. It is idempotent - lots are deduped by (asset, date, quantity) so a
/// re-imported or period-overlapping statement never double-books.
/// </summary>
public partial class SavingsPlanImportService(AppDbContext db, ILogger<SavingsPlanImportService> logger)
{
    // ISIN: 2 country letters + 9 alphanumeric + 1 check digit (e.g. IE00BK5BQT80).
    [GeneratedRegex(@"\b([A-Z]{2}[A-Z0-9]{9}[0-9])\b")]
    private static partial Regex IsinRegex();

    // "..., quantity: 0.031295"
    [GeneratedRegex(@"quantity:\s*([0-9]+(?:\.[0-9]+)?)")]
    private static partial Regex QuantityRegex();

    /// <summary>Creates lots for the statement's savings-plan rows and returns how many were added.</summary>
    public async Task<int> ImportAsync(MonthlyStatement statement, CancellationToken ct = default)
    {
        if (!string.Equals(statement.Bank, "TRADE REPUBLIC", StringComparison.Ordinal))
            return 0;

        var rows = statement.Transactions
            .Where(t => t.Description.Contains("Savings plan execution", StringComparison.Ordinal))
            .ToList();
        if (rows.Count == 0) return 0;

        var assetsByIsin = new Dictionary<string, InvestmentAsset>(StringComparer.Ordinal);
        var seen         = new HashSet<(string Isin, DateOnly Date, decimal Quantity)>();
        var imported     = 0;

        foreach (var tx in rows)
        {
            var isinMatch = IsinRegex().Match(tx.Description);
            var qtyMatch  = QuantityRegex().Match(tx.Description);
            if (!isinMatch.Success || !qtyMatch.Success)
            {
                logger.LogWarning(
                    "Skipping a Trade Republic savings-plan row without a parseable ISIN/quantity: {Description}",
                    tx.Description);
                continue;
            }

            var isin     = isinMatch.Groups[1].Value;
            var quantity = decimal.Parse(qtyMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            if (quantity <= 0) continue;

            // The statement gives the exact EUR paid and the exact quantity; derive the unit price.
            var pricePerUnit = Math.Round(tx.Amount / quantity, 4, MidpointRounding.AwayFromZero);

            if (!assetsByIsin.TryGetValue(isin, out var asset))
            {
                asset = await db.InvestmentAssets.FirstOrDefaultAsync(a => a.Isin == isin, ct);
                if (asset is null)
                {
                    asset = new InvestmentAsset
                    {
                        AssetType  = "ETF",
                        Isin       = isin,
                        Ticker     = null, // user sets the Alpha Vantage ticker to enable pricing
                        Name       = ExtractFundName(tx.Description, isin) ?? $"ETF {isin}",
                        Notes      = $"Auto-created from a Trade Republic savings plan ({isin}). " +
                                     "Set the ETF ticker to enable price updates.",
                        ImportedAt = DateTime.UtcNow,
                    };
                    db.InvestmentAssets.Add(asset);
                }
                assetsByIsin[isin] = asset;
            }

            // Dedup within this batch, then against already-persisted lots (existing asset only).
            if (!seen.Add((isin, tx.DatePosting, quantity)))
                continue;
            if (asset.Id != 0 && await db.InvestmentLots.AnyAsync(
                    l => l.AssetId == asset.Id && l.Date == tx.DatePosting && l.Quantity == quantity, ct))
                continue;

            db.InvestmentLots.Add(new InvestmentLot
            {
                Asset        = asset,
                Date         = tx.DatePosting,
                Quantity     = quantity,
                PricePerUnit = pricePerUnit,
                Fees         = 0m, // Trade Republic savings plans are free
                Notes        = "Trade Republic savings plan",
                ImportedAt   = DateTime.UtcNow,
            });
            imported++;
        }

        if (imported > 0) await db.SaveChangesAsync(ct);
        return imported;
    }

    /// <summary>The fund name sits between the ISIN and the ", quantity:" suffix.</summary>
    private static string? ExtractFundName(string description, string isin)
    {
        var start = description.IndexOf(isin, StringComparison.Ordinal);
        if (start < 0) return null;
        start += isin.Length;

        var qi  = description.IndexOf("quantity:", start, StringComparison.Ordinal);
        var end = qi < 0 ? description.Length : qi;

        var name = description[start..end].Trim().TrimEnd(',').Trim();
        return name.Length == 0 ? null : name;
    }
}
