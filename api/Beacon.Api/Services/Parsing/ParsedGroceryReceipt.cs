namespace Beacon.Api.Services.Parsing;

public record ParsedGroceryReceipt(
    string StoreName,
    DateOnly ReceiptDate,
    decimal Total,
    IReadOnlyList<ParsedGroceryItem> Items
);

public record ParsedGroceryItem(
    string Description,
    decimal Amount,
    decimal Quantity,
    string? ReceiptCategory = null
);
