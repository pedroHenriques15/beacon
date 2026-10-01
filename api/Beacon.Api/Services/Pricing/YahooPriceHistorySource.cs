using System.Net;
using System.Text;
using System.Text.Json;

namespace Beacon.Api.Services.Pricing;

/// <summary>
/// Closes from Yahoo Finance's chart endpoint (unofficial: no key and the whole history in one
/// request, but it answers 429 to a client without a browser-like User-Agent). Its closes are
/// adjusted for splits but not for distributions. Symbols are
/// Yahoo's: Xetra listings end in ".DE". An ISIN is mapped through OpenFIGI to its German
/// composite ticker, which is kept only if Yahoo prices it in EUR.
/// </summary>
public class YahooPriceHistorySource(IHttpClientFactory httpClientFactory, ILogger<YahooPriceHistorySource> logger)
    : IPriceHistorySource
{
    public const string YahooClient = "yahoo-finance";
    public const string OpenFigiClient = "openfigi";

    private const string ChartUrl = "https://query1.finance.yahoo.com/v8/finance/chart/";
    private const string OpenFigiUrl = "https://api.openfigi.com/v3/mapping";

    // OpenFIGI's exchange code for the German composite listing, which Yahoo lists as "{ticker}.DE".
    private const string GermanComposite = "GR";

    /// <summary>The waits between attempts: a failed request is retried at most this many times.</summary>
    internal TimeSpan[] RetryDelays { get; init; } =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    public async Task<IReadOnlyDictionary<DateOnly, decimal>> GetDailyClosesAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // period2 is exclusive: midnight UTC of the day after 'to'.
        var period1 = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var period2 = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var url = $"{ChartUrl}{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval=1d";

        var body = await SendAsync(YahooClient, () => new HttpRequestMessage(HttpMethod.Get, url), symbol, ct);
        return ParseChart(body, symbol, from, to);
    }

    public async Task<string?> FindSymbolByIsinAsync(string isin, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new[]
        {
            new { idType = "ID_ISIN", idValue = isin, exchCode = GermanComposite },
        });
        var body = await SendAsync(OpenFigiClient, () => new HttpRequestMessage(HttpMethod.Post, OpenFigiUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        }, isin, ct);

        var ticker = ParseOpenFigiTicker(body);
        if (ticker is null)
        {
            logger.LogInformation("No German listing found for ISIN {Isin}", isin);
            return null;
        }

        var symbol = $"{ticker}.DE";
        try
        {
            // A short range is enough to learn the listing's currency.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await GetDailyClosesAsync(symbol, today.AddDays(-10), today, ct);
            return symbol;
        }
        catch (PriceSourceException ex) when (!ex.Unavailable)
        {
            // Yahoo answered and refused the symbol. Had it not answered, the listing may be fine.
            logger.LogInformation("ISIN {Isin} maps to {Symbol}, which can't be used: {Error}", isin, symbol, ex.Message);
            return null;
        }
    }

    internal static IReadOnlyDictionary<DateOnly, decimal> ParseChart(string body, string symbol, DateOnly from, DateOnly to)
    {
        using var doc = ParseJson(body, symbol);
        if (!doc.RootElement.TryGetProperty("chart", out var chart))
            throw new PriceSourceException($"Unexpected price response for {symbol}.");

        if (chart.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            throw new PriceSourceException(ChartErrorMessage(error, symbol));

        if (!chart.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
            throw new PriceSourceException($"No prices found for {symbol}. Check the ticker.");

        var result = results[0];
        var meta = result.GetProperty("meta");
        var currency = meta.TryGetProperty("currency", out var c) ? c.GetString() : null;
        if (!string.Equals(currency, "EUR", StringComparison.Ordinal))
            throw new PriceSourceException(
                $"{symbol} is priced in {currency ?? "an unknown currency"}, not EUR. " +
                "Use its EUR listing (on Xetra, the ticker ending in .DE).");

        var closes = new Dictionary<DateOnly, decimal>();
        if (!result.TryGetProperty("timestamp", out var timestamps))
            return closes; // a range without trading days

        var closeValues = result.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close");
        var zone = ExchangeZone(meta);

        for (var i = 0; i < timestamps.GetArrayLength() && i < closeValues.GetArrayLength(); i++)
        {
            if (closeValues[i].ValueKind != JsonValueKind.Number) continue; // a day without a close
            var close = Math.Round(closeValues[i].GetDecimal(), 4, MidpointRounding.AwayFromZero);
            if (close <= 0) continue;

            // Each bar is stamped with the start of its session: the exchange's date is the trading day.
            var instant = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64());
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
            if (date < from || date > to) continue;

            closes[date] = close;
        }

        return closes;
    }

    internal static string? ParseOpenFigiTicker(string body)
    {
        using var doc = ParseJson(body, "the ISIN lookup");
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return null;

        // A job without "data" carries {"warning": "No identifier found."}.
        if (!doc.RootElement[0].TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var listing in data.EnumerateArray())
        {
            if (listing.TryGetProperty("exchCode", out var exchange) && exchange.GetString() == GermanComposite
                && listing.TryGetProperty("ticker", out var ticker) && ticker.GetString() is { Length: > 0 } value)
                return value.Trim().ToUpperInvariant();
        }

        return null;
    }

    private async Task<string> SendAsync(
        string clientName, Func<HttpRequestMessage> createRequest, string subject, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(clientName);

        for (var attempt = 0; ; attempt++)
        {
            string failure;
            try
            {
                using var request = createRequest();
                using var response = await client.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode) return body;

                var status = (int)response.StatusCode;
                if (response.StatusCode != HttpStatusCode.TooManyRequests && status < 500)
                    throw new PriceSourceException(ClientErrorMessage(body, subject, status));

                failure = $"HTTP {status}";
            }
            catch (HttpRequestException ex)
            {
                failure = ex.Message;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = "the request timed out";
            }

            if (attempt >= RetryDelays.Length)
                throw new PriceSourceException(
                    $"The price source did not answer for {subject} ({failure}). Try again later.", unavailable: true);

            logger.LogInformation("Price request for {Subject} failed ({Failure}); retrying in {Delay}",
                subject, failure, RetryDelays[attempt]);
            await Task.Delay(RetryDelays[attempt], ct);
        }
    }

    private static string ClientErrorMessage(string body, string subject, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("chart", out var chart)
                && chart.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                return ChartErrorMessage(error, subject);
        }
        catch (JsonException)
        {
            // Not JSON: the status code is all there is.
        }

        return status == 404
            ? $"No prices found for {subject}. Check the ticker."
            : $"The price source refused the request for {subject} (HTTP {status}).";
    }

    private static string ChartErrorMessage(JsonElement error, string symbol) =>
        error.TryGetProperty("description", out var d) && d.GetString() is { Length: > 0 } description
            ? $"No prices found for {symbol}: {description}"
            : $"No prices found for {symbol}. Check the ticker.";

    private static JsonDocument ParseJson(string body, string subject)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new PriceSourceException($"Unreadable price response for {subject}.", ex);
        }
    }

    private static TimeZoneInfo ExchangeZone(JsonElement meta)
    {
        if (meta.TryGetProperty("exchangeTimezoneName", out var name) && name.GetString() is { Length: > 0 } id
            && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            return zone;

        var seconds = meta.TryGetProperty("gmtoffset", out var offset) && offset.TryGetInt32(out var s) ? s : 0;
        return TimeZoneInfo.CreateCustomTimeZone("exchange", TimeSpan.FromSeconds(seconds), "exchange", "exchange");
    }
}
