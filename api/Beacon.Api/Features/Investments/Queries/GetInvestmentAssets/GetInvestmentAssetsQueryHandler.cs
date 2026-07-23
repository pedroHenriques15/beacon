using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;

public record InvestmentLotResponse(
    int Id,
    int AssetId,
    DateOnly Date,
    decimal Quantity,
    decimal PricePerUnit,
    decimal? Fees,
    string? Notes);

public record InvestmentPriceSnapshotResponse(
    int Id,
    int AssetId,
    DateOnly Date,
    decimal PricePerUnit);

public record InvestmentAssetResponse(
    int Id,
    string AssetType,
    string? Ticker,
    string Name,
    string? Notes,
    List<InvestmentLotResponse> Lots,
    List<InvestmentPriceSnapshotResponse> PriceSnapshots);

public class GetInvestmentAssetsQueryHandler(AppDbContext db)
{
    public async Task<List<InvestmentAssetResponse>> HandleAsync(CancellationToken ct = default) =>
        await db.InvestmentAssets
            .Include(a => a.Lots)
            .Include(a => a.PriceSnapshots)
            .OrderBy(a => a.Name)
            .Select(a => new InvestmentAssetResponse(
                a.Id, a.AssetType, a.Ticker, a.Name, a.Notes,
                a.Lots
                    .OrderByDescending(l => l.Date)
                    .Select(l => new InvestmentLotResponse(l.Id, l.AssetId, l.Date, l.Quantity, l.PricePerUnit, l.Fees, l.Notes))
                    .ToList(),
                a.PriceSnapshots
                    .OrderByDescending(p => p.Date)
                    .Select(p => new InvestmentPriceSnapshotResponse(p.Id, p.AssetId, p.Date, p.PricePerUnit))
                    .ToList()))
            .ToListAsync(ct);
}
