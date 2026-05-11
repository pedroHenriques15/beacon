namespace FinanceHub.Api.Features.Groceries.Commands.UpdateGroceryItem;

public record UpdateGroceryItemCommand(
    int Id,
    string? Description,
    decimal? Amount,
    decimal? Quantity);
