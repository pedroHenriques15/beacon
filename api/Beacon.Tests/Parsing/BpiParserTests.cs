using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class BpiParserTests
{
    private readonly BpiParser _parser = new();

    [Theory]
    [InlineData("BBPIPTPL")]
    [InlineData("EXTRACTO INTEGRADO")]
    public void CanParse_ReturnsTrueForKnownSignals(string signal)
    {
        Assert.True(_parser.CanParse($"some text {signal} more text"));
    }

    [Fact]
    public void CanParse_ReturnsFalseForUnrelatedText()
    {
        Assert.False(_parser.CanParse("ACTVPTPL EXTRATO COMBINADO ActivoBank"));
    }

    [Fact]
    public void BankName_IsBpi()
    {
        Assert.Equal("BPI", _parser.BankName);
    }

    private static string BuildFullText(
        string iban = "PT50 0010 0000 0000 0000 0000 1",
        string periodFrom = "01/01/2024",
        string periodTo   = "31/01/2024",
        string opening    = "1 000,00",
        string closing    = "1 500,00",
        string? activos   = null,
        string? txBlock   = null)
    {
        var activosLine = activos is not null ? $"ACTIVOS {activos}" : string.Empty;

        return $"""
            IBAN: {iban}
            Período De {periodFrom} a {periodTo}
            SALDO ANTERIOR CONTABILISTICO {opening}
            SALDO ACTUAL CONTABILISTICO {closing}
            {activosLine}
            DEPÓSITOS À ORDEM
            {txBlock ?? string.Empty}
            PLANOS DE POUPANÇA
            """;
    }

    [Fact]
    public void Parse_ExtractsPeriodFromHeader()
    {
        var result = _parser.Parse("bpi.pdf", [BuildFullText()]);

        Assert.Equal(new DateOnly(2024, 1, 1),  result.PeriodFrom);
        Assert.Equal(new DateOnly(2024, 1, 31), result.PeriodTo);
    }

    [Fact]
    public void Parse_ExtractsIbanAsAccount()
    {
        var result = _parser.Parse("bpi.pdf", [BuildFullText(iban: "PT50 0010 0000 0000 0000 0000 1")]);

        Assert.Equal("PT50001000000000000000001", result.Account);
    }

    [Fact]
    public void Parse_CurrencyIsAlwaysEur()
    {
        var result = _parser.Parse("bpi.pdf", [BuildFullText()]);
        Assert.Equal("EUR", result.Currency);
    }

    [Fact]
    public void Parse_WithoutActivos_UsesCheckingAccountBalances()
    {
        var result = _parser.Parse("bpi.pdf", [BuildFullText(opening: "1 000,00", closing: "1 500,00")]);

        Assert.Equal(1000.00m, result.OpeningBalance);
        Assert.Equal(1500.00m, result.ClosingBalance);
    }

    [Fact]
    public void Parse_WithActivos_ClosingIsActivos()
    {
        var result = _parser.Parse("bpi.pdf", [BuildFullText(
            opening: "1 000,00",
            closing: "1 500,00",
            activos: "10 000,00")]);

        Assert.Equal(10000.00m, result.ClosingBalance);
        Assert.Equal(9500.00m,  result.OpeningBalance);
    }

    [Fact]
    public void Parse_WithActivos_SetsPprBalance()
    {
        var result = _parser.Parse("bpi.pdf", [BuildFullText(
            opening: "1 000,00",
            closing: "1 500,00",
            activos: "10 000,00")]);

        Assert.Equal(8500.00m, result.PprBalance);
    }

    [Fact]
    public void Parse_WithoutActivos_PprBalanceIsNull()
    {
        var result = _parser.Parse("bpi.pdf", [BuildFullText(opening: "1 000,00", closing: "1 500,00")]);

        Assert.Null(result.PprBalance);
    }

    [Fact]
    public void Parse_ParsesCreditTransaction()
    {
        var txBlock = "01/01 02/01 TRANSFERENCIA RECEBIDA 500,00 1 500,00";
        var result  = _parser.Parse("bpi.pdf", [BuildFullText(txBlock: txBlock)]);

        Assert.Single(result.Transactions);
        var tx = result.Transactions[0];
        Assert.Equal("TRANSFERENCIA RECEBIDA", tx.Description);
        Assert.Equal(500.00m, tx.Amount);
        Assert.Equal("credit", tx.Type);
        Assert.Equal(1500.00m, tx.Balance);
        Assert.Equal(new DateOnly(2024, 1, 1), tx.DatePosting);
        Assert.Equal(new DateOnly(2024, 1, 2), tx.DateValue);
    }

    [Fact]
    public void Parse_ParsesDebitTransaction_NegativeAmount()
    {
        var txBlock = "15/01 15/01 PAGAMENTO FATURA -200,00 800,00";
        var result  = _parser.Parse("bpi.pdf", [BuildFullText(txBlock: txBlock)]);

        Assert.Single(result.Transactions);
        var tx = result.Transactions[0];
        Assert.Equal(200.00m, tx.Amount);
        Assert.Equal("debit", tx.Type);
    }

    [Fact]
    public void Parse_MissingValueDate_FallsBackToPostingDate()
    {
        var txBlock = "01/01 PAGAMENTO SEM DATA VAL 100,00 900,00";
        var result  = _parser.Parse("bpi.pdf", [BuildFullText(txBlock: txBlock)]);

        if (result.Transactions.Count > 0)
        {
            var tx = result.Transactions[0];
            Assert.Equal(tx.DatePosting, tx.DateValue);
        }
    }

    [Fact]
    public void Parse_SkipsLinesOutsideCurrentAccountBlock()
    {
        var fullText = $"""
            IBAN: PT50001000000000000000001
            Período De 01/01/2024 a 31/01/2024
            SALDO ANTERIOR CONTABILISTICO 1 000,00
            SALDO ACTUAL CONTABILISTICO 1 500,00
            01/01 01/01 OUTSIDE BLOCK TX 999,00 999,00
            DEPÓSITOS À ORDEM
            01/01 01/01 INSIDE BLOCK TX 500,00 1 500,00
            PLANOS DE POUPANÇA
            """;

        var result = _parser.Parse("bpi.pdf", [fullText]);

        Assert.Single(result.Transactions);
        Assert.Equal("INSIDE BLOCK TX", result.Transactions[0].Description);
    }

    [Fact]
    public void Parse_SkipsHeaderLines()
    {
        var txBlock = """
            DATA DATA DESCRICAO VALOR SALDO
            MOV VAL
            01/01 01/01 REAL TX 100,00 1 100,00
            """;
        var result = _parser.Parse("bpi.pdf", [BuildFullText(txBlock: txBlock)]);

        Assert.Single(result.Transactions);
        Assert.Equal("REAL TX", result.Transactions[0].Description);
    }

    [Fact]
    public void Parse_IncludesBpiReformaTransactionLines()
    {
        var txBlock = "01/01 01/01 BPI REFORMA PLANO POUPANCA -300,00 1 700,00";
        var result  = _parser.Parse("bpi.pdf", [BuildFullText(txBlock: txBlock)]);

        Assert.Single(result.Transactions);
        Assert.Contains("BPI REFORMA", result.Transactions[0].Description);
        Assert.Equal(300.00m, result.Transactions[0].Amount);
        Assert.Equal("debit",  result.Transactions[0].Type);
    }

    [Fact]
    public void Parse_MissingPeriodHeader_ThrowsInvalidOperation()
    {
        var badText = "IBAN: PT50001000000000000000001\nNo period here";
        Assert.Throws<InvalidOperationException>(() => _parser.Parse("bpi.pdf", [badText]));
    }

    [Fact]
    public void Parse_StopsParsingTransactionsAtPoupancaMarker()
    {
        var txBlock = """
            01/01 01/01 VALID TX 100,00 1 100,00
            PLANOS DE POUPANÇA
            02/01 02/01 AFTER PPR TX 50,00 1 050,00
            """;

        var fullText = $"""
            IBAN: PT50001000000000000000001
            Período De 01/01/2024 a 31/01/2024
            SALDO ANTERIOR CONTABILISTICO 1 000,00
            SALDO ACTUAL CONTABILISTICO 1 100,00
            DEPÓSITOS À ORDEM
            {txBlock}
            """;

        var result = _parser.Parse("bpi.pdf", [fullText]);

        Assert.Single(result.Transactions);
        Assert.Equal("VALID TX", result.Transactions[0].Description);
    }

    [Fact]
    public void Parse_MultiplePages_AggregatesTransactions()
    {
        var page1 = """
            IBAN: PT50001000000000000000001
            Período De 01/02/2024 a 29/02/2024
            SALDO ANTERIOR CONTABILISTICO 500,00
            SALDO ACTUAL CONTABILISTICO 900,00
            DEPÓSITOS À ORDEM
            01/02 01/02 FIRST TX 200,00 700,00
            """;

        var page2 = """
            02/02 02/02 SECOND TX 200,00 900,00
            PLANOS DE POUPANÇA
            """;

        var result = _parser.Parse("bpi.pdf", [page1, page2]);

        Assert.Equal(2, result.Transactions.Count);
    }
}
