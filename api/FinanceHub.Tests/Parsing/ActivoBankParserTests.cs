using FinanceHub.Api.Services.Parsing;
using Xunit;

namespace FinanceHub.Tests.Parsing;

public class ActivoBankParserTests
{
    private readonly ActivoBankParser _parser = new();

    [Theory]
    [InlineData("ACTVPTPL")]
    [InlineData("ActivoBank")]
    [InlineData("EXTRATO COMBINADO")]
    public void CanParse_ReturnsTrueForKnownSignals(string signal)
    {
        Assert.True(_parser.CanParse($"some text {signal} more text"));
    }

    [Fact]
    public void CanParse_ReturnsFalseForUnrelatedText()
    {
        Assert.False(_parser.CanParse("BBPIPTPL some BPI statement text"));
    }

    [Fact]
    public void BankName_IsActivobank()
    {
        Assert.Equal("ACTIVOBANK", _parser.BankName);
    }

    private static string BuildSamplePage(string? extra = null) => $"""
        DEPOSITO A ORDEM: 123456789
        EXTRATO DE 2024/01/01 A 2024/01/31
        MOEDA BASE: EURO
        SALDO INICIAL 1 000.00
        01.01 01.02 TRANSFERENCIA RECEBIDA 500.00 1 500.00
        01.15 01.15 PAGAMENTO SERVICOS 200.00 1 300.00
        A TRANSPORTAR 1 300.00
        SALDO FINAL 1 300.00
        {extra ?? string.Empty}
        """;

    [Fact]
    public void Parse_ExtractsAccountAndPeriod()
    {
        var result = _parser.Parse("statement.pdf", [BuildSamplePage()]);

        Assert.Equal("ACTIVOBANK", result.Bank);
        Assert.Equal("123456789", result.Account);
        Assert.Equal(new DateOnly(2024, 1, 1), result.PeriodFrom);
        Assert.Equal(new DateOnly(2024, 1, 31), result.PeriodTo);
        Assert.Equal("EUR", result.Currency);
    }

    [Fact]
    public void Parse_ExtractsBalances()
    {
        var result = _parser.Parse("statement.pdf", [BuildSamplePage()]);

        Assert.Equal(1000.00m, result.OpeningBalance);
        Assert.Equal(1300.00m, result.ClosingBalance);
    }

    [Fact]
    public void Parse_ExtractsTwoTransactions()
    {
        var result = _parser.Parse("statement.pdf", [BuildSamplePage()]);

        Assert.Equal(2, result.Transactions.Count);
    }

    [Fact]
    public void Parse_FirstTransaction_IsCreditFromBalanceIncrease()
    {
        var result = _parser.Parse("statement.pdf", [BuildSamplePage()]);
        var tx = result.Transactions[0];

        Assert.Equal(new DateOnly(2024, 1, 1), tx.DatePosting);
        Assert.Equal(new DateOnly(2024, 1, 2), tx.DateValue);
        Assert.Equal("TRANSFERENCIA RECEBIDA", tx.Description);
        Assert.Equal(500.00m, tx.Amount);
        Assert.Equal("credit", tx.Type);
        Assert.Equal(1500.00m, tx.Balance);
    }

    [Fact]
    public void Parse_SecondTransaction_IsDebitFromBalanceDecrease()
    {
        var result = _parser.Parse("statement.pdf", [BuildSamplePage()]);
        var tx = result.Transactions[1];

        Assert.Equal("PAGAMENTO SERVICOS", tx.Description);
        Assert.Equal(200.00m, tx.Amount);
        Assert.Equal("debit", tx.Type);
        Assert.Equal(1300.00m, tx.Balance);
    }

    [Fact]
    public void Parse_SkipsLinesWithSkipPrefixes()
    {
        var result = _parser.Parse("statement.pdf", [BuildSamplePage()]);
        Assert.All(result.Transactions, tx => Assert.DoesNotContain("A TRANSPORTAR", tx.Description));
        Assert.All(result.Transactions, tx => Assert.DoesNotContain("SALDO FINAL",   tx.Description));
    }

    [Fact]
    public void Parse_CurrencyEuro_MapsToEur()
    {
        var result = _parser.Parse("statement.pdf", [BuildSamplePage()]);
        Assert.Equal("EUR", result.Currency);
    }

    [Fact]
    public void Parse_AmountWithThousandsSeparator_ParsesCorrectly()
    {
        var result = _parser.Parse("statement.pdf", [BuildSamplePage()]);
        Assert.Equal(1000.00m, result.OpeningBalance);
    }

    [Fact]
    public void Parse_YearRollover_PreviousMonthGetsDecrementedYear()
    {
        var page = """
            DEPOSITO A ORDEM: 999
            EXTRATO DE 2024/01/01 A 2024/01/31
            MOEDA BASE: EURO
            SALDO INICIAL 500.00
            12.31 12.31 SALDO ANTERIOR 500.00 1 000.00
            01.01 01.01 NOVA TRANSFERENCIA 500.00 1 500.00
            SALDO FINAL 1 500.00
            """;

        var result = _parser.Parse("statement.pdf", [page]);

        var decTx = result.Transactions.First(t => t.DatePosting.Month == 12);
        Assert.Equal(2023, decTx.DatePosting.Year);
    }

    [Fact]
    public void Parse_MultiplePages_AggregatesTransactions()
    {
        var page1 = """
            DEPOSITO A ORDEM: 111
            EXTRATO DE 2024/01/01 A 2024/01/31
            MOEDA BASE: EURO
            SALDO INICIAL 1 000.00
            01.01 01.01 FIRST TX 100.00 1 100.00
            """;

        var page2 = """
            TRANSPORTE 1 100.00
            01.02 01.02 SECOND TX 50.00 1 050.00
            SALDO FINAL 1 050.00
            """;

        var result = _parser.Parse("statement.pdf", [page1, page2]);

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal("FIRST TX",  result.Transactions[0].Description);
        Assert.Equal("SECOND TX", result.Transactions[1].Description);
    }

    [Fact]
    public void Parse_TypeUnknown_WhenBalanceNotInitialized()
    {
        var page = """
            DEPOSITO A ORDEM: 111
            EXTRATO DE 2024/01/01 A 2024/01/31
            MOEDA BASE: EURO
            01.01 01.01 MYSTERY TX 100.00 1 100.00
            SALDO INICIAL 1 000.00
            SALDO FINAL 1 100.00
            """;

        var result = _parser.Parse("statement.pdf", [page]);

        Assert.Equal("unknown", result.Transactions[0].Type);
    }
}
