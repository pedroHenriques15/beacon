namespace FinanceHub.Api.Features.Categories.Commands.UpdateCategoryRule;

public record UpdateCategoryRuleCommand(int Id, string? Pattern, decimal? Value);
