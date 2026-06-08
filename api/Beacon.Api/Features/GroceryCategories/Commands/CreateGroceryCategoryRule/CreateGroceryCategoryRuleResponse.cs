namespace Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryCategoryRule;

public record CreateGroceryCategoryRuleResponse(int Id, int CategoryId, string? Pattern, decimal? Value);
