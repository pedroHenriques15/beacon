using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.CreateInvestmentLot;

public record CreateInvestmentLotCommand(
    int AssetId,
    DateOnly Date,
    decimal Quantity,
    decimal PricePerUnit,
    decimal? Fees,
    string? Notes);

public class CreateInvestmentLotCommandHandler(AppDbContext db)
{
    public async Task<(InvestmentLotResponse? Result, string? Error)> HandleAsync(
        CreateInvestmentLotCommand command, CancellationToken ct = default)
    {
        var assetExists = await db.InvestmentAssets.AnyAsync(a => a.Id == command.AssetId, ct);
        if (!assetExists) return (null, "Investment asset not found.");

        if (command.Quantity == 0)
            return (null, "Quantity must not be zero.");

        if (command.PricePerUnit <= 0)
            return (null, "PricePerUnit must be greater than zero.");

        if (command.Quantity < 0)
        {
            var held = await db.InvestmentLots
                .Where(l => l.AssetId == command.AssetId)
                .SumAsync(l => (decimal?)l.Quantity, ct) ?? 0;
            if (held + command.Quantity < 0)
                return (null, $"Cannot sell {Math.Abs(command.Quantity):0.####} - only {held:0.####} held.");
        }

        var lot = new InvestmentLot
        {
            AssetId      = command.AssetId,
            Date         = command.Date,
            Quantity     = command.Quantity,
            PricePerUnit = command.PricePerUnit,
            Fees         = command.Fees,
            Notes        = command.Notes?.Trim(),
            ImportedAt   = DateTime.UtcNow,
        };

        db.InvestmentLots.Add(lot);
        await db.SaveChangesAsync(ct);

        return (new InvestmentLotResponse(lot.Id, lot.AssetId, lot.Date, lot.Quantity, lot.PricePerUnit, lot.Fees, lot.Notes), null);
    }
}
