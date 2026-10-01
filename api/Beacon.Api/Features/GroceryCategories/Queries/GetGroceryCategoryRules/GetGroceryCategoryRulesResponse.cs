namespace Beacon.Api.Features.GroceryCategories.Queries.GetGroceryCategoryRules;

public record GetGroceryCategoryRulesResponse(int Id, int CategoryId, string? Pattern, string CategoryName, string CategoryColor, decimal? Value);
