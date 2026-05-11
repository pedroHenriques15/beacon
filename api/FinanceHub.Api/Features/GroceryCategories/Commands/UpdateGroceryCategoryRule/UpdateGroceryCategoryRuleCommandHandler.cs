using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.GroceryCategories.Commands.UpdateGroceryCategoryRule;

public class UpdateGroceryCategoryRuleCommandHandler(AppDbContext db, ILogger<UpdateGroceryCategoryRuleCommandHandler> logger)
{
    public async Task<bool> HandleAsync(UpdateGroceryCategoryRuleCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("UpdateGroceryCategoryRule: id={Id} pattern={Pattern} value={Value}", cmd.Id, cmd.Pattern, cmd.Value);

        new UpdateGroceryCategoryRuleCommandValidator().Validate(cmd).ThrowIfInvalid();

        var rule = await db.GroceryCategoryRules.FirstOrDefaultAsync(r => r.Id == cmd.Id, ct);
        if (rule is null) return false;

        rule.Pattern = string.IsNullOrWhiteSpace(cmd.Pattern) ? null : cmd.Pattern.Trim();
        rule.Value   = cmd.Value;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
