namespace Beacon.Api.Features.Categories.Commands.CreateCategoryRule;

public record CreateCategoryRuleCommand(int CategoryId, string? Pattern, decimal? Value);
