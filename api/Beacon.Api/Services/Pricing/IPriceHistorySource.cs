namespace Beacon.Api.Services.Pricing;

/// <summary>Daily closing prices in EUR, and the EUR-listed symbol for an ISIN.</summary>
public interface IPriceHistorySource
{
    /// <summary>
    /// The closes of <paramref name="symbol"/> from <paramref name="from"/> to <paramref name="to"/>,
    /// both included, by exchange date. During a trading session today's entry is the live price.
    /// Closes are as the source gives them: Yahoo's are adjusted for splits, not for distributions.
    /// Throws <see cref="PriceSourceException"/> when the source refuses or the prices are not in EUR.
    /// </summary>
    Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyClosesAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// The symbol of the ISIN's EUR listing, or null when there is none. Throws
    /// <see cref="PriceSourceException"/> when a source is unavailable, rather than answer null.
    /// </summary>
    Task<string?> FindSymbolByIsinAsync(string isin, CancellationToken ct = default);
}

/// <summary>
/// A price source failure with a message fit to show the user. <see cref="Unavailable"/> means the
/// source never gave an answer (network errors, timeouts, 429 or 5xx after the retries), so the
/// failure says nothing about the symbol asked for.
/// </summary>
public class PriceSourceException(string message, Exception? inner = null, bool unavailable = false)
    : Exception(message, inner)
{
    public bool Unavailable { get; } = unavailable;
}
