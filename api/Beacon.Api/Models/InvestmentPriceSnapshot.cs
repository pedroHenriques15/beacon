namespace Beacon.Api.Models;

public class InvestmentPriceSnapshot
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public InvestmentAsset Asset { get; set; } = null!;
    public DateOnly Date { get; set; }
    public decimal PricePerUnit { get; set; } // per share or per gram
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
