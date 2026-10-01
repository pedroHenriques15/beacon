using Beacon.Api.Data;
using Beacon.Api.Features.Categories.Shared;
using Beacon.Api.Features.Shared;
using Beacon.Api.Models;

namespace Beacon.Api.Features.Categories.Commands.CreateCategory;

public class CreateCategoryCommandHandler(AppDbContext db, ApplyRuleService applyRule, ILogger<CreateCategoryCommandHandler> logger)
{
    public async Task<CreateCategoryResponse> HandleAsync(CreateCategoryCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("CreateCategory: name={Name}", cmd.Name);

        new CreateCategoryCommandValidator().Validate(cmd).ThrowIfInvalid();
        var category = new Category { Name = cmd.Name.Trim(), Color = cmd.Color ?? "#94a3b8" };
        db.Categories.Add(category);

        CategoryRule? rule = null;
        if (!string.IsNullOrWhiteSpace(cmd.Pattern))
        {
            rule = new CategoryRule { Pattern = cmd.Pattern.Trim(), Value = cmd.Value };
            category.Rules.Add(rule);
        }

        await db.SaveChangesAsync(ct);

        if (rule is not null)
            await applyRule.ApplyAsync(rule);

        return new CreateCategoryResponse(category.Id, category.Name, category.Color);
    }
}
