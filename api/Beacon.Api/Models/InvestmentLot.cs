namespace Beacon.Api.Models;

public class InvestmentLot
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public InvestmentAsset Asset { get; set; } = null!;
    public DateOnly Date { get; set; }
    public decimal Quantity { get; set; }     // positive = buy, negative = sell
    public decimal PricePerUnit { get; set; } // per share or per gram
    public decimal? Fees { get; set; }
    public string? Notes { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
