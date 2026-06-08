using Beacon.Api.Validation;

namespace Beacon.Api.Features.Transactions.Commands.UpdateTransaction;

public class UpdateTransactionCommandValidator
{
    public ValidationResult Validate(UpdateTransactionCommand cmd)
    {
        var errors = new List<string>();

        if (cmd.Id <= 0)
            errors.Add("Id must be a positive integer.");

        if (cmd.Description is not null && string.IsNullOrWhiteSpace(cmd.Description))
            errors.Add("Description must not be empty.");

        if (cmd.Amount is not null && cmd.Amount < 0)
            errors.Add("Amount must be non-negative.");

        if (cmd.Type is not null && cmd.Type != "credit" && cmd.Type != "debit")
            errors.Add("Type must be 'credit' or 'debit'.");

        if (cmd.CategoryId is not null && cmd.CategoryId <= 0)
            errors.Add("CategoryId must be a positive integer.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
