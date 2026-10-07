namespace Beacon.Api.Models;

public class GroceryCategoryRule
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string? Pattern { get; set; }
    public decimal? Value { get; set; }

    /// <summary>True: the text must equal the whole description; false: it may be any part of it.</summary>
    public bool MatchWholeDescription { get; set; }
    public GroceryCategory Category { get; set; } = null!;
}
