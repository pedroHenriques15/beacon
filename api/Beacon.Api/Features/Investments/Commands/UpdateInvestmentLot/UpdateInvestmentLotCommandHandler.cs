using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.UpdateInvestmentLot;

public record UpdateInvestmentLotCommand(
    int Id,
    DateOnly Date,
    decimal Quantity,
    decimal PricePerUnit,
    decimal? Fees,
    string? Notes);

public class UpdateInvestmentLotCommandHandler(AppDbContext db)
{
    public async Task<(InvestmentLotResponse? Result, string? Error)> HandleAsync(
        UpdateInvestmentLotCommand command, CancellationToken ct = default)
    {
        var lot = await db.InvestmentLots.FindAsync([command.Id], ct);
        if (lot is null) return (null, "Investment lot not found.");

        if (command.Quantity == 0)
            return (null, "Quantity must not be zero.");

        if (command.PricePerUnit <= 0)
            return (null, "PricePerUnit must be greater than zero.");

        lot.Date         = command.Date;
        lot.Quantity     = command.Quantity;
        lot.PricePerUnit = command.PricePerUnit;
        lot.Fees         = command.Fees;
        lot.Notes        = command.Notes?.Trim();
        await db.SaveChangesAsync(ct);

        return (new InvestmentLotResponse(lot.Id, lot.AssetId, lot.Date, lot.Quantity, lot.PricePerUnit, lot.Fees, lot.Notes), null);
    }
}
