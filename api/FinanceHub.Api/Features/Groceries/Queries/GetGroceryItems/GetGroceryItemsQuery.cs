namespace FinanceHub.Api.Features.Groceries.Queries.GetGroceryItems;

public record GetGroceryItemsQuery(
    int? ReceiptId,
    string? Store,
    string? Month,
    int? CategoryId,
    string? Search,
    string? SortCol,
    string? SortDir,
    int Skip = 0,
    int Take = 20);
