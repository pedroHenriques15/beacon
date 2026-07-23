namespace Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryCategoryRule;

public record CreateGroceryCategoryRuleCommand(int CategoryId, string? Pattern, decimal? Value);
