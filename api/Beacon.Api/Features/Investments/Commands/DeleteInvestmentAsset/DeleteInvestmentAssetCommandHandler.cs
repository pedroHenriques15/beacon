using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Investments.Commands.DeleteInvestmentAsset;

public record DeleteInvestmentAssetCommand(int Id);

public class DeleteInvestmentAssetCommandHandler(AppDbContext db)
{
    public async Task<bool> HandleAsync(DeleteInvestmentAssetCommand command, CancellationToken ct = default)
    {
        var asset = await db.InvestmentAssets.FindAsync([command.Id], ct);
        if (asset is null) return false;

        db.InvestmentAssets.Remove(asset);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
