namespace FinanceHub.Api.Features.Transactions.Commands.SetTransactionCategory;

public record SetTransactionCategoryCommand(int TransactionId, int? CategoryId, int? DeleteRuleId);
