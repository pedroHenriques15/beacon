using Beacon.Api.Data;
using Beacon.Api.Features.Salary.Queries.GetSalaryProfiles;
using Beacon.Api.Models;

namespace Beacon.Api.Features.Salary.Commands.CreateSalaryProfile;

public record CreateSalaryProfileCommand(string Name, string? Description, string? HourlyRateFormula = null);

public class CreateSalaryProfileCommandHandler(AppDbContext db)
{
    public async Task<SalaryProfileResponse> HandleAsync(CreateSalaryProfileCommand command, CancellationToken ct = default)
    {
        var profile = new SalaryProfile
        {
            Name = command.Name.Trim(),
            Description = command.Description?.Trim(),
            HourlyRateFormula = NormalizeFormula(command.HourlyRateFormula),
        };
        db.SalaryProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return new SalaryProfileResponse(profile.Id, profile.Name, profile.Description, 0, profile.HourlyRateFormula);
    }

    internal static string NormalizeFormula(string? formula) => formula switch
    {
        "hours" or "workdays" or "days" => formula,
        _ => "days",
    };

}
