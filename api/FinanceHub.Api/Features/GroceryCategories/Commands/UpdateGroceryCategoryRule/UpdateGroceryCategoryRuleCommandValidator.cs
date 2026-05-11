using FinanceHub.Api.Validation;

namespace FinanceHub.Api.Features.GroceryCategories.Commands.UpdateGroceryCategoryRule;

public class UpdateGroceryCategoryRuleCommandValidator
{
    public ValidationResult Validate(UpdateGroceryCategoryRuleCommand cmd)
    {
        var errors = new List<string>();

        if (cmd.Id <= 0)
            errors.Add("Id must be a positive integer.");

        if (string.IsNullOrWhiteSpace(cmd.Pattern) && cmd.Value is null)
            errors.Add("At least one of Pattern or Value is required.");

        if (cmd.Value is not null && cmd.Value < 0)
            errors.Add("Value must be non-negative.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
