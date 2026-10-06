using Beacon.Api.Services.Parsing;
using Xunit;
using static Beacon.Tests.Parsing.XtbWorkbook;

namespace Beacon.Tests.Parsing;

public class XtbExportParserTests
{
    private readonly XtbExportParser _parser = new();

    private static readonly DateTime June10 = new(2026, 6, 10, 9, 30, 0, DateTimeKind.Utc);

    private static XlsxWorkbook Read(MemoryStream file) => XlsxWorkbook.Read(file);

    private XtbExport Parse(params object?[][] operations) =>
        _parser.Parse(FileName, Read(Export(operations)));

    [Fact]
    public void CanParse_TheThreeSheetsEachWithAnAccountNumber()
    {
        Assert.True(_parser.CanParse(Read(Export([]))));
    }

    [Fact]
    public void CanParse_AnotherWorkbook_IsFalse()
    {
        var other = Write(("Sheet1", [["Account number", "1"]]), ("Cash Operations", [["Type", "Amount"]]));

        Assert.False(_parser.CanParse(Read(other)));
    }

    [Fact]
    public void Parse_APurchase_IsABuyOfItsTicker_DatedInLisbon()
    {
        // 23:30 UTC on 30 June is 00:30 on 1 July in Lisbon (summer time).
        var late = new DateTime(2026, 6, 30, 23, 30, 0, DateTimeKind.Utc);

        var export = Parse(Purchase(late, "SXR8.DE", -300.00m, "OPEN BUY 0.5 @ 600.00", "5550001"));

        var trade = Assert.Single(export.Trades);
        Assert.Null(trade.Isin);
        Assert.Equal("SXR8.DE", trade.Ticker);
        Assert.Equal("Example S&P 500", trade.AssetName);
        Assert.Equal(new DateOnly(2026, 7, 1), trade.Date);
        Assert.Equal(0.5m, trade.Quantity);
        Assert.Equal(600.00m, trade.PricePerUnit);
        Assert.Equal(0m, trade.Fees);
        Assert.Equal("XTB:5550001", trade.ExternalId);
        Assert.Equal("XTB investment plan", trade.Note);
    }

    [Fact]
    public void Parse_ASplitFill_TakesTheFillsShare()
    {
        var export = Parse(
            Purchase(June10, "VWCE.DE", -200.00m, "OPEN BUY 2/2.5 @ 100.00", "5550001"),
            Purchase(June10, "VWCE.DE", -50.00m, "OPEN BUY 0.5/2.5 @ 100.00", "5550002"));

        Assert.Equal([2m, 0.5m], export.Trades.Select(t => t.Quantity));
    }

    [Fact]
    public void Parse_ASell_IsANegativeQuantity()
    {
        var export = Parse(Sell(June10, "VWCE.DE", 55.00m, "CLOSE BUY 0.5/2.5 @ 110.00", "5550003"));

        var trade = Assert.Single(export.Trades);
        Assert.Equal(-0.5m, trade.Quantity);
        Assert.Equal(110.00m, trade.PricePerUnit);
        Assert.Equal("XTB sell", trade.Note);
    }

    [Fact]
    public void Parse_AnAmountRoundedToTheCent_IsTheQuantityAtThePrice()
    {
        // 0.8333 × 720.00 = 599.976: XTB spent 600.00 and shows the quantity to four places.
        var export = Parse(Purchase(June10, "SXR8.DE", -600.00m, "OPEN BUY 0.8333 @ 720.00", "5550001"));

        Assert.Equal(0.8333m, Assert.Single(export.Trades).Quantity);
    }

    [Fact]
    public void Parse_DepositsAndSubaccountTransfers_AreSkipped()
    {
        var export = Parse(
            Operation("Deposit", June10, 300.00m, "5550010", "bank deposit"),
            Operation("Subaccount transfer", June10, -300.00m, "5550011"),
            Operation("Subaccount transfer", June10, 300.00m, "5550012"),
            Purchase(June10.AddMinutes(1), "SXR8.DE", -300.00m, "OPEN BUY 0.5 @ 600.00", "5550013"));

        Assert.Equal(["XTB:5550013"], export.Trades.Select(t => t.ExternalId));
    }

