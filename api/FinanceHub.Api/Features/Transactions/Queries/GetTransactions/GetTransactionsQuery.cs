namespace FinanceHub.Api.Features.Transactions.Queries.GetTransactions;

public record GetTransactionsQuery(
    string? Bank,
    string? Month,
    string? Type,
    string? CategoryFilter,
    string? Search,
    int Skip = 0,
    int Take = 20,
    bool IncludeTransfers = false,
    string? SortBy = null,
    string? SortDir = null);
