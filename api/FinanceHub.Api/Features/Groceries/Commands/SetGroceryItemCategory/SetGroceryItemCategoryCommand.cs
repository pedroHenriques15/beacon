namespace FinanceHub.Api.Features.Groceries.Commands.SetGroceryItemCategory;

public record SetGroceryItemCategoryCommand(int ItemId, int? CategoryId, int? DeleteRuleId);
