using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class Micro1InvoiceParserTests
{
    private readonly Micro1InvoiceParser _parser = new();

    private static string BuildSamplePage(
        string billTo   = "Micro1 Inc.",
        string period   = "July 1, 2026 to July 15, 2026",
        string hours    = "28.73",
        string payRate  = "50",
        string basePay  = "1436.50",
        string other    = "105.00",
        string total    = "1,541.50") => $"""
        INVOICE
        Document INV-EXAMPLE-1
        Issue Date July 17, 2026
        BILL FROM BILL TO TOTAL
        Example Contractor {billTo} USD ${total}
        Group: Example Group
        Description Amount
        Pay As You Go Contract
        Invoice for work between {period}
        for 1 submission
        Other: Project → Example Group | Hours → {hours} | Pay Rate → ${payRate} | Base Pay → ${basePay} | Other → ${other} USD ${total}
        Subtotal USD ${total}
        VAT 0% USD $0
        Total USD ${total}
        Page 1/1
        """;

    [Fact]
    public void CanParse_ReturnsTrueForMicro1Signal()
    {
        Assert.True(_parser.CanParse("BILL TO Micro1 Inc. 655 Montgomery St\nTotal USD $10.00"));
    }

    [Fact]
    public void CanParse_ReturnsFalseForUnrelatedText()
    {
        Assert.False(_parser.CanParse("CentralGest Software - RECIBA5.RPT"));
    }

    [Fact]
    public void CanParse_ReturnsFalseWhenMicro1NamedButNoInvoiceTotal()
    {
        // A bank statement whose transaction line merely mentions the employer must not be taken for an invoice.
        Assert.False(_parser.CanParse("01.07 TRANSFERENCIA RECEBIDA Micro1 Inc. 1 328,49"));
    }

    [Fact]
    public void ParserName_IsMicro1()
    {
        Assert.Equal("Micro1", _parser.ParserName);
    }

    [Fact]
    public void Parse_SetsEmployerToMicro1()
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage()]);
        Assert.Equal("Micro1 Inc.", result.Employer);
    }

    [Fact]
    public void Parse_ExtractsPeriod_FirstOfWorkMonth()
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage()]);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Period);
    }

    [Fact]
    public void Parse_GrossAndNetAreTotalUsd()
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage()]);
        Assert.Equal(1541.50m, result.GrossAmount);
        Assert.Equal(1541.50m, result.NetAmount);
    }

    [Fact]
    public void Parse_ExtractsBaseHoursAndRate()
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage()]);
        Assert.Equal(1436.50m, result.BaseAmount);
        Assert.Equal(28.73m, result.HoursWorked);
        Assert.Equal(50m, result.HourlyRate);
    }

    [Fact]
    public void Parse_ProducesBasePayAndOtherIncomeItems()
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage()]);

        Assert.Equal(2, result.LineItems.Count);
        var basePay = result.LineItems.First(i => i.Description == "Base Pay");
        var other   = result.LineItems.First(i => i.Description == "Other");
        Assert.Equal(1436.50m, basePay.Amount);
        Assert.Equal("income", basePay.ItemType);
        Assert.Equal(105.00m, other.Amount);
        Assert.Equal("income", other.ItemType);
    }

    [Fact]
    public void Parse_HandlesUsThousandsSeparatorInBasePay()
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage(basePay: "2,436.50", total: "2,541.50")]);
        Assert.Equal(2436.50m, result.BaseAmount);
        Assert.Equal(2541.50m, result.GrossAmount);
    }

    [Theory]
    [InlineData("January 1, 2026 to January 15, 2026", 1)]
    [InlineData("December 1, 2025 to December 15, 2025", 12)]
    public void Parse_ExtractsPeriodMonth(string period, int expectedMonth)
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage(period: period)]);
        Assert.Equal(expectedMonth, result.Period.Month);
    }

    [Theory]
    [InlineData("1,541.50", 1541.50)]
    [InlineData("105.00", 105.00)]
    public void ParseUs_ConvertsUsFormatDecimals(string input, double expected)
    {
        Assert.Equal((decimal)expected, Micro1InvoiceParser.ParseUs(input));
    }
}