    [Fact]
    public void Parse_TradesComeOldestFirst()
    {
        // The sheet lists the newest first.
        var export = Parse(
            Sell(June10.AddDays(5), "VWCE.DE", 110.00m, "CLOSE BUY 1 @ 110.00", "5550002"),
            Purchase(June10, "VWCE.DE", -100.00m, "OPEN BUY 1 @ 100.00", "5550001"));

        Assert.Equal(["XTB:5550001", "XTB:5550002"], export.Trades.Select(t => t.ExternalId));
    }

    [Fact]
    public void Parse_AnIdStoredAsANumber_IsKeptWhole()
    {
        var export = Parse(Purchase(June10, "SXR8.DE", -300.00m, "OPEN BUY 0.5 @ 600.00", 1234567890m));

        Assert.Equal("XTB:1234567890", Assert.Single(export.Trades).ExternalId);
    }

    [Fact]
    public void Parse_ReadsThePeriodInLisbon_AndTheHoldingsByTicker()
    {
        var export = _parser.Parse(FileName, Read(Export([],
            positions: [Position("SXR8.DE", 0.5m), Position("SXR8.DE", 0.25m), Position("VWCE.DE", 2m)])));

        Assert.Equal(new DateOnly(2026, 6, 1), export.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 6, 30), export.PeriodTo);
        Assert.Equal(Generated, export.GeneratedAtUtc, TimeSpan.FromSeconds(1));
        Assert.Equal(
            [new XtbHolding("SXR8.DE", 0.75m), new XtbHolding("VWCE.DE", 2m)],
            export.Holdings.OrderBy(h => h.Ticker));
    }

    [Fact]
    public void Parse_AnUnknownType_IsRefusedNamingIt()
    {
        var ex = Assert.Throws<FormatException>(() => Parse(Operation("Dividend", June10, 1.20m, "5550020")));

        Assert.Contains("\"Dividend\"", ex.Message);
        Assert.Contains("2026-06-10", ex.Message);
    }

    [Fact]
    public void Parse_AnInstrumentOtherThanAnEtf_IsRefused()
    {
        var ex = Assert.Throws<FormatException>(() =>
            Parse(Purchase(June10, "AAPL.US", -300.00m, "OPEN BUY 1 @ 300.00", "5550001", category: "STC")));

        Assert.Contains("STC", ex.Message);
    }

    [Fact]
    public void Parse_AnAmountThatIsNotTheQuantityAtThePrice_IsRefused()
    {
        // A commission on top, which no export has shown yet.
        var ex = Assert.Throws<FormatException>(() =>
            Parse(Purchase(June10, "SXR8.DE", -301.20m, "OPEN BUY 0.5 @ 600.00", "5550001")));

        Assert.Contains("commission", ex.Message);
    }

    [Fact]
    public void Parse_ACommentThatIsNotABuy_IsRefused()
    {
        Assert.Throws<FormatException>(() =>
            Parse(Purchase(June10, "SXR8.DE", -300.00m, "OPEN SELL 0.5 @ 600.00", "5550001")));
    }

    [Fact]
    public void Parse_AnAccountNotInEur_IsRefused_ByItsFileName()
    {
        var ex = Assert.Throws<FormatException>(() =>
            _parser.Parse("USD_10000001_2026-05-31_2026-06-30.xlsx", Read(Export([]))));

        Assert.Contains("USD", ex.Message);
    }

    [Fact]
    public void Parse_AnAccountNotInEur_IsRefused_ByItsSheets()
    {
        var ex = Assert.Throws<FormatException>(() =>
            _parser.Parse("export.xlsx", Read(Export([], summaryCurrency: "USD"))));

        Assert.Contains("USD", ex.Message);
    }

    [Fact]
    public void Parse_AShortPosition_IsRefused()
    {
        Assert.Throws<FormatException>(() =>
            _parser.Parse(FileName, Read(Export([], positions: [Position("SXR8.DE", 1m, type: "SELL")]))));
    }
}
