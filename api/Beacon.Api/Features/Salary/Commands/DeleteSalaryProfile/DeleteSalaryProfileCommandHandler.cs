using Beacon.Api.Data;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Salary.Commands.DeleteSalaryProfile;

public record DeleteSalaryProfileCommand(int Id);

public class DeleteSalaryProfileCommandHandler(AppDbContext db, FileStorageService fileStorage, ILogger<DeleteSalaryProfileCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteSalaryProfileCommand command, CancellationToken ct = default)
    {
        var profile = await db.SalaryProfiles.FindAsync([command.Id], ct);
        if (profile is null) return false;

        var pdfPaths = await db.SalarySlips
            .Where(s => s.SalaryProfileId == command.Id && s.PdfPath != null)
            .Select(s => s.PdfPath!)
            .ToListAsync(ct);

        db.SalaryProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);

        foreach (var path in pdfPaths)
        {
            try { fileStorage.Delete(path); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete PDF {Path} for profile {Id}", path, command.Id); }
        }
        return true;
    }
}
