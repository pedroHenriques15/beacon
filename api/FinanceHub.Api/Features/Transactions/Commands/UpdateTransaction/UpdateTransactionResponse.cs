namespace FinanceHub.Api.Features.Transactions.Commands.UpdateTransaction;

public record UpdateTransactionResponse(
    int Id, int StatementId, DateOnly DatePosting, DateOnly DateValue,
    string Description, decimal Amount, string Type, decimal Balance,
    int? CategoryId, int? CategoryRuleId, bool CategorySetManually, bool IsExcluded);
