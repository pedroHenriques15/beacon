using Beacon.Api.Data;

namespace Beacon.Api.Features.Categories.Commands.UpdateCategory;

public class UpdateCategoryCommandHandler(AppDbContext db, ILogger<UpdateCategoryCommandHandler> logger)
{
    public async Task<UpdateCategoryResponse?> HandleAsync(UpdateCategoryCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("UpdateCategory: id={Id}", cmd.Id);
        var category = await db.Categories.FindAsync([cmd.Id], ct);
        if (category is null) return null;

        if (!string.IsNullOrWhiteSpace(cmd.Name))  category.Name  = cmd.Name.Trim();
        if (!string.IsNullOrWhiteSpace(cmd.Color)) category.Color = cmd.Color;

        await db.SaveChangesAsync(ct);
        return new UpdateCategoryResponse(category.Id, category.Name, category.Color);
    }
}
