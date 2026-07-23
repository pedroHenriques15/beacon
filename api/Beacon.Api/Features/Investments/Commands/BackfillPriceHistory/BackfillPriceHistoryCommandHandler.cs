using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.BackfillPriceHistory;

public record BackfillPriceHistoryCommand(int AssetId);

public record BackfillPriceHistoryResponse(
    int AssetId,
    int SnapshotsAdded,
    int SnapshotsSkipped,
    DateOnly? EarliestDate,
    DateOnly? LatestDate,
    string Message);

public class BackfillPriceHistoryCommandHandler(AppDbContext db, AlphaVantageService alphaVantage)
{
    public async Task<(BackfillPriceHistoryResponse? Result, string? Error)> HandleAsync(
        BackfillPriceHistoryCommand command, CancellationToken ct = default)
    {
        var asset = await db.InvestmentAssets.FindAsync([command.AssetId], ct);
        if (asset is null) return (null, null);

        var earliestLotDate = await db.InvestmentLots
            .Where(l => l.AssetId == command.AssetId)
            .MinAsync(l => (DateOnly?)l.Date, ct);
        if (earliestLotDate is null)
            return (null, "Add at least one lot before backfilling history.");

        var existingDates = (await db.InvestmentPriceSnapshots
            .Where(p => p.AssetId == command.AssetId)
            .Select(p => p.Date)
            .ToListAsync(ct)).ToHashSet();

        if (existingDates.Count > 0 && existingDates.Min() <= earliestLotDate.Value.AddDays(7))
            return (new BackfillPriceHistoryResponse(
                command.AssetId, 0, 0, existingDates.Min(), existingDates.Max(),
                "History already covers the period since the first lot — no API call used."), null);

        Dictionary<DateOnly, decimal> series;
        try
        {
            series = await alphaVantage.FetchDailySeriesAsync(asset, ct);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var candidates = series
            .Where(kv => kv.Key >= earliestLotDate.Value && kv.Key < today)
            .OrderBy(kv => kv.Key)
            .ToList();

        var added   = 0;
        var skipped = 0;
        foreach (var (date, price) in candidates)
        {
            if (existingDates.Contains(date))
            {
                skipped++;
                continue;
            }

            db.InvestmentPriceSnapshots.Add(new InvestmentPriceSnapshot
            {
                AssetId      = command.AssetId,
                Date         = date,
                PricePerUnit = price,
                ImportedAt   = DateTime.UtcNow,
            });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);

        var message = candidates.Count == 0
            ? "No historical prices available for the period."
            : added == 0
                ? "History already complete — nothing to add."
                : $"Added {added} daily price(s).";

        return (new BackfillPriceHistoryResponse(
            command.AssetId, added, skipped,
            candidates.Count > 0 ? candidates[0].Key : null,
            candidates.Count > 0 ? candidates[^1].Key : null,
            message), null);
    }
}
