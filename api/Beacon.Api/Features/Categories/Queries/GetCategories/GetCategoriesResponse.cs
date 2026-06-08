namespace Beacon.Api.Features.Categories.Queries.GetCategories;

public record GetCategoriesResponse(int Id, string Name, string Color, bool IsProtected, List<RuleDto> Rules);
public record RuleDto(int Id, int CategoryId, string Pattern);
