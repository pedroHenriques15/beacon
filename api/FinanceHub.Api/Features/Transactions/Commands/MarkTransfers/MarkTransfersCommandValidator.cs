using FinanceHub.Api.Validation;

namespace FinanceHub.Api.Features.Transactions.Commands.MarkTransfers;

public class MarkTransfersCommandValidator
{
    public ValidationResult Validate(MarkTransfersCommand cmd)
    {
        var errors = new List<string>();

        if (cmd.TxIds is null || cmd.TxIds.Length == 0)
            errors.Add("At least one transaction ID is required.");
        else if (cmd.TxIds.Any(id => id <= 0))
            errors.Add("All transaction IDs must be positive integers.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
