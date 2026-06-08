using Beacon.Api.Data;
using Beacon.Api.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Categories.Commands.UpdateCategoryRule;

public class UpdateCategoryRuleCommandHandler(AppDbContext db, ILogger<UpdateCategoryRuleCommandHandler> logger)
{
    public async Task<bool> HandleAsync(UpdateCategoryRuleCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("UpdateCategoryRule: id={Id} pattern={Pattern} value={Value}", cmd.Id, cmd.Pattern, cmd.Value);

        new UpdateCategoryRuleCommandValidator().Validate(cmd).ThrowIfInvalid();

        var rule = await db.CategoryRules.FirstOrDefaultAsync(r => r.Id == cmd.Id, ct);
        if (rule is null) return false;

        rule.Pattern = string.IsNullOrWhiteSpace(cmd.Pattern) ? string.Empty : cmd.Pattern.Trim();
        rule.Value = cmd.Value;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
