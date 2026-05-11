using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Salary.Queries.GetSalaryItemCategories;
using FinanceHub.Api.Models;

namespace FinanceHub.Api.Features.Salary.Commands.CreateSalaryItemCategory;

public class CreateSalaryItemCategoryCommandHandler(AppDbContext db)
{
    public async Task<SalaryItemCategoryResponse> HandleAsync(CreateSalaryItemCategoryCommand command, CancellationToken ct = default)
    {
        var cat = new SalaryItemCategory
        {
            SalaryProfileId = command.SalaryProfileId,
            Name     = command.Name.Trim(),
            Color    = command.Color,
            ItemType = command.ItemType
        };
        db.SalaryItemCategories.Add(cat);
        await db.SaveChangesAsync(ct);
        return new SalaryItemCategoryResponse(cat.Id, cat.SalaryProfileId, cat.Name, cat.Color, cat.ItemType, cat.IsProtected);
    }
}
