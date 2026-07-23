using System.Text.Json;

namespace Beacon.Api.Services;

public class AlphaVantageService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private const string BaseUrl = "https://www.alphavantage.co/query";

    // Alpha Vantage removed precious metals (XAU) from its currency endpoints, so gold is priced
    // via an EUR-listed physical gold ETC. Xetra-Gold (4GLD): 1 unit = 1 gram, so quotes are EUR/gram.
    private const string DefaultGoldProxyTicker = "4GLD.DEX";

    private string ApiKey => configuration["AlphaVantage:ApiKey"]
        ?? throw new InvalidOperationException("AlphaVantage:ApiKey is not configured.");

    private string GoldProxyTicker => configuration["AlphaVantage:GoldProxyTicker"] ?? DefaultGoldProxyTicker;

    public async Task<decimal> FetchEtfPriceAsync(string ticker, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("alpha-vantage");
        var url = $"{BaseUrl}?function=GLOBAL_QUOTE&symbol={Uri.EscapeDataString(ticker)}&apikey={ApiKey}";
        var response = await client.GetStringAsync(url, ct);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        if (!root.TryGetProperty("Global Quote", out var quote))
        {
            ThrowIfLimited(root);
            throw new InvalidOperationException($"Unexpected Alpha Vantage response for ticker '{ticker}'.");
        }

        if (!quote.TryGetProperty("05. price", out var priceEl) || priceEl.GetString() is not string priceStr)
            throw new InvalidOperationException($"No price data found for ticker '{ticker}'. Check that the ticker is correct.");

        if (!decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var price) || price <= 0)
            throw new InvalidOperationException($"Invalid price value returned for ticker '{ticker}'.");

        return price;
    }

    public Task<decimal> FetchGoldPricePerGramAsync(CancellationToken ct = default) =>
        FetchEtfPriceAsync(GoldProxyTicker, ct);

    public async Task<Dictionary<DateOnly, decimal>> FetchDailySeriesAsync(Models.InvestmentAsset asset, CancellationToken ct = default)
    {
        var isEtf = asset.AssetType == "ETF";
        if (isEtf && string.IsNullOrWhiteSpace(asset.Ticker))
            throw new InvalidOperationException("Asset has no ticker configured.");

        var ticker = isEtf ? asset.Ticker! : GoldProxyTicker;
        var client = httpClientFactory.CreateClient("alpha-vantage");
        var url = $"{BaseUrl}?function=TIME_SERIES_DAILY&symbol={Uri.EscapeDataString(ticker)}&outputsize=full&apikey={ApiKey}";
        var response = await client.GetStringAsync(url, ct);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        if (!root.TryGetProperty("Time Series (Daily)", out var series))
        {
            ThrowIfLimited(root);
            throw new InvalidOperationException($"Unexpected Alpha Vantage response for ticker '{ticker}'.");
        }

        var result = new Dictionary<DateOnly, decimal>();
        foreach (var day in series.EnumerateObject())
        {
            if (!DateOnly.TryParseExact(day.Name, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                continue;

            if (!day.Value.TryGetProperty("4. close", out var closeEl) || closeEl.GetString() is not string closeStr)
                continue;

            if (!decimal.TryParse(closeStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var close) || close <= 0)
                continue;

            result[date] = close;
        }

        return result;
    }

    private static void ThrowIfLimited(JsonElement root)
    {
        if (root.TryGetProperty("Note", out _))
            throw new InvalidOperationException("Alpha Vantage daily rate limit reached. Try again later.");

        if (root.TryGetProperty("Error Message", out var error))
            throw new InvalidOperationException(
                error.GetString()?.Contains("apikey", StringComparison.OrdinalIgnoreCase) == true
                    ? "Alpha Vantage API key is invalid or missing. Set AlphaVantage__ApiKey."
                    : $"Alpha Vantage error: {error.GetString()}");

        if (root.TryGetProperty("Information", out var info))
            throw new InvalidOperationException(
                info.GetString()?.Contains("apikey", StringComparison.OrdinalIgnoreCase) == true
                    ? "Alpha Vantage API key is invalid or not activated."
                    : "Alpha Vantage rate limit or quota exceeded. Try again later.");
    }
}
