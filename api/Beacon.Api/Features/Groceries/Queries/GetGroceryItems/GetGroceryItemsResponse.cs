namespace Beacon.Api.Features.Groceries.Queries.GetGroceryItems;

public record GroceryItemResponse(
    int Id,
    int ReceiptId,
    string StoreName,
    DateOnly ReceiptDate,
    string Description,
    decimal Amount,
    decimal Quantity,
    int? CategoryId,
    string? CategoryName,
    string? CategoryColor,
    bool CategorySetManually);

public record PagedGroceryItemsResult(
    List<GroceryItemResponse> Items,
    int TotalCount,
    decimal TotalAmount);
