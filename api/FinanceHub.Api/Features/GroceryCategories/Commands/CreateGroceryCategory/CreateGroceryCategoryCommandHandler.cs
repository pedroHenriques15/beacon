using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Groceries.Shared;
using FinanceHub.Api.Features.Shared;
using FinanceHub.Api.Models;

namespace FinanceHub.Api.Features.GroceryCategories.Commands.CreateGroceryCategory;

public class CreateGroceryCategoryCommandHandler(AppDbContext db, GroceryApplyRuleService applyRule, ILogger<CreateGroceryCategoryCommandHandler> logger)
{
    public async Task<CreateGroceryCategoryResponse> HandleAsync(CreateGroceryCategoryCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("CreateGroceryCategory: name={Name}", cmd.Name);

        new CreateGroceryCategoryCommandValidator().Validate(cmd).ThrowIfInvalid();
        var category = new GroceryCategory { Name = cmd.Name.Trim(), Color = cmd.Color ?? "#a855f7" };
        db.GroceryCategories.Add(category);

        GroceryCategoryRule? rule = null;
        if (!string.IsNullOrWhiteSpace(cmd.Pattern))
        {
            rule = new GroceryCategoryRule { Pattern = cmd.Pattern.Trim(), Value = cmd.Value };
            category.Rules.Add(rule);
        }

        await db.SaveChangesAsync(ct);

        if (rule is not null)
            await applyRule.ApplyAsync(rule);

        return new CreateGroceryCategoryResponse(category.Id, category.Name, category.Color);
    }
}
