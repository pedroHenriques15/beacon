using FinanceHub.Api.Validation;

namespace FinanceHub.Api.Features.Groceries.Commands.CreateGroceryItem;

public class CreateGroceryItemCommandValidator
{
    public ValidationResult Validate(CreateGroceryItemCommand cmd)
    {
        var errors = new List<string>();

        if (cmd.ReceiptId <= 0)
            errors.Add("ReceiptId must be a positive integer.");

        if (string.IsNullOrWhiteSpace(cmd.Description))
            errors.Add("Description is required.");

        if (cmd.Amount <= 0)
            errors.Add("Amount must be positive.");

        if (cmd.Quantity <= 0)
            errors.Add("Quantity must be positive.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
