using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class TradeRepublicParserTests
{
    private readonly TradeRepublicParser _parser = new();

    private static readonly string[] SamplePages =
    [
        """
        TRADE REPUBLIC BANK GMBH, SUCURSAL EM PORTUGAL AV. DA REPUBLICA 50, 1050-196 LISBOA, PORTUGAL
        TEST USER DATE 01 Aug 2026 - 05 Aug 2026
        Rua Example 1 IBAN PT50000000000000000000000
        1000-000 City BIC TRBKPTP2XXX
        ACCOUNT STATEMENT SUMMARY
        PRODUCT OPENING BALANCE MONEY IN MONEY OUT ENDING BALANCE
        Checking Account €1,000.00 €120.00 €57.30 €1,062.70
        ACCOUNT TRANSACTIONS
        DATE TYPE DESCRIPTION MONEY IN M OU O T NEY BALANCE
        01 Aug
        Interest Interest payment €5.00 €1,005.00
        2026
        02 Aug Card
        MINI MERCADO €7.30 €997.70
        2026 Transaction
        Trade Republic Bank GmbH www.traderepublic.pt Headquarters: Trade Republic Bank GmbH Directors
        Generated on 2026-08-06 12:22:27 Europe/Lisbon (UTC+01:00) Page 1 of 2
        """,
        """
        TRADE REPUBLIC BANK GMBH, SUCURSAL EM PORTUGAL AV. DA REPUBLICA 50, 1050-196 LISBOA, PORTUGAL
        DATE TYPE DESCRIPTION MONEY IN M OU O T NEY BALANCE
        03 Aug Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All-
        Trade €50.00€947.70
        2026 World UCITS ETF (USD) Accumulating, quantity: 0.303000
        04 Aug
        Transfer Incoming transfer from Example Sender (PT50111111111111111111111) €115.00 €1,062.70
        2026
        BALANCE OVERVIEW
        as of 05 Aug 2026
        ESCROW ACCOUNTS BALANCE
        HSBC €1,062.70
        MONEY MARKET FUNDS ISIN UNITS PRICE PER UNIT MARKET VALUE IN EUR
        BNP Paribas InstiCash Euro Liquidity LU3041245716 0.00 €1.00 €0.00
        Generated on 2026-08-06 12:22:27 Europe/Lisbon (UTC+01:00) Page 2 of 2
        """
    ];

    [Theory]
    [InlineData("TRBKPTP2")]
    [InlineData("TRADE REPUBLIC BANK GMBH")]
    public void CanParse_ReturnsTrueForKnownSignals(string signal)
    {
        Assert.True(_parser.CanParse($"some text {signal} more text"));
    }

    [Fact]
    public void CanParse_ReturnsFalseForUnrelatedText()
    {
        Assert.False(_parser.CanParse("ACTVPTPL ActivoBank BBPIPTPL REVOPTP2"));
    }

    [Fact]
    public void BankName_IsTradeRepublic()
    {
        Assert.Equal("TRADE REPUBLIC", _parser.BankName);
    }

    [Fact]
    public void Parse_ExtractsHeaderMetadata()
    {
        var result = _parser.Parse("statement.pdf", SamplePages);

        Assert.Equal("TRADE REPUBLIC", result.Bank);
        Assert.Equal("PT50000000000000000000000", result.Account);
        Assert.Equal("EUR", result.Currency);
        Assert.Equal(new DateOnly(2026, 8, 1), result.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 8, 5), result.PeriodTo);
        Assert.Equal(1000.00m, result.OpeningBalance);
        Assert.Equal(1062.70m, result.ClosingBalance);
    }

    [Fact]
    public void Parse_ExtractsAllTransactions_AndStopsAtBalanceOverview()
    {
        var result = _parser.Parse("statement.pdf", SamplePages);

        Assert.Equal(4, result.Transactions.Count);
    }

    [Fact]
    public void Parse_ClassifiesCreditFromBalanceDelta()
    {
        var result = _parser.Parse("statement.pdf", SamplePages);

        var interest = result.Transactions[0];
        Assert.Contains("Interest payment", interest.Description);
        Assert.Equal(5.00m, interest.Amount);
        Assert.Equal("credit", interest.Type);
        Assert.Equal(1005.00m, interest.Balance);
        Assert.Equal(new DateOnly(2026, 8, 1), interest.DatePosting);
    }

    [Fact]
    public void Parse_ClassifiesDebit_AndKeepsMerchantInDescription()
    {
        var result = _parser.Parse("statement.pdf", SamplePages);

        var card = result.Transactions[1];
        Assert.Contains("MINI MERCADO", card.Description);
        Assert.Equal(7.30m, card.Amount);
        Assert.Equal("debit", card.Type);
        Assert.Equal(997.70m, card.Balance);
    }

    [Fact]
    public void Parse_HandlesNoSpaceMoneyOut_OnWrappedSavingsPlanRow()
    {
        var result = _parser.Parse("statement.pdf", SamplePages);

        var savings = result.Transactions[2];
        Assert.Equal(50.00m, savings.Amount);
        Assert.Equal(947.70m, savings.Balance);
        Assert.Equal("debit", savings.Type);
        Assert.Contains("Savings plan execution", savings.Description);
        Assert.Contains("IE00BK5BQT80", savings.Description);
    }

    [Fact]
    public void Parse_ParsesWrappedIncomingTransfer()
    {
        var result = _parser.Parse("statement.pdf", SamplePages);

        var transfer = result.Transactions[3];
        Assert.Contains("Incoming transfer", transfer.Description);
        Assert.Equal(115.00m, transfer.Amount);
        Assert.Equal("credit", transfer.Type);
        Assert.Equal(new DateOnly(2026, 8, 4), transfer.DatePosting);
    }

    [Fact]
    public void Parse_ReconciledStatement_ProducesNoVerifierWarnings()
    {
        var result   = _parser.Parse("statement.pdf", SamplePages);
        var warnings = ParseVerifier.VerifyStatement(result);
        Assert.Empty(warnings);
    }
}
