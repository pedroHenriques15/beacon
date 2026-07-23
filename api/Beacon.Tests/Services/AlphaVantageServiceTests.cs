using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.Extensions.Configuration;

namespace Beacon.Tests.Services;

public class AlphaVantageServiceTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AlphaVantage:ApiKey"] = "test-key" })
            .Build();

    private static AlphaVantageService MakeService(HttpMessageHandler handler) =>
        new(new FakeHttpClientFactory(handler), Config());

    private const string EtfQuoteBody =
        """{"Global Quote": {"01. symbol": "VWCE", "05. price": "130.2500"}}""";

    private const string GoldRateBody =
        """{"Realtime Currency Exchange Rate": {"5. Exchange Rate": "2000.00000000"}}""";

    [Fact]
    public async Task FetchEtfPrice_ValidResponse_ReturnsPrice()
    {
        var service = MakeService(new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, EtfQuoteBody));

        var price = await service.FetchEtfPriceAsync("VWCE");

        Assert.Equal(130.25m, price);
    }

    [Fact]
    public async Task FetchEtfPrice_PinsGlobalQuoteUri()
    {
        var handler = new RecordingHttpMessageHandler(System.Net.HttpStatusCode.OK, EtfQuoteBody);

        await MakeService(handler).FetchEtfPriceAsync("VWCE");

        var query = handler.Requests.Single().Query;
        Assert.Contains("function=GLOBAL_QUOTE", query);
        Assert.Contains("symbol=VWCE", query);
    }

    [Fact]
    public async Task FetchEtfPrice_NoteResponse_ThrowsRateLimit()
    {
        var service = MakeService(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK, """{"Note": "API call frequency reached"}"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchEtfPriceAsync("VWCE"));

        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchEtfPrice_InformationMentioningApiKey_ThrowsInvalidKeyHint()
    {
        var service = MakeService(new FakeHttpMessageHandler(
            System.Net.HttpStatusCode.OK, """{"Information": "The provided apikey is invalid"}"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchEtfPriceAsync("VWCE"));

        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task FetchGoldPrice_ConvertsTroyOunceToGrams()
    {
        var service = MakeService(new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, GoldRateBody));

        var price = await service.FetchGoldPricePerGramAsync();

        Assert.Equal(Math.Round(2000m / 31.1035m, 4), price);
    }

    [Fact]
    public async Task FetchGoldPrice_PinsCurrencyExchangeUri()
    {
        var handler = new RecordingHttpMessageHandler(System.Net.HttpStatusCode.OK, GoldRateBody);

        await MakeService(handler).FetchGoldPricePerGramAsync();

        var query = handler.Requests.Single().Query;
        Assert.Contains("function=CURRENCY_EXCHANGE_RATE", query);
        Assert.Contains("from_currency=XAU", query);
        Assert.Contains("to_currency=EUR", query);
    }

    [Fact]
    public async Task FetchDailySeries_Etf_ParsesClosesAndPinsUri()
    {
        var body = """
            {"Time Series (Daily)": {
                "2026-07-22": {"4. close": "130.2500"},
                "2026-07-21": {"4. close": "128.0000"}
            }}
            """;
        var handler = new RecordingHttpMessageHandler(System.Net.HttpStatusCode.OK, body);
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = "VWCE", Name = "Vanguard" };

        var series = await MakeService(handler).FetchDailySeriesAsync(asset);

        Assert.Equal(2, series.Count);
        Assert.Equal(130.25m, series[new DateOnly(2026, 7, 22)]);
        Assert.Equal(128m, series[new DateOnly(2026, 7, 21)]);
        var query = handler.Requests.Single().Query;
        Assert.Contains("function=TIME_SERIES_DAILY", query);
        Assert.Contains("outputsize=full", query);
    }

    [Fact]
    public async Task FetchDailySeries_Gold_ConvertsAndPinsFxDailyUri()
    {
        var body = """{"Time Series FX (Daily)": {"2026-07-22": {"4. close": "3110.35"}}}""";
        var handler = new RecordingHttpMessageHandler(System.Net.HttpStatusCode.OK, body);
        var asset = new InvestmentAsset { AssetType = "Gold", Name = "Physical Gold" };

        var series = await MakeService(handler).FetchDailySeriesAsync(asset);

        Assert.Equal(100.0000m, series[new DateOnly(2026, 7, 22)]);
        var query = handler.Requests.Single().Query;
        Assert.Contains("function=FX_DAILY", query);
        Assert.Contains("from_symbol=XAU", query);
        Assert.Contains("to_symbol=EUR", query);
    }

    [Fact]
    public async Task FetchDailySeries_EtfWithoutTicker_Throws()
    {
        var service = MakeService(new ThrowingHttpMessageHandler());
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = null, Name = "Broken" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchDailySeriesAsync(asset));
    }
}
