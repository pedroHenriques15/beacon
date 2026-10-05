using Beacon.Api.Services.Pricing;
using Microsoft.Extensions.Configuration;

namespace Beacon.Tests.Services;

/// <summary>Configuration, clock, queue and price source for tests that touch price syncing.</summary>
internal static class TestPricing
{
    public static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

    public static PriceSyncQueue Queue() => new(Config());

    /// <summary>The asset ids waiting in <paramref name="queue"/>, which it empties.</summary>
    public static List<int> Drain(PriceSyncQueue queue)
    {
        var ids = new List<int>();
        while (queue.Reader.TryRead(out var id)) ids.Add(id);
        return ids;
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Answers from a fixed series per symbol and records what it was asked.</summary>
internal sealed class StubPriceHistorySource : IPriceHistorySource
{
    public Dictionary<string, Dictionary<DateOnly, decimal>> Series { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string?> SymbolsByIsin { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Failures { get; } = new(StringComparer.Ordinal);
    public List<(string Symbol, DateOnly From, DateOnly To)> Requests { get; } = [];
    public List<string> IsinLookups { get; } = [];

    public Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyClosesAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Requests.Add((symbol, from, to));
        if (Failures.TryGetValue(symbol, out var failure))
            throw new PriceSourceException(failure);

        IReadOnlyDictionary<DateOnly, decimal> closes = Series.TryGetValue(symbol, out var series)
            ? series.Where(kv => kv.Key >= from && kv.Key <= to).ToDictionary()
            : new Dictionary<DateOnly, decimal>();
        return Task.FromResult(closes);
    }

    public Task<string?> FindSymbolByIsinAsync(string isin, CancellationToken ct = default)
    {
        IsinLookups.Add(isin);
        return Task.FromResult(SymbolsByIsin.GetValueOrDefault(isin));
    }
}
