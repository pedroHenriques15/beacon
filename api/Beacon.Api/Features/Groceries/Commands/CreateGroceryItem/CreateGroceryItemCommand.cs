namespace Beacon.Api.Features.Groceries.Commands.CreateGroceryItem;

public record CreateGroceryItemCommand(
    int ReceiptId,
    string Description,
    decimal Amount,
    decimal Quantity);
