namespace FinanceHub.Api.Models;

public class SalarySlip
{
    public int Id { get; set; }
    public int SalaryProfileId { get; set; }
    public SalaryProfile SalaryProfile { get; set; } = null!;
    public DateOnly Period { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string? Notes { get; set; }
    public string? SourceFile { get; set; }
    public string? PdfPath { get; set; }
    public string? FileHash { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public decimal? BaseAmount { get; set; }
    public decimal? HoursWorked { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal? TotalEspecie { get; set; }
    public ICollection<SalaryLineItem> LineItems { get; set; } = [];
}
