namespace FinanceHub.Api.Models;

public class SalaryProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ICollection<SalarySlip> SalarySlips { get; set; } = [];
    public ICollection<SalaryItemCategory> SalaryItemCategories { get; set; } = [];
}
