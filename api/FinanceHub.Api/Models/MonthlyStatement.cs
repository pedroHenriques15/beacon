namespace FinanceHub.Api.Models;

public class MonthlyStatement
{
    public int Id { get; set; }
    public string Bank { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public string Currency { get; set; } = "EUR";
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string? PdfPath { get; set; }
    public string? FileHash { get; set; }
    public decimal? PprBalance { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Transaction> Transactions { get; set; } = [];
}
