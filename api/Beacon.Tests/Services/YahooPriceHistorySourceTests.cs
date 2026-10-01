using System.Globalization;
using System.Net;
using Beacon.Api.Services.Pricing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beacon.Tests.Services;

/// <summary>Synthetic responses shaped like Yahoo's chart endpoint and OpenFIGI's mapping.</summary>
public class YahooPriceHistorySourceTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    private static YahooPriceHistorySource MakeSource(HttpMessageHandler handler) =>
        new(new FakeHttpClientFactory(handler), NullLogger<YahooPriceHistorySource>.Instance)
        {
            RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
        };

    // Bars are stamped with the session's start: 09:00 in Berlin, 07:00 UTC in summer.
    private static long SessionStart(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(7, 0)), TimeSpan.Zero).ToUnixTimeSeconds();

    private static string Chart(string currency, params (DateOnly Date, decimal? Close)[] bars)
    {
        var timestamps = string.Join(",", bars.Select(b => SessionStart(b.Date)));
        var closes = string.Join(",", bars.Select(b => b.Close?.ToString(CultureInfo.InvariantCulture) ?? "null"));
        return $$$"""
            {"chart": {"result": [{
                "meta": {"currency": "{{{currency}}}", "symbol": "VWCE.DE", "exchangeName": "GER",
                         "exchangeTimezoneName": "Europe/Berlin", "gmtoffset": 7200},
                "timestamp": [{{{timestamps}}}],
                "indicators": {"quote": [{"close": [{{{closes}}}]}]}
            }], "error": null}}
            """;
    }

    private const string NotFoundChart =
        """{"chart": {"result": null, "error": {"code": "Not Found", "description": "No data found, symbol may be delisted"}}}""";

    [Fact]
    public async Task Closes_AreReadByExchangeDate_SkippingDaysWithoutAClose()
    {
        var body = Chart("EUR", (new DateOnly(2026, 9, 28), 169.38000488m), (new DateOnly(2026, 9, 29), null),
            (new DateOnly(2026, 9, 30), 170.1m));

        var closes = await MakeSource(new FakeHttpMessageHandler(HttpStatusCode.OK, body))
            .GetDailyClosesAsync("VWCE.DE", From, To);

        Assert.Equal(2, closes.Count);
        Assert.Equal(169.38m, closes[new DateOnly(2026, 9, 28)]);
        Assert.Equal(170.1m, closes[new DateOnly(2026, 9, 30)]);
    }

    [Fact]
    public void Closes_UseTheExchangeDate_NotTheUtcDate()
    {
        // 23:30 UTC on the 29th is already the 30th in Berlin (UTC+2).
        var late = new DateTimeOffset(2026, 9, 29, 23, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var body = $$$"""
            {"chart": {"result": [{"meta": {"currency": "EUR", "exchangeTimezoneName": "Europe/Berlin"},
              "timestamp": [{{{late}}}], "indicators": {"quote": [{"close": [100.5]}]}}]}}
            """;

        var closes = YahooPriceHistorySource.ParseChart(body, "VWCE.DE", From, To);

        Assert.Equal(new DateOnly(2026, 9, 30), Assert.Single(closes).Key);
    }

    [Fact]
    public async Task Request_AsksForTheWholeRange()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK, Chart("EUR"));

        await MakeSource(handler).GetDailyClosesAsync("VWCE.DE", From, To);

        var uri = handler.Requests.Single();
        Assert.Equal("/v8/finance/chart/VWCE.DE", uri.AbsolutePath);
        Assert.Contains($"period1={new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()}", uri.Query);
        Assert.Contains($"period2={new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()}", uri.Query);
        Assert.Contains("interval=1d", uri.Query);
    }

    [Fact]
    public async Task NonEurListing_IsRejected()
    {
        var body = Chart("USD", (new DateOnly(2026, 9, 28), 190m));

        var ex = await Assert.ThrowsAsync<PriceSourceException>(() =>
            MakeSource(new FakeHttpMessageHandler(HttpStatusCode.OK, body)).GetDailyClosesAsync("VWRA.L", From, To));

        Assert.Contains("priced in USD, not EUR", ex.Message);
    }

    [Fact]
    public async Task UnknownSymbol_SurfacesTheSourceMessage_WithoutRetrying()
    {
        var handler = new CountingHttpMessageHandler((HttpStatusCode.NotFound, NotFoundChart), (HttpStatusCode.OK, Chart("EUR")));

        var ex = await Assert.ThrowsAsync<PriceSourceException>(() =>
            MakeSource(handler).GetDailyClosesAsync("NOPE.DE", From, To));

        Assert.Contains("symbol may be delisted", ex.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RateLimitAndServerErrors_AreRetried()
    {
        var handler = new CountingHttpMessageHandler(
            (HttpStatusCode.TooManyRequests, "Too Many Requests"),
            (HttpStatusCode.BadGateway, ""),
            (HttpStatusCode.OK, Chart("EUR", (new DateOnly(2026, 9, 28), 100m))));

        var closes = await MakeSource(handler).GetDailyClosesAsync("VWCE.DE", From, To);

        Assert.Single(closes);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Retries_StopAfterThree()
    {
        var handler = new CountingHttpMessageHandler(Enumerable.Repeat((HttpStatusCode.TooManyRequests, ""), 5).ToArray());

        var ex = await Assert.ThrowsAsync<PriceSourceException>(() =>
            MakeSource(handler).GetDailyClosesAsync("VWCE.DE", From, To));

        Assert.Contains("did not answer", ex.Message);
        Assert.True(ex.Unavailable);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task NetworkFailure_BecomesAPriceSourceException()
    {
        var source = MakeSource(new FailingHttpMessageHandler(new HttpRequestException("No such host")));

        var ex = await Assert.ThrowsAsync<PriceSourceException>(() => source.GetDailyClosesAsync("VWCE.DE", From, To));

        Assert.Contains("No such host", ex.Message);
    }

    [Fact]
    public async Task IsinLookup_TakesTheGermanTicker_AndChecksItIsPricedInEur()
    {
        var figi = """[{"data": [{"ticker": "VWCE", "exchCode": "GR", "name": "VANG FTSE AW USDA"}]}]""";
        var handler = new CountingHttpMessageHandler((HttpStatusCode.OK, figi), (HttpStatusCode.OK, Chart("EUR")));

        var symbol = await MakeSource(handler).FindSymbolByIsinAsync("IE00BK5BQT80");

        Assert.Equal("VWCE.DE", symbol);
        Assert.Equal(["api.openfigi.com", "query1.finance.yahoo.com"], handler.Hosts);
    }

    [Fact]
    public async Task IsinLookup_WithoutAGermanListing_ReturnsNull()
    {
        var handler = new CountingHttpMessageHandler((HttpStatusCode.OK, """[{"warning": "No identifier found."}]"""));

        Assert.Null(await MakeSource(handler).FindSymbolByIsinAsync("XX0000000000"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task IsinLookup_WhenYahooDoesNotAnswer_FailsRatherThanFindNoListing()
    {
        var figi = """[{"data": [{"ticker": "VWCE", "exchCode": "GR"}]}]""";
        var handler = new CountingHttpMessageHandler(
            [(HttpStatusCode.OK, figi), .. Enumerable.Repeat((HttpStatusCode.ServiceUnavailable, ""), 4)]);

        var ex = await Assert.ThrowsAsync<PriceSourceException>(() =>
            MakeSource(handler).FindSymbolByIsinAsync("IE00BK5BQT80"));

        Assert.True(ex.Unavailable);
        Assert.Contains("did not answer", ex.Message);
    }

    [Fact]
    public async Task IsinLookup_ListingNotInEur_ReturnsNull()
    {
        var figi = """[{"data": [{"ticker": "ODD", "exchCode": "GR"}]}]""";
        var handler = new CountingHttpMessageHandler((HttpStatusCode.OK, figi), (HttpStatusCode.OK, Chart("USD")));

        Assert.Null(await MakeSource(handler).FindSymbolByIsinAsync("IE00BK5BQT80"));
    }
}

/// <summary>Answers from a queue and counts the calls and the hosts called.</summary>
internal sealed class CountingHttpMessageHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);

    public int Calls => Hosts.Count;
    public List<string> Hosts { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Hosts.Add(request.RequestUri!.Host);
        var (status, body) = _responses.Dequeue();
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
