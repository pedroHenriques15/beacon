using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.UpsertInvestmentPrice;

public record UpsertInvestmentPriceCommand(int AssetId, DateOnly Date, decimal PricePerUnit);

public class UpsertInvestmentPriceCommandHandler(AppDbContext db)
{
    public async Task<(InvestmentPriceSnapshotResponse? Result, string? Error)> HandleAsync(
        UpsertInvestmentPriceCommand command, CancellationToken ct = default)
    {
        var assetExists = await db.InvestmentAssets.AnyAsync(a => a.Id == command.AssetId, ct);
        if (!assetExists) return (null, "Investment asset not found.");

        if (command.PricePerUnit <= 0)
            return (null, "PricePerUnit must be greater than zero.");

        var existing = await db.InvestmentPriceSnapshots
            .FirstOrDefaultAsync(p => p.AssetId == command.AssetId && p.Date == command.Date, ct);

        if (existing is not null)
        {
            existing.PricePerUnit = command.PricePerUnit;
            existing.ImportedAt   = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return (new InvestmentPriceSnapshotResponse(existing.Id, existing.AssetId, existing.Date, existing.PricePerUnit), null);
        }

        var snapshot = new InvestmentPriceSnapshot
        {
            AssetId      = command.AssetId,
            Date         = command.Date,
            PricePerUnit = command.PricePerUnit,
            ImportedAt   = DateTime.UtcNow,
        };

        db.InvestmentPriceSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);

        return (new InvestmentPriceSnapshotResponse(snapshot.Id, snapshot.AssetId, snapshot.Date, snapshot.PricePerUnit), null);
    }
}
