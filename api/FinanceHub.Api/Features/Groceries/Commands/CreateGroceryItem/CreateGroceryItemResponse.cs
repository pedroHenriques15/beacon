namespace FinanceHub.Api.Features.Groceries.Commands.CreateGroceryItem;

public record CreateGroceryItemResponse(
    int Id,
    int ReceiptId,
    string Description,
    decimal Amount,
    decimal Quantity,
    int? CategoryId);
