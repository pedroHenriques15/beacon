using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.FetchInvestmentPrice;

public record FetchInvestmentPriceCommand(int AssetId);

public class FetchInvestmentPriceCommandHandler(AppDbContext db, AlphaVantageService alphaVantage)
{
    public async Task<(InvestmentPriceSnapshotResponse? Result, string? Error)> HandleAsync(
        FetchInvestmentPriceCommand command, CancellationToken ct = default)
    {
        var asset = await db.InvestmentAssets.FindAsync([command.AssetId], ct);
        if (asset is null) return (null, null);

        decimal price;
        try
        {
            price = asset.AssetType == "ETF"
                ? await alphaVantage.FetchEtfPriceAsync(asset.Ticker!, ct)
                : await alphaVantage.FetchGoldPricePerGramAsync(ct);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existing = await db.InvestmentPriceSnapshots
            .FirstOrDefaultAsync(p => p.AssetId == command.AssetId && p.Date == today, ct);

        if (existing is not null)
        {
            existing.PricePerUnit = price;
            existing.ImportedAt   = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return (new InvestmentPriceSnapshotResponse(existing.Id, existing.AssetId, existing.Date, existing.PricePerUnit), null);
        }

        var snapshot = new InvestmentPriceSnapshot
        {
            AssetId      = command.AssetId,
            Date         = today,
            PricePerUnit = price,
            ImportedAt   = DateTime.UtcNow,
        };

        db.InvestmentPriceSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);

        return (new InvestmentPriceSnapshotResponse(snapshot.Id, snapshot.AssetId, snapshot.Date, snapshot.PricePerUnit), null);
    }
}
