namespace FinanceHub.Api.Models;

public class GroceryReceipt
{
    public int Id { get; set; }
    public string StoreName { get; set; } = "";
    public DateOnly ReceiptDate { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public string? SourceFile { get; set; }
    public string? PdfPath { get; set; }
    public string? FileHash { get; set; }
    public DateTime ImportedAt { get; set; }
    public ICollection<GroceryItem> Items { get; set; } = new List<GroceryItem>();
}
