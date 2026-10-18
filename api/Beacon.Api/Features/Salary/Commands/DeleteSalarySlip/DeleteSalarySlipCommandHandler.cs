using Beacon.Api.Data;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Salary.Commands.DeleteSalarySlip;

public record DeleteSalarySlipCommand(int Id);

public class DeleteSalarySlipCommandHandler(AppDbContext db, FileStorageService fileStorage, ILogger<DeleteSalarySlipCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteSalarySlipCommand command, CancellationToken ct = default)
    {
        var slip = await db.SalarySlips
            .Include(s => s.Files)
            .FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (slip is null) return false;

        // Its own PDF and every further one (ADR-039).
        var pdfPaths = slip.Files.Select(f => f.PdfPath).ToList();
        if (slip.PdfPath is not null) pdfPaths.Insert(0, slip.PdfPath);

        db.SalarySlips.Remove(slip);
        await db.SaveChangesAsync(ct);

        foreach (var path in pdfPaths)
        {
            try { fileStorage.Delete(path); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete PDF {Path} for salary slip {Id}", path, command.Id); }
        }
        return true;
    }
}
