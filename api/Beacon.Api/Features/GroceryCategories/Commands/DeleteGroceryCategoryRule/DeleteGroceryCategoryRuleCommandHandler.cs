using Beacon.Api.Data;

namespace Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryCategoryRule;

public class DeleteGroceryCategoryRuleCommandHandler(AppDbContext db, ILogger<DeleteGroceryCategoryRuleCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteGroceryCategoryRuleCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("DeleteGroceryCategoryRule: id={Id}", cmd.Id);
        var rule = await db.GroceryCategoryRules.FindAsync([cmd.Id], ct);
        if (rule is null) return false;
        db.GroceryCategoryRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
