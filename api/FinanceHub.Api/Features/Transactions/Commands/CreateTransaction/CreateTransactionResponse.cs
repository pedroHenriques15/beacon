namespace FinanceHub.Api.Features.Transactions.Commands.CreateTransaction;

public record CreateTransactionResponse(
    int Id, int StatementId, DateOnly DatePosting, DateOnly DateValue,
    string Description, decimal Amount, string Type, decimal Balance,
    int? CategoryId, bool CategorySetManually, bool IsExcluded);
