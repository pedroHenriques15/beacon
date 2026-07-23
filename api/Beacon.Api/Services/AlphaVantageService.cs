using System.Text.Json;

namespace Beacon.Api.Services;

public class AlphaVantageService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private const string BaseUrl = "https://www.alphavantage.co/query";
    // Troy ounce to grams conversion constant
    private const decimal TroyOzToGrams = 31.1035m;

    private string ApiKey => configuration["AlphaVantage:ApiKey"]
        ?? throw new InvalidOperationException("AlphaVantage:ApiKey is not configured.");

    /// <summary>Fetches latest price per share for an ETF or stock ticker.</summary>
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

    /// <summary>Fetches latest gold price per gram in EUR via XAU/EUR exchange rate.</summary>
    public async Task<decimal> FetchGoldPricePerGramAsync(CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("alpha-vantage");
        var url = $"{BaseUrl}?function=CURRENCY_EXCHANGE_RATE&from_currency=XAU&to_currency=EUR&apikey={ApiKey}";
        var response = await client.GetStringAsync(url, ct);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        if (!root.TryGetProperty("Realtime Currency Exchange Rate", out var rate))
        {
            ThrowIfLimited(root);
            throw new InvalidOperationException("Unexpected Alpha Vantage response for XAU/EUR.");
        }

        if (!rate.TryGetProperty("5. Exchange Rate", out var rateEl) || rateEl.GetString() is not string rateStr)
            throw new InvalidOperationException("No exchange rate data found for XAU/EUR.");

        if (!decimal.TryParse(rateStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pricePerTroyOz) || pricePerTroyOz <= 0)
            throw new InvalidOperationException("Invalid exchange rate value returned for XAU/EUR.");

        return Math.Round(pricePerTroyOz / TroyOzToGrams, 4);
    }

    private static void ThrowIfLimited(JsonElement root)
    {
        if (root.TryGetProperty("Note", out _))
            throw new InvalidOperationException("Alpha Vantage daily rate limit reached. Try again later.");

        if (root.TryGetProperty("Information", out var info))
            throw new InvalidOperationException(
                info.GetString()?.Contains("apikey", StringComparison.OrdinalIgnoreCase) == true
                    ? "Alpha Vantage API key is invalid or not activated."
                    : "Alpha Vantage rate limit or quota exceeded. Try again later.");
    }
}
