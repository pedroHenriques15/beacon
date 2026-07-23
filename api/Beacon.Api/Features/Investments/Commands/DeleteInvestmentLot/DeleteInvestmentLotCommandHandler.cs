using Beacon.Api.Data;

namespace Beacon.Api.Features.Investments.Commands.DeleteInvestmentLot;

public record DeleteInvestmentLotCommand(int Id);

public class DeleteInvestmentLotCommandHandler(AppDbContext db)
{
    public async Task<bool> HandleAsync(DeleteInvestmentLotCommand command, CancellationToken ct = default)
    {
        var lot = await db.InvestmentLots.FindAsync([command.Id], ct);
        if (lot is null) return false;

        db.InvestmentLots.Remove(lot);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
