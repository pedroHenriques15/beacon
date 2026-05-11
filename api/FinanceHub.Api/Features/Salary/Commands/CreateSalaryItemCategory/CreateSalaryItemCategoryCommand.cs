namespace FinanceHub.Api.Features.Salary.Commands.CreateSalaryItemCategory;

public record CreateSalaryItemCategoryCommand(int SalaryProfileId, string Name, string Color, string ItemType);
