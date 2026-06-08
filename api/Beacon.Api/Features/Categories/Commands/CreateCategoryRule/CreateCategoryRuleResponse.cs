namespace Beacon.Api.Features.Categories.Commands.CreateCategoryRule;

public record CreateCategoryRuleResponse(int Id, int CategoryId, string Pattern, decimal? Value);
