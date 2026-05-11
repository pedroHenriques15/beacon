namespace FinanceHub.Api.Models;

public class GroceryCategoryRule
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string? Pattern { get; set; }
    public decimal? Value { get; set; }
    public GroceryCategory Category { get; set; } = null!;
}
