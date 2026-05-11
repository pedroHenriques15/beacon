namespace FinanceHub.Api.Features.Groceries.Commands.UpdateGroceryItem;

public record UpdateGroceryItemResponse(
    int Id,
    int ReceiptId,
    string Description,
    decimal Amount,
    decimal Quantity,
    int? CategoryId);
