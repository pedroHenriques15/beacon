namespace Beacon.Api.Models;

public class GroceryReceiptCategoryMapping
{
    public int Id { get; set; }
    public string ReceiptCategoryName { get; set; } = "";
    public int GroceryCategoryId { get; set; }
    public GroceryCategory Category { get; set; } = null!;
}
