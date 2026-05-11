using FinanceHub.Api.Validation;

namespace FinanceHub.Api.Features.Transactions.Commands.CreateTransaction;

public class CreateTransactionCommandValidator
{
    public ValidationResult Validate(CreateTransactionCommand cmd)
    {
        var errors = new List<string>();

        if (cmd.StatementId <= 0)
            errors.Add("StatementId must be a positive integer.");

        if (string.IsNullOrWhiteSpace(cmd.Description))
            errors.Add("Description is required.");

        if (cmd.Amount < 0)
            errors.Add("Amount must be non-negative.");

        if (cmd.Type != "credit" && cmd.Type != "debit")
            errors.Add("Type must be 'credit' or 'debit'.");

        if (cmd.CategoryId is not null && cmd.CategoryId <= 0)
            errors.Add("CategoryId must be a positive integer.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
