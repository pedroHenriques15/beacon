using Beacon.Api.Data;

namespace Beacon.Api.Features.Salary.Commands.DeleteSalaryProfile;

public record DeleteSalaryProfileCommand(int Id);

public class DeleteSalaryProfileCommandHandler(AppDbContext db)
{
    public async Task<bool> HandleAsync(DeleteSalaryProfileCommand command, CancellationToken ct = default)
    {
        var profile = await db.SalaryProfiles.FindAsync([command.Id], ct);
        if (profile is null) return false;
        db.SalaryProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
