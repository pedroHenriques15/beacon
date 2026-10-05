namespace Beacon.Api.Features.Investments.Commands.SyncPriceHistory;

/// <summary>Syncs one asset's prices, or, with no asset, every asset still held.</summary>
public record SyncPriceHistoryCommand(int? AssetId = null);
