using FinanceHub.Api.Data;

namespace FinanceHub.Api.Features.Salary.Commands.DeleteSalarySlip;

public record DeleteSalarySlipCommand(int Id);

public class DeleteSalarySlipCommandHandler(AppDbContext db)
{
    public async Task<bool> HandleAsync(DeleteSalarySlipCommand command, CancellationToken ct = default)
    {
        var slip = await db.SalarySlips.FindAsync([command.Id], ct);
        if (slip is null) return false;
        db.SalarySlips.Remove(slip);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
