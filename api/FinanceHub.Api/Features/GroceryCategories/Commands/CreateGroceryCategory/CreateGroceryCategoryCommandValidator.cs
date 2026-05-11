using FinanceHub.Api.Validation;

namespace FinanceHub.Api.Features.GroceryCategories.Commands.CreateGroceryCategory;

public class CreateGroceryCategoryCommandValidator
{
    public ValidationResult Validate(CreateGroceryCategoryCommand cmd)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(cmd.Name))
            errors.Add("Name is required.");

        if (cmd.Value is not null && cmd.Value < 0)
            errors.Add("Value must be non-negative.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
