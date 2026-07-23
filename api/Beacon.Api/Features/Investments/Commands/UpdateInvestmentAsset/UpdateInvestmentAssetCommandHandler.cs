using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.UpdateInvestmentAsset;

public record UpdateInvestmentAssetCommand(int Id, string? Ticker, string Name, string? Notes);

public class UpdateInvestmentAssetCommandHandler(AppDbContext db)
{
    public async Task<(InvestmentAssetResponse? Result, string? Error)> HandleAsync(
        UpdateInvestmentAssetCommand command, CancellationToken ct = default)
    {
        var asset = await db.InvestmentAssets
            .Include(a => a.Lots)
            .Include(a => a.PriceSnapshots)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == command.Id, ct);

        if (asset is null) return (null, null);

        if (string.IsNullOrWhiteSpace(command.Name))
            return (null, "Name is required.");

        if (asset.AssetType == "ETF")
        {
            if (string.IsNullOrWhiteSpace(command.Ticker))
                return (null, "Ticker is required for ETF assets.");

            var ticker = command.Ticker.Trim().ToUpperInvariant();
            var duplicate = await db.InvestmentAssets
                .AnyAsync(a => a.AssetType == "ETF" && a.Ticker == ticker && a.Id != command.Id, ct);
            if (duplicate) return (null, $"An ETF with ticker '{ticker}' already exists.");

            asset.Ticker = ticker;
        }
        else
        {
            var duplicate = await db.InvestmentAssets
                .AnyAsync(a => a.AssetType == "Gold" && a.Name == command.Name.Trim() && a.Id != command.Id, ct);
            if (duplicate) return (null, "A Gold asset with this name already exists.");

            asset.Ticker = null;
        }

        asset.Name  = command.Name.Trim();
        asset.Notes = command.Notes?.Trim();
        await db.SaveChangesAsync(ct);

        return (new InvestmentAssetResponse(
            asset.Id, asset.AssetType, asset.Ticker, asset.Name, asset.Notes,
            asset.Lots.OrderByDescending(l => l.Date)
                .Select(l => new InvestmentLotResponse(l.Id, l.AssetId, l.Date, l.Quantity, l.PricePerUnit, l.Fees, l.Notes))
                .ToList(),
            asset.PriceSnapshots.OrderByDescending(p => p.Date)
                .Select(p => new InvestmentPriceSnapshotResponse(p.Id, p.AssetId, p.Date, p.PricePerUnit))
                .ToList()), null);
    }
}
