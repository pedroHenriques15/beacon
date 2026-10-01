using Beacon.Api.Data;

namespace Beacon.Api.Features.Investments.Commands.DeleteInvestmentPriceSnapshot;

public record DeleteInvestmentPriceSnapshotCommand(int Id);

public class DeleteInvestmentPriceSnapshotCommandHandler(AppDbContext db)
{
    public async Task<bool> HandleAsync(DeleteInvestmentPriceSnapshotCommand command, CancellationToken ct = default)
    {
        var snapshot = await db.InvestmentPriceSnapshots.FindAsync([command.Id], ct);
        if (snapshot is null) return false;

        db.InvestmentPriceSnapshots.Remove(snapshot);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
