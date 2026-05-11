using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Shared;

namespace FinanceHub.Api.Features.GroceryCategories.Commands.UpdateGroceryCategory;

public class UpdateGroceryCategoryCommandHandler(AppDbContext db, ILogger<UpdateGroceryCategoryCommandHandler> logger)
{
    public async Task<UpdateGroceryCategoryResponse?> HandleAsync(UpdateGroceryCategoryCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("UpdateGroceryCategory: id={Id}", cmd.Id);

        new UpdateGroceryCategoryCommandValidator().Validate(cmd).ThrowIfInvalid();

        var category = await db.GroceryCategories.FindAsync([cmd.Id], ct);
        if (category is null) return null;

        if (!string.IsNullOrWhiteSpace(cmd.Name))  category.Name  = cmd.Name.Trim();
        if (!string.IsNullOrWhiteSpace(cmd.Color)) category.Color = cmd.Color;

        await db.SaveChangesAsync(ct);
        return new UpdateGroceryCategoryResponse(category.Id, category.Name, category.Color);
    }
}
