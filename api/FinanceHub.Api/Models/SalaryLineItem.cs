namespace FinanceHub.Api.Models;

public class SalaryLineItem
{
    public int Id { get; set; }
    public int SalarySlipId { get; set; }
    public SalarySlip SalarySlip { get; set; } = null!;
    public int SalaryItemCategoryId { get; set; }
    public SalaryItemCategory SalaryItemCategory { get; set; } = null!;
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitValue { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? IncidenciaBase { get; set; }
}
