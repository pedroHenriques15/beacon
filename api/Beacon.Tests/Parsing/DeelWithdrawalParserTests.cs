using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class DeelWithdrawalParserTests
{
    private readonly DeelWithdrawalParser _parser = new();

    private static string BuildSamplePage(
        string source = "1,541.50",
        string fee    = "10.79",
        string rate   = "0.86788828",
        string total  = "1,328.49") => $"""
        Confirmation Statement
        Withdrawn from Pages 1 of 1
        Example account Deel transaction ID 99999999
        Withdrawal state Withdrawn
        Transaction details
        Source currency USD · US Dollar
        Source amount ${source}
        Provider fee $0
        Exchange fees -${fee}
        Exchange rate 1.00 USD = {rate} EUR
        Total sent €{total}
        Withdrawn to
        Currency EUR · Euro
        """;

    [Fact]
    public void CanParse_ReturnsTrueForDeelSignal()
    {
        Assert.True(_parser.CanParse("Deel transaction ID 12345\nTotal sent €10.00"));
    }

    [Fact]
    public void CanParse_ReturnsFalseForUnrelatedText()
    {
        Assert.False(_parser.CanParse("Micro1 Inc. INVOICE Total USD $100.00"));
    }

    [Fact]
    public void CanParse_ReturnsFalseWhenDeelIdPresentButNoTotalSent()
    {
        Assert.False(_parser.CanParse("Payment reference: Deel transaction ID 98712773"));
    }

    [Fact]
    public void Parse_MissingExchangeFeeLine_DefaultsToZero()
    {
        var page = """
            Confirmation Statement
            Deel transaction ID 99999999
            Source amount $1,000.00
            Exchange rate 1.00 USD = 0.90000000 EUR
            Total sent €900.00
            """;
        var w = _parser.Parse([page]);
        Assert.Equal(0m, w.ExchangeFeeUsd);
    }

    [Fact]
    public void Parse_ExtractsSourceAmount()
    {
        var w = _parser.Parse([BuildSamplePage()]);
        Assert.Equal(1541.50m, w.SourceAmountUsd);
    }

    [Fact]
    public void Parse_ExtractsExchangeFeeAsPositiveMagnitude()
    {
        var w = _parser.Parse([BuildSamplePage()]);
        Assert.Equal(10.79m, w.ExchangeFeeUsd);
    }

    [Fact]
    public void Parse_ExtractsExchangeRate()
    {
        var w = _parser.Parse([BuildSamplePage()]);
        Assert.Equal(0.86788828m, w.ExchangeRate);
    }

    [Fact]
    public void Parse_ExtractsTotalEur()
    {
        var w = _parser.Parse([BuildSamplePage()]);
        Assert.Equal(1328.49m, w.TotalEur);
    }

    [Fact]
    public void Parse_MissingField_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _parser.Parse(["Deel transaction ID 1 but nothing else useful"]));
    }
}
