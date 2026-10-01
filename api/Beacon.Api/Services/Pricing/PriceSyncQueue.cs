using System.Threading.Channels;

namespace Beacon.Api.Services.Pricing;

/// <summary>
/// Assets waiting for a price sync outside the daily run: a ticker just set, a buy just recorded,
/// an ETF just created by a savings plan. <see cref="PriceHistorySyncService"/> reads it, so the
/// request that queued an asset never waits for the price source or fails because of it.
/// </summary>
public class PriceSyncQueue(IConfiguration configuration)
{
    private readonly Channel<int> _assetIds = Channel.CreateUnbounded<int>();

    public ChannelReader<int> Reader => _assetIds.Reader;

    /// <summary>Queues the asset, unless price syncing is turned off.</summary>
    public void Enqueue(int assetId)
    {
        if (PriceSyncSettings.Enabled(configuration))
            _assetIds.Writer.TryWrite(assetId);
    }
}
