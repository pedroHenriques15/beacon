using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Shared;

namespace FinanceHub.Api.Features.GroceryCategories.Commands.DeleteGroceryCategory;

public class DeleteGroceryCategoryCommandHandler(AppDbContext db, ILogger<DeleteGroceryCategoryCommandHandler> logger)
{
    public async Task<(bool Found, bool IsProtected)> HandleAsync(DeleteGroceryCategoryCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("DeleteGroceryCategory: id={Id}", cmd.Id);
        return await ProtectedEntityHelper.DeleteIfAllowedAsync(db, db.GroceryCategories, cmd.Id, ct);
    }
}
