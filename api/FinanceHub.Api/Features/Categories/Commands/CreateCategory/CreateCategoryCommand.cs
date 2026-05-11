namespace FinanceHub.Api.Features.Categories.Commands.CreateCategory;

public record CreateCategoryCommand(string Name, string? Color, string? Pattern, decimal? Value = null);
