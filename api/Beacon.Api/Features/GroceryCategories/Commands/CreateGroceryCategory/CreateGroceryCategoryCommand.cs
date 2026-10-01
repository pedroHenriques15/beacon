namespace Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryCategory;

public record CreateGroceryCategoryCommand(string Name, string? Color, string? Pattern, decimal? Value = null);
