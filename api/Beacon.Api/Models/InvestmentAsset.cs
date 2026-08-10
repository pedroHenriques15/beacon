namespace Beacon.Api.Models;

public class InvestmentAsset
{
    public int Id { get; set; }
    public string AssetType { get; set; } = ""; // "ETF" or "Gold"
    public string? Ticker { get; set; }
    public string? Isin { get; set; }           // ISIN used to match auto-imported holdings (e.g. Trade Republic savings plans)
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public ICollection<InvestmentLot> Lots { get; set; } = [];
    public ICollection<InvestmentPriceSnapshot> PriceSnapshots { get; set; } = [];
}
