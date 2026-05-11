namespace FinanceHub.Api.Features.Transactions.Queries.GetTransactions;

public record GetTransactionsResponse(
    int Id, int StatementId, string Bank,
    DateOnly DatePosting, DateOnly DateValue,
    string Description, decimal Amount, string Type, decimal Balance,
    int? CategoryId, int? CategoryRuleId, bool CategorySetManually, bool IsInternalTransfer,
    CategoryDto? Category);

public record CategoryDto(int Id, string Name, string Color);
