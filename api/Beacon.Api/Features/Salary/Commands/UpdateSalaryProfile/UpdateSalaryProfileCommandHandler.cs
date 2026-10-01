using Beacon.Api.Data;
using Beacon.Api.Features.Salary.Queries.GetSalaryProfiles;

namespace Beacon.Api.Features.Salary.Commands.UpdateSalaryProfile;

public record UpdateSalaryProfileCommand(int Id, string Name, string? Description, string? HourlyRateFormula = null);

public class UpdateSalaryProfileCommandHandler(AppDbContext db)
{
    public async Task<SalaryProfileResponse?> HandleAsync(UpdateSalaryProfileCommand command, CancellationToken ct = default)
    {
        var profile = await db.SalaryProfiles.FindAsync([command.Id], ct);
        if (profile is null) return null;
        profile.Name = command.Name.Trim();
        profile.Description = command.Description?.Trim();
        if (command.HourlyRateFormula is not null)
            profile.HourlyRateFormula = CreateSalaryProfile.CreateSalaryProfileCommandHandler.NormalizeFormula(command.HourlyRateFormula);
        await db.SaveChangesAsync(ct);
        var slipCount = db.SalarySlips.Count(s => s.SalaryProfileId == profile.Id);
        return new SalaryProfileResponse(profile.Id, profile.Name, profile.Description, slipCount, profile.HourlyRateFormula);
    }
}
