using Beacon.Api.Data;
using Beacon.Api.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Salary.Commands.DeleteSalaryItemCategory;

public record DeleteSalaryItemCategoryCommand(int Id);

public class DeleteSalaryItemCategoryCommandHandler(AppDbContext db)
{
    public async Task<(bool Found, bool IsProtected, bool InUse)> HandleAsync(DeleteSalaryItemCategoryCommand command, CancellationToken ct = default)
    {
        var exists = await db.SalaryItemCategories.AnyAsync(c => c.Id == command.Id, ct);
        if (!exists) return (false, false, false);

        if (await db.SalaryLineItems.AnyAsync(li => li.SalaryItemCategoryId == command.Id, ct))
            return (true, false, true);

        try
        {
            var (found, isProtected) = await ProtectedEntityHelper.DeleteIfAllowedAsync(db, db.SalaryItemCategories, command.Id, ct);
            return (found, isProtected, false);
        }
        catch (DbUpdateException)
        {
            return (true, false, true);
        }
    }
}
