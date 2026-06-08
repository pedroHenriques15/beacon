using Beacon.Api.Data;
using Beacon.Api.Features.Groceries.Shared;
using Beacon.Api.Features.Shared;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryCategoryRule;

public class CreateGroceryCategoryRuleCommandHandler(AppDbContext db, GroceryApplyRuleService applyRule, ILogger<CreateGroceryCategoryRuleCommandHandler> logger)
{
    public async Task<CreateGroceryCategoryRuleResponse?> HandleAsync(CreateGroceryCategoryRuleCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("CreateGroceryCategoryRule: categoryId={CategoryId} pattern={Pattern} value={Value}",
            cmd.CategoryId, cmd.Pattern, cmd.Value);

        new CreateGroceryCategoryRuleCommandValidator().Validate(cmd).ThrowIfInvalid();

        if (!await db.GroceryCategories.AnyAsync(c => c.Id == cmd.CategoryId, ct))
            return null;

        var pattern = string.IsNullOrWhiteSpace(cmd.Pattern) ? null : cmd.Pattern.Trim();
        var rule = new GroceryCategoryRule { CategoryId = cmd.CategoryId, Pattern = pattern, Value = cmd.Value };
        db.GroceryCategoryRules.Add(rule);
        await db.SaveChangesAsync(ct);

        await applyRule.ApplyAsync(rule);

        return new CreateGroceryCategoryRuleResponse(rule.Id, rule.CategoryId, rule.Pattern, rule.Value);
    }
}
