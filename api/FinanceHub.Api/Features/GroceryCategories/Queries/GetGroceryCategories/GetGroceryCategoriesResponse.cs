namespace FinanceHub.Api.Features.GroceryCategories.Queries.GetGroceryCategories;

public record GetGroceryCategoriesResponse(int Id, string Name, string Color, bool IsProtected, List<GroceryRuleDto> Rules);
public record GroceryRuleDto(int Id, int CategoryId, string? Pattern, decimal? Value);
