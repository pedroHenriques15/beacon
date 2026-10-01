using Beacon.Api.Validation;

namespace Beacon.Api.Features.Categories.Commands.CreateCategoryRule;

public class CreateCategoryRuleCommandValidator
{
    public ValidationResult Validate(CreateCategoryRuleCommand cmd)
    {
        var errors = new List<string>();

        if (cmd.CategoryId <= 0)
            errors.Add("CategoryId must be a positive integer.");

        if (string.IsNullOrWhiteSpace(cmd.Pattern) && cmd.Value is null)
            errors.Add("At least one of Pattern or Value is required.");

        if (cmd.Value is not null && cmd.Value < 0)
            errors.Add("Value must be non-negative.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
