using Beacon.Api.Data;
using Beacon.Api.Features.Shared;

namespace Beacon.Api.Features.Salary.Commands.DeleteSalaryItemCategory;

public record DeleteSalaryItemCategoryCommand(int Id);

public class DeleteSalaryItemCategoryCommandHandler(AppDbContext db)
{
    public async Task<(bool Found, bool IsProtected)> HandleAsync(DeleteSalaryItemCategoryCommand command, CancellationToken ct = default)
    {
        return await ProtectedEntityHelper.DeleteIfAllowedAsync(db, db.SalaryItemCategories, command.Id, ct);
    }
}
