using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Categories.Shared;
using FinanceHub.Api.Features.Shared;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Categories.Commands.CreateCategoryRule;

public class CreateCategoryRuleCommandHandler(AppDbContext db, ApplyRuleService applyRule, ILogger<CreateCategoryRuleCommandHandler> logger)
{
    public async Task<CreateCategoryRuleResponse?> HandleAsync(CreateCategoryRuleCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("CreateCategoryRule: categoryId={CategoryId} pattern={Pattern} value={Value}", cmd.CategoryId, cmd.Pattern, cmd.Value);

        new CreateCategoryRuleCommandValidator().Validate(cmd).ThrowIfInvalid();

        if (!await db.Categories.AnyAsync(c => c.Id == cmd.CategoryId, ct))
            return null;

        var pattern = string.IsNullOrWhiteSpace(cmd.Pattern) ? string.Empty : cmd.Pattern.Trim();
        var rule = new CategoryRule { CategoryId = cmd.CategoryId, Pattern = pattern, Value = cmd.Value };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync(ct);

        await applyRule.ApplyAsync(rule);

        return new CreateCategoryRuleResponse(rule.Id, rule.CategoryId, rule.Pattern, rule.Value);
    }
}
