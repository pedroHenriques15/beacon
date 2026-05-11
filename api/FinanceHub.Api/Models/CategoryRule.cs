using System.Text.Json.Serialization;

namespace FinanceHub.Api.Models;

public class CategoryRule
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public decimal? Value { get; set; }

    [JsonIgnore]
    public Category Category { get; set; } = null!;
}
