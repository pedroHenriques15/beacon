using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Salary.Queries.GetSalaryItemCategories;

namespace FinanceHub.Api.Features.Salary.Commands.UpdateSalaryItemCategory;

public record UpdateSalaryItemCategoryCommand(int Id, string Name, string Color, string ItemType);

public class UpdateSalaryItemCategoryCommandHandler(AppDbContext db)
{
    public async Task<SalaryItemCategoryResponse?> HandleAsync(UpdateSalaryItemCategoryCommand command, CancellationToken ct = default)
    {
        var cat = await db.SalaryItemCategories.FindAsync([command.Id], ct);
        if (cat is null) return null;
        cat.Name     = command.Name.Trim();
        cat.Color    = command.Color;
        cat.ItemType = command.ItemType;
        await db.SaveChangesAsync(ct);
        return new SalaryItemCategoryResponse(cat.Id, cat.SalaryProfileId, cat.Name, cat.Color, cat.ItemType, cat.IsProtected);
    }
}
