namespace Beacon.Api.Models;

public class InvestmentPriceSnapshot
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public InvestmentAsset Asset { get; set; } = null!;
    public DateOnly Date { get; set; }
    public decimal PricePerUnit { get; set; } // per share or per gram
    public string Source { get; set; } = PriceSources.Manual;
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Where a price snapshot came from. A sync replaces Synced and Legacy rows, never Manual ones.</summary>
public static class PriceSources
{
    /// <summary>Entered by hand.</summary>
    public const string Manual = "Manual";

    /// <summary>A daily close written by the price sync.</summary>
    public const string Synced = "Synced";

    /// <summary>Stored before sources were recorded: fetched intraday quotes and hand-entered prices alike.</summary>
    public const string Legacy = "Legacy";
}
