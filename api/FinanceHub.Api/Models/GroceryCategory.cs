using FinanceHub.Api.Features.Shared;

namespace FinanceHub.Api.Models;

public class GroceryCategory : IProtectedEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#a855f7";
    public bool IsProtected { get; set; }
    public ICollection<GroceryCategoryRule> Rules { get; set; } = new List<GroceryCategoryRule>();
}
