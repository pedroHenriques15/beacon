namespace Beacon.Api.Features.Transactions.Queries.GetTransactions;

public record PagedTransactionsResponse(
    List<GetTransactionsResponse> Items,
    int TotalCount,
    decimal TotalCredit,
    decimal TotalDebit);
