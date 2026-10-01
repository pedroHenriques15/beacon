namespace Beacon.Api.Features.GroceryCategories.Commands.UpdateGroceryCategory;

public record UpdateGroceryCategoryCommand(int Id, string? Name, string? Color);
