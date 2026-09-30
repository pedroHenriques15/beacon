using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class Micro1InvoiceParserTests
{
    private readonly Micro1InvoiceParser _parser = new();

    private static string BuildSamplePage(
        string billTo = "Micro1 Inc.",
        string period = "July 1, 2026 to July 15, 2026",
        string hours = "30.50",
        string payRate = "50",
        string basePay = "1525.00",
        string other = "75.00",
        string total = "1,600.00") => $"""
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

    /// <summary>
    /// Invoice with no earnings beyond base pay: the summary line carries no "| Other → $x" segment at
    /// all, and the header uses the older "Invoice #" / "Sub total" wording.
    /// </summary>
    private static string BuildBasePayOnlyPage(
        string hours = "7.20",
        string basePay = "360.00",
        string total = "360.00") => $"""
        INVOICE
        Invoice # INV-EXAMPLE-2
        Issue date June 17, 2026
        Due date June 20, 2026
        BILL FROM BILL TO TOTAL DUE
        Example Contractor Micro1 Inc. USD ${total}
        Group: Example Group
        Description Amount
        Pay As You Go Contract
        Invoice for work between June 1, 2026 to June 15, 2026
        for 1 submission
        Other: Project → Example Group | Hours → {hours} | Pay Rate → $50 | Base Pay → ${basePay} USD ${total}
        Sub total USD ${total}
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
        Assert.False(_parser.CanParse("01.07 TRANSFERENCIA RECEBIDA Micro1 Inc. 1 391,95"));
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
        Assert.Equal(1600.00m, result.GrossAmount);
        Assert.Equal(1600.00m, result.NetAmount);
    }

    [Fact]
    public void Parse_ExtractsBaseHoursAndRate()
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage()]);
        Assert.Equal(1525.00m, result.BaseAmount);
        Assert.Equal(30.50m, result.HoursWorked);
        Assert.Equal(50m, result.HourlyRate);
    }

    [Fact]
    public void Parse_ProducesBasePayAndOtherIncomeItems()
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage()]);

        Assert.Equal(2, result.LineItems.Count);
        var basePay = result.LineItems.First(i => i.Description == "Base Pay");
        var other = result.LineItems.First(i => i.Description == "Other");
        Assert.Equal(1525.00m, basePay.Amount);
        Assert.Equal("income", basePay.ItemType);
        Assert.Equal(75.00m, other.Amount);
        Assert.Equal("income", other.ItemType);
    }

    [Fact]
    public void Parse_HandlesUsThousandsSeparatorInBasePay()
    {
        var result = _parser.Parse("inv.pdf", [BuildSamplePage(basePay: "2,525.00", total: "2,600.00")]);
        Assert.Equal(2525.00m, result.BaseAmount);
        Assert.Equal(2600.00m, result.GrossAmount);
    }

    [Fact]
    public void Parse_HandlesInvoiceWithoutOtherSegment()
    {
        var result = _parser.Parse("inv.pdf", [BuildBasePayOnlyPage()]);

        Assert.Equal(360.00m, result.GrossAmount);
        Assert.Equal(360.00m, result.BaseAmount);
        Assert.Equal(7.20m, result.HoursWorked);
        Assert.Equal(50m, result.HourlyRate);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Period);
    }

    [Fact]
    public void Parse_OmitsOtherLineItemWhenInvoiceIsBasePayOnly()
    {
        var result = _parser.Parse("inv.pdf", [BuildBasePayOnlyPage()]);

        var item = Assert.Single(result.LineItems);
        Assert.Equal("Base Pay", item.Description);
        Assert.Equal(360.00m, item.Amount);
    }

    [Fact]
    public void Parse_FoldsUnbrokenDownRemainderIntoOtherWhenSegmentIsMissing()
    {
        // Base pay below the total with no "Other →" segment: the difference must not be dropped.
        var result = _parser.Parse("inv.pdf", [BuildBasePayOnlyPage(basePay: "300.00", total: "360.00")]);

        var other = result.LineItems.First(i => i.Description == "Other");
        Assert.Equal(60.00m, other.Amount);
        Assert.Equal("income", other.ItemType);
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
    [InlineData("1,234.56", 1234.56)]
    [InlineData("75.00", 75.00)]
    public void ParseUs_ConvertsUsFormatDecimals(string input, double expected)
    {
        Assert.Equal((decimal)expected, Micro1InvoiceParser.ParseUs(input));
    }
}
