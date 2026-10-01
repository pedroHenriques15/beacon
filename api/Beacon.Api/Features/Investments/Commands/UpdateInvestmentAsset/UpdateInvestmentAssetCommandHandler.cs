using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Beacon.Api.Services.Pricing;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.UpdateInvestmentAsset;

public record UpdateInvestmentAssetCommand(int Id, string? Ticker, string Name, string? Notes);

public class UpdateInvestmentAssetCommandHandler(AppDbContext db, PriceSyncQueue priceSyncQueue)
{
    public async Task<(InvestmentAssetResponse? Result, string? Error)> HandleAsync(
        UpdateInvestmentAssetCommand command, CancellationToken ct = default)
    {
        var asset = await db.InvestmentAssets
            .Include(a => a.Lots)
            .FirstOrDefaultAsync(a => a.Id == command.Id, ct);

        if (asset is null) return (null, null);
        var previousTicker = asset.Ticker;

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

        asset.Name = command.Name.Trim();
        asset.Notes = command.Notes?.Trim();
        await db.SaveChangesAsync(ct);
        if (asset.Ticker != previousTicker) priceSyncQueue.Enqueue(asset.Id);

        var lots = asset.Lots.OrderByDescending(l => l.Date)
            .Select(l => new InvestmentLotResponse(l.Id, l.AssetId, l.Date, l.Quantity, l.PricePerUnit, l.Fees, l.Notes))
            .ToList();
        var prices = await RecentPrices.LoadAsync(
            db, new Dictionary<int, DateOnly?> { [asset.Id] = lots.Select(l => (DateOnly?)l.Date).Min() }, ct);
        var (count, recent) = prices.GetValueOrDefault(asset.Id, (0, []));

        return (new InvestmentAssetResponse(
            asset.Id, asset.AssetType, asset.Ticker, asset.Isin, asset.Name, asset.Notes,
            asset.PricesSyncedAt, asset.PriceSyncError, count, lots, recent), null);
    }
}
