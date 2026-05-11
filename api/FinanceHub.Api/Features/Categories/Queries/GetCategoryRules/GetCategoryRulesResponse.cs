namespace FinanceHub.Api.Features.Categories.Queries.GetCategoryRules;

public record GetCategoryRulesResponse(int Id, int CategoryId, string Pattern, string CategoryName, string CategoryColor, decimal? Value);
