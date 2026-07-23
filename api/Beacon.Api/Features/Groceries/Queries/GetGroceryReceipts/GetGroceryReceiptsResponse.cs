namespace Beacon.Api.Features.Groceries.Queries.GetGroceryReceipts;

public record GroceryReceiptSummary(
    int Id,
    string StoreName,
    DateOnly ReceiptDate,
    decimal Total,
    int ItemCount,
    string? SourceFile);
