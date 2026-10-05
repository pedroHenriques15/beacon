using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Queries.GetAssetPrices;

public record GetAssetPricesQuery(int AssetId);

/// <summary>Every price of one asset, newest first: its history chart and price table.</summary>
public class GetAssetPricesQueryHandler(AppDbContext db)
{
    /// <returns>Null when the asset does not exist.</returns>
    public async Task<List<InvestmentPriceSnapshotResponse>?> HandleAsync(GetAssetPricesQuery query, CancellationToken ct = default)
    {
        if (!await db.InvestmentAssets.AnyAsync(a => a.Id == query.AssetId, ct)) return null;

        return await db.InvestmentPriceSnapshots
            .Where(p => p.AssetId == query.AssetId)
            .OrderByDescending(p => p.Date)
            .Select(p => new InvestmentPriceSnapshotResponse(p.Id, p.AssetId, p.Date, p.PricePerUnit, p.Source))
            .ToListAsync(ct);
    }
}
