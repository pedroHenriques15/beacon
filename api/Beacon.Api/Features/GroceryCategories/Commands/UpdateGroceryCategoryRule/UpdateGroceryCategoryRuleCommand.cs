namespace Beacon.Api.Features.GroceryCategories.Commands.UpdateGroceryCategoryRule;

public record UpdateGroceryCategoryRuleCommand(int Id, string? Pattern, decimal? Value, bool MatchWholeDescription = false);
