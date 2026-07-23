namespace Beacon.Api.Models;

public class InvestmentAsset
{
    public int Id { get; set; }
    public string AssetType { get; set; } = ""; // "ETF" or "Gold"
    public string? Ticker { get; set; }
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public ICollection<InvestmentLot> Lots { get; set; } = [];
    public ICollection<InvestmentPriceSnapshot> PriceSnapshots { get; set; } = [];
}
