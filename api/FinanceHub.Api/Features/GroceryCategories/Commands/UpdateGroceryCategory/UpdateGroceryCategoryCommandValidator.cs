using FinanceHub.Api.Validation;

namespace FinanceHub.Api.Features.GroceryCategories.Commands.UpdateGroceryCategory;

public class UpdateGroceryCategoryCommandValidator
{
    public ValidationResult Validate(UpdateGroceryCategoryCommand cmd)
    {
        var errors = new List<string>();

        if (cmd.Id <= 0)
            errors.Add("Id must be a positive integer.");

        if (string.IsNullOrWhiteSpace(cmd.Name) && string.IsNullOrWhiteSpace(cmd.Color))
            errors.Add("At least one of Name or Color must be provided.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
