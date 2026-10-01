namespace Beacon.Api.Features.Transactions.Queries.GetTransactions;

public record GetTransactionsQuery(
    string? Bank,
    string? Month,
    string? Type,
    string? CategoryFilter,
    string? Search,
    int Skip = 0,
    int Take = 20,
    string? SortBy = null,
    string? SortDir = null);
