using Beacon.Api.Data;
using Beacon.Api.Features.Shared;

namespace Beacon.Api.Features.Categories.Commands.DeleteCategory;

public class DeleteCategoryCommandHandler(AppDbContext db, ILogger<DeleteCategoryCommandHandler> logger)
{
    public async Task<(bool Found, bool IsProtected)> HandleAsync(DeleteCategoryCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("DeleteCategory: id={Id}", cmd.Id);
        return await ProtectedEntityHelper.DeleteIfAllowedAsync(db, db.Categories, cmd.Id, ct);
    }
}
