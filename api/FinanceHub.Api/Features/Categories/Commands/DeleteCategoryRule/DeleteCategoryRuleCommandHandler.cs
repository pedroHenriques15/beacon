using FinanceHub.Api.Data;

namespace FinanceHub.Api.Features.Categories.Commands.DeleteCategoryRule;

public class DeleteCategoryRuleCommandHandler(AppDbContext db, ILogger<DeleteCategoryRuleCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteCategoryRuleCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("DeleteCategoryRule: id={Id}", cmd.Id);
        var rule = await db.CategoryRules.FindAsync([cmd.Id], ct);
        if (rule is null) return false;
        db.CategoryRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
