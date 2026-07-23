using Beacon.Api.Data;
using Beacon.Api.Services;

namespace Beacon.Api.Features.Salary.Commands.DeleteSalarySlip;

public record DeleteSalarySlipCommand(int Id);

public class DeleteSalarySlipCommandHandler(AppDbContext db, FileStorageService fileStorage, ILogger<DeleteSalarySlipCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteSalarySlipCommand command, CancellationToken ct = default)
    {
        var slip = await db.SalarySlips.FindAsync([command.Id], ct);
        if (slip is null) return false;

        var pdfPath = slip.PdfPath;
        db.SalarySlips.Remove(slip);
        await db.SaveChangesAsync(ct);

        if (pdfPath is not null)
        {
            try { fileStorage.Delete(pdfPath); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete PDF for salary slip {Id}", command.Id); }
        }
        return true;
    }
}
