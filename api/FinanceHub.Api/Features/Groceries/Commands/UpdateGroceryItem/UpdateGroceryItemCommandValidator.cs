using FinanceHub.Api.Validation;

namespace FinanceHub.Api.Features.Groceries.Commands.UpdateGroceryItem;

public class UpdateGroceryItemCommandValidator
{
    public ValidationResult Validate(UpdateGroceryItemCommand cmd)
    {
        var errors = new List<string>();

        if (cmd.Id <= 0)
            errors.Add("Id must be a positive integer.");

        if (string.IsNullOrWhiteSpace(cmd.Description) && cmd.Amount is null && cmd.Quantity is null)
            errors.Add("At least one of Description, Amount, or Quantity must be provided.");

        if (cmd.Amount is not null && cmd.Amount <= 0)
            errors.Add("Amount must be positive.");

        if (cmd.Quantity is not null && cmd.Quantity <= 0)
            errors.Add("Quantity must be positive.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
