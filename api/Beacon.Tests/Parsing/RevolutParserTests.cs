using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class RevolutParserTests
{
    private readonly RevolutParser _parser = new();

    [Theory]
    [InlineData("REVOPTP2")]
    [InlineData("Revolut Bank UAB")]
    public void CanParse_ReturnsTrueForKnownSignals(string signal)
    {
        Assert.True(_parser.CanParse($"some text {signal} more text"));
    }

    [Fact]
    public void CanParse_ReturnsFalseForUnrelatedText()
    {
        Assert.False(_parser.CanParse("ACTVPTPL ActivoBank BBPIPTPL"));
    }

    [Fact]
    public void BankName_IsRevolut()
    {
        Assert.Equal("REVOLUT", _parser.BankName);
    }

    private static string BuildPage(
        string iban    = "PTabc123",
        string summary = "Conta (Conta Corrente) 1.000,00€ 0,00€ 0,00€ 2.000,00€",
        string? txLine = null)
    {
        return $"""
            IBAN {iban}
            {summary}
            {txLine ?? string.Empty}
            """;
    }

    private const string ValidFileName = "revolut_2024-01-01_2024-01-31.pdf";

    [Fact]
    public void Parse_ExtractsPeriodFromFilename()
    {
        var result = _parser.Parse(ValidFileName, [BuildPage()]);

        Assert.Equal(new DateOnly(2024, 1, 1),  result.PeriodFrom);
        Assert.Equal(new DateOnly(2024, 1, 31), result.PeriodTo);
    }

    [Fact]
    public void Parse_PeriodDefaultsWhenFilenameHasNoPattern()
    {
        var result = _parser.Parse("statement.pdf", [BuildPage()]);

        Assert.Equal(default, result.PeriodFrom);
        Assert.Equal(default, result.PeriodTo);
    }

    [Fact]
    public void Parse_ExtractsIban()
    {
        var result = _parser.Parse(ValidFileName, [BuildPage(iban: "PTxyz456")]);
        Assert.Equal("PTxyz456", result.Account);
    }

    [Fact]
    public void Parse_CurrencyIsAlwaysEur()
    {
        var result = _parser.Parse(ValidFileName, [BuildPage()]);
        Assert.Equal("EUR", result.Currency);
    }

    [Fact]
    public void Parse_ExtractsOpeningAndClosingFromSummary()
    {
        var summary = "Conta (Conta Corrente) 1.000,00€ 0,00€ 0,00€ 2.500,00€";
        var result  = _parser.Parse(ValidFileName, [BuildPage(summary: summary)]);

        Assert.Equal(1000.00m, result.OpeningBalance);
        Assert.Equal(2500.00m, result.ClosingBalance);
    }

    [Fact]
    public void Parse_NoSummaryLine_BalancesAreZero()
    {
        var page = $"""
            IBAN PTabc123
            Some other text
            """;

        var result = _parser.Parse(ValidFileName, [page]);

        Assert.Equal(0m, result.OpeningBalance);
        Assert.Equal(0m, result.ClosingBalance);
    }

    [Fact]
    public void Parse_ParsesCreditTransaction()
    {
        var summary = "Conta (Conta Corrente) 1.000,00€ 0,00€ 0,00€ 1.500,00€";
        var txLine  = "01/01/2024 01/01/2024 TRANSFER IN 500,00€ 1.500,00€";
        var result  = _parser.Parse(ValidFileName, [BuildPage(summary: summary, txLine: txLine)]);

        Assert.Single(result.Transactions);
        var tx = result.Transactions[0];
        Assert.Equal("TRANSFER IN", tx.Description);
        Assert.Equal(500.00m, tx.Amount);
        Assert.Equal("credit", tx.Type);
        Assert.Equal(1500.00m, tx.Balance);
        Assert.Equal(new DateOnly(2024, 1, 1), tx.DatePosting);
    }

    [Fact]
    public void Parse_ParsesDebitTransaction()
    {
        var summary = "Conta (Conta Corrente) 1.000,00€ 0,00€ 0,00€ 700,00€";
        var txLine  = "10/01/2024 10/01/2024 PAYMENT OUT 300,00€ 700,00€";
        var result  = _parser.Parse(ValidFileName, [BuildPage(summary: summary, txLine: txLine)]);

        Assert.Single(result.Transactions);
        var tx = result.Transactions[0];
        Assert.Equal(300.00m, tx.Amount);
        Assert.Equal("debit", tx.Type);
    }

    [Fact]
    public void Parse_TypeUnknownWhenDeltaDoesNotMatchAmount()
    {
        var summary = "Conta (Conta Corrente) 1.000,00€ 0,00€ 0,00€ 1.050,00€";
        var txLine  = "05/01/2024 05/01/2024 WEIRD TX 100,00€ 1.050,00€";
        var result  = _parser.Parse(ValidFileName, [BuildPage(summary: summary, txLine: txLine)]);

        Assert.Single(result.Transactions);
        Assert.Equal("unknown", result.Transactions[0].Type);
    }

    [Fact]
    public void Parse_MultipleTransactions_BalanceChainedCorrectly()
    {
        var summary = "Conta (Conta Corrente) 1.000,00€ 0,00€ 0,00€ 1.200,00€";
        var txLines = """
            01/01/2024 01/01/2024 INCOME 500,00€ 1.500,00€
            02/01/2024 02/01/2024 EXPENSE 300,00€ 1.200,00€
            """;

        var result = _parser.Parse(ValidFileName, [BuildPage(summary: summary, txLine: txLines)]);

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal("credit", result.Transactions[0].Type);
        Assert.Equal("debit",  result.Transactions[1].Type);
    }

    [Fact]
    public void Parse_ParsesDateInDdMmYyyyFormat()
    {
        var summary = "Conta (Conta Corrente) 500,00€ 0,00€ 0,00€ 600,00€";
        var txLine  = "15/03/2024 16/03/2024 SOME TX 100,00€ 600,00€";
        var result  = _parser.Parse(ValidFileName, [BuildPage(summary: summary, txLine: txLine)]);

        var tx = result.Transactions[0];
        Assert.Equal(new DateOnly(2024, 3, 15), tx.DatePosting);
        Assert.Equal(new DateOnly(2024, 3, 16), tx.DateValue);
    }

    [Fact]
    public void Parse_AmountWithThousandDots_ParsesCorrectly()
    {
        var summary = "Conta (Conta Corrente) 10.000,00€ 0,00€ 0,00€ 11.000,00€";
        var txLine  = "01/01/2024 01/01/2024 BIG TX 1.000,00€ 11.000,00€";
        var result  = _parser.Parse(ValidFileName, [BuildPage(summary: summary, txLine: txLine)]);

        Assert.Equal(10000.00m, result.OpeningBalance);
        Assert.Equal(1000.00m,  result.Transactions[0].Amount);
    }
}
