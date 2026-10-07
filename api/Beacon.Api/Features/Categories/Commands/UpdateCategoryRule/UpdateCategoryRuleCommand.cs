namespace Beacon.Api.Features.Categories.Commands.UpdateCategoryRule;

public record UpdateCategoryRuleCommand(int Id, string? Pattern, decimal? Value, bool MatchWholeDescription = false);
