namespace Beacon.Api.Features.Investments.Commands.SyncPriceHistory;

public record SyncPriceHistoryResponse(List<AssetPriceSyncResult> Assets);

/// <summary>
/// One asset's sync: closes <see cref="Added"/> on new dates, <see cref="Replaced"/> on dates
/// whose synced or legacy price changed, synced closes <see cref="Removed"/> because the asset's
/// symbol changed and the new one has no close that day, and prices <see cref="Kept"/> because
/// they were entered by hand.
/// <see cref="Error"/> is set when the sync failed, and then no price was changed.
/// </summary>
public record AssetPriceSyncResult(
    int AssetId,
    string Name,
    string? Symbol,
    int Added,
    int Replaced,
    int Removed,
    int Kept,
    DateOnly? LatestClose,
    string? Error);
