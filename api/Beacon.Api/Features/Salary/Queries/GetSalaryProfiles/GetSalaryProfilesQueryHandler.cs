using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Salary.Queries.GetSalaryProfiles;

public record SalaryProfileResponse(int Id, string Name, string? Description, int SlipCount, string HourlyRateFormula);

public class GetSalaryProfilesQueryHandler(AppDbContext db)
{
    public async Task<List<SalaryProfileResponse>> HandleAsync(CancellationToken ct = default) =>
        await db.SalaryProfiles
            .OrderBy(p => p.Name)
            .Select(p => new SalaryProfileResponse(
                p.Id, p.Name, p.Description,
                p.SalarySlips.Count, p.HourlyRateFormula))
            .ToListAsync(ct);
}
