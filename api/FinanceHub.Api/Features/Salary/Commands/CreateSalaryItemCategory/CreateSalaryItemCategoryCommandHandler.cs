using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Salary.Queries.GetSalaryItemCategories;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Salary.Commands.CreateSalaryItemCategory;

public class CreateSalaryItemCategoryCommandHandler(AppDbContext db)
{
    public async Task<(SalaryItemCategoryResponse? Result, string? Error)> HandleAsync(CreateSalaryItemCategoryCommand command, CancellationToken ct = default)
    {
        if (!await db.SalaryProfiles.AnyAsync(p => p.Id == command.SalaryProfileId, ct))
            return (null, "Salary profile not found.");

        var cat = new SalaryItemCategory
        {
            SalaryProfileId = command.SalaryProfileId,
            Name     = command.Name.Trim(),
            Color    = command.Color,
            ItemType = command.ItemType
        };
        db.SalaryItemCategories.Add(cat);
        await db.SaveChangesAsync(ct);
        return (new SalaryItemCategoryResponse(cat.Id, cat.SalaryProfileId, cat.Name, cat.Color, cat.ItemType, cat.IsProtected), null);
    }
}
