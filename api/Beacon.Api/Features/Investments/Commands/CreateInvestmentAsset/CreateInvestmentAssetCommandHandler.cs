using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.CreateInvestmentAsset;

public record CreateInvestmentAssetCommand(string AssetType, string? Ticker, string Name, string? Notes);

public class CreateInvestmentAssetCommandHandler(AppDbContext db)
{
    private static readonly string[] ValidTypes = ["ETF", "Gold"];

    public async Task<(InvestmentAssetResponse? Result, string? Error)> HandleAsync(
        CreateInvestmentAssetCommand command, CancellationToken ct = default)
    {
        if (!ValidTypes.Contains(command.AssetType))
            return (null, $"AssetType must be one of: {string.Join(", ", ValidTypes)}.");

        if (command.AssetType == "ETF" && string.IsNullOrWhiteSpace(command.Ticker))
            return (null, "Ticker is required for ETF assets.");

        if (string.IsNullOrWhiteSpace(command.Name))
            return (null, "Name is required.");

        var ticker = command.Ticker?.Trim().ToUpperInvariant();

        if (command.AssetType == "ETF")
        {
            var duplicate = await db.InvestmentAssets
                .AnyAsync(a => a.AssetType == "ETF" && a.Ticker == ticker, ct);
            if (duplicate) return (null, $"An ETF with ticker '{ticker}' already exists.");
        }
        else
        {
            var duplicate = await db.InvestmentAssets
                .AnyAsync(a => a.AssetType == "Gold" && a.Name == command.Name.Trim(), ct);
            if (duplicate) return (null, "A Gold asset with this name already exists.");
        }

        var asset = new InvestmentAsset
        {
            AssetType  = command.AssetType,
            Ticker     = ticker,
            Name       = command.Name.Trim(),
            Notes      = command.Notes?.Trim(),
            ImportedAt = DateTime.UtcNow,
        };

        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync(ct);

        return (new InvestmentAssetResponse(asset.Id, asset.AssetType, asset.Ticker, asset.Name, asset.Notes, [], []), null);
    }
}
