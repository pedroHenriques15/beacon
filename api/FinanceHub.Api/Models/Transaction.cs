using System.Text.Json.Serialization;

namespace FinanceHub.Api.Models;

public class Transaction
{
    public int Id { get; set; }
    public int StatementId { get; set; }
    public DateOnly DatePosting { get; set; }
    public DateOnly DateValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public int? CategoryId { get; set; }
    public int? CategoryRuleId { get; set; }
    public bool CategorySetManually { get; set; }
    public bool IsExcluded { get; set; }

    [JsonIgnore]
    public MonthlyStatement Statement { get; set; } = null!;
    public Category? Category { get; set; }
}
