namespace FinanceHub.Api.Models;

public class GroceryItem
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? ReceiptCategory { get; set; }
    public int? CategoryId { get; set; }
    public int? CategoryRuleId { get; set; }
    public bool CategorySetManually { get; set; }

    public GroceryReceipt Receipt { get; set; } = null!;
    public GroceryCategory? Category { get; set; }
}
