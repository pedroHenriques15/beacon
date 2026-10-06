using Beacon.Api.Services.Parsing;
using static Beacon.Tests.Parsing.TradeRepublicCsv;

namespace Beacon.Tests.Parsing;

public class TradeRepublicCsvParserTests
{
    private readonly TradeRepublicCsvParser _parser = new();

    private ParsedStatement Parse(params string[] rows) => _parser.Parse("export.csv", [File(rows)]);

    // ── Detection ────────────────────────────────────────────────────────────────

    [Fact]
    public void CanParse_TheExportsHeader_IsTrue() =>
        Assert.True(_parser.CanParse(File(Cash("2026-08-02", "CARD_TRANSACTION", "-7.300000", "MINI MERCADO"))));

    [Fact]
    public void CanParse_AByteOrderMarkBeforeTheHeader_IsTrue() =>
        Assert.True(_parser.CanParse("﻿" + File()));

    [Theory]
    [InlineData("TRADE REPUBLIC BANK GMBH, SUCURSAL EM PORTUGAL\nBIC TRBKPTP2XXX")] // the old PDF statement
    [InlineData("\"date\",\"description\",\"amount\"\n\"2026-08-02\",\"SHOP\",\"-1.00\"")]  // another CSV
    [InlineData("\"datetime\",\"date")]                                                    // an open quote
    [InlineData("")]
    public void CanParse_AnythingElse_IsFalse(string text) => Assert.False(_parser.CanParse(text));

    // ── Rows ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_OneRowOfEachType_BecomesOneTransactionEach()
    {
        var parsed = Parse(
            Cash("2026-08-01", "CARD_ORDERING_FEE", "0.000000", "Trade Republic Card", time: "08:00:00", fee: "-5.00"),
            Cash("2026-08-02", "INTEREST_PAYMENT", "3.210000", "Interest payment for payout collection x1"),
            Cash("2026-08-03", "BENEFITS_SAVEBACK", "1.500000", " Saveback cash reward r1 for reservation: r2"),
            Cash("2026-08-04", "CARD_TRANSACTION", "-7.300000", "MINI MERCADO"),
            Cash("2026-08-05", "CARD_TRANSACTION_INTERNATIONAL", "-12.50", "EXAMPLE CAFE"),
            Cash("2026-08-06", "TRANSFER_INSTANT_INBOUND", "250.000000", "Incoming transfer from EXAMPLE PERSON"),
            Cash("2026-08-07", "TRANSFER_INSTANT_OUTBOUND", "-40.000000", "Outgoing transfer for EXAMPLE PERSON"),
            Buy("2026-08-08", "-50.00", "0.300000", "166.6600000000", "trade-1"));

        Assert.Equal(
            [
                ("Trade Republic Card", 5.00m, "debit"),
                ("Interest payment for payout collection x1", 3.21m, "credit"),
                ("Saveback cash reward r1 for reservation: r2", 1.50m, "credit"),
                ("MINI MERCADO", 7.30m, "debit"),
                ("EXAMPLE CAFE", 12.50m, "debit"),
                ("Incoming transfer from EXAMPLE PERSON", 250.00m, "credit"),
                ("Outgoing transfer for EXAMPLE PERSON", 40.00m, "debit"),
                ($"Savings plan execution {Isin} Example World Fund, quantity: 0.500000", 50.00m, "debit"),
            ],
            parsed.Transactions.Select(t => (t.Description, t.Amount, t.Type)));
        Assert.All(parsed.Transactions.SkipLast(1), t => Assert.Null(t.Trade));
        Assert.All(parsed.Transactions, t => Assert.Equal(t.DatePosting, t.DateValue));
        Assert.Equal(new DateOnly(2026, 8, 4), parsed.Transactions[3].DatePosting);
    }

    [Fact]
    public void Parse_ABuy_IsCashOutOfAmountFeeAndTax_AndCarriesItsTrade()
    {
        var parsed = Parse(Buy("2026-08-07", "-299.97", "1.800000", "166.6500000000", "trade-7",
            fee: "-1.00", tax: "-0.02", description: $"Buy trade {Isin} Example World Fund, quantity: 1.800000"));

        var tx = Assert.Single(parsed.Transactions);
        Assert.Equal(300.99m, tx.Amount);
        Assert.Equal("debit", tx.Type);
        var trade = tx.Trade!;
        Assert.Equal(Isin, trade.Isin);
        Assert.Equal("Example World ETF", trade.AssetName);
        Assert.Equal(new DateOnly(2026, 8, 7), trade.Date);
        Assert.Equal(1.8m, trade.Quantity);
        Assert.Equal(166.65m, trade.PricePerUnit);
        Assert.Equal(1.02m, trade.Fees);
        Assert.Equal("trade-7", trade.ExternalId);
        Assert.Equal("Trade Republic buy", trade.Note);
    }

    [Fact]
    public void Parse_ASavingsPlanBuy_IsNotedAsOne_WithNoFees()
    {
        var trade = Parse(Buy("2026-08-03", "-12.50", "0.075000", "166.6600000000", "trade-3"))
            .Transactions.Single().Trade!;

        Assert.Equal(0m, trade.Fees);
        Assert.Equal("Trade Republic savings plan", trade.Note);
    }

    [Fact]
    public void Parse_QuotedCommasAndQuotes_StayInTheDescription()
    {
        var parsed = Parse(Cash("2026-08-04", "CARD_TRANSACTION", "-3.00", "SHOP \"A\", LDA"));

        Assert.Equal("SHOP \"A\", LDA", parsed.Transactions.Single().Description);
    }

    [Fact]
    public void Parse_RowsOutOfOrder_AreSortedByTime_AndBalancesRunInThatOrder()
    {
        var parsed = Parse(
            Cash("2026-08-20", "CARD_TRANSACTION", "-30.00", "THIRD"),
            Cash("2026-08-05", "TRANSFER_INSTANT_INBOUND", "100.00", "FIRST", time: "18:00:00"),
            Cash("2026-08-05", "CARD_TRANSACTION", "-10.00", "SECOND", time: "18:30:00"));

        Assert.Equal(["FIRST", "SECOND", "THIRD"], parsed.Transactions.Select(t => t.Description));
        Assert.Equal([100.00m, 90.00m, 60.00m], parsed.Transactions.Select(t => t.Balance));
    }

    [Fact]
    public void Parse_TheStatement_IsTheCalendarMonth_WithBalancesFromZero()
    {
        var parsed = Parse(
            Cash("2026-07-14", "TRANSFER_INSTANT_INBOUND", "200.00", "IN"),
            Cash("2026-07-21", "CARD_TRANSACTION", "-6.40", "OUT"));

        Assert.Equal("TRADE REPUBLIC", parsed.Bank);
        Assert.Equal(string.Empty, parsed.Account);
        Assert.Equal("EUR", parsed.Currency);
        Assert.Equal(new DateOnly(2026, 7, 1), parsed.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 7, 31), parsed.PeriodTo);
        Assert.True(parsed.BalancesRelative);
        Assert.Equal(0m, parsed.OpeningBalance);
        Assert.Equal(193.60m, parsed.ClosingBalance);
        Assert.Equal("export.csv", parsed.SourceFile);
        Assert.Empty(ParseVerifier.VerifyStatement(parsed));
    }

    [Fact]
    public void Parse_ARowThatMovesNoMoney_IsSkippedAndNamedInTheWarnings()
    {
        var parsed = Parse(
            Cash("2026-08-04", "CARD_TRANSACTION", "-3.00", "SHOP"),
            Cash("2026-08-05", "CARD_TRANSACTION", "0.000000", "VOIDED SHOP"));

        Assert.Single(parsed.Transactions);
        var warning = Assert.Single(parsed.Warnings!);
        Assert.Contains("2026-08-05", warning);
        Assert.Contains("VOIDED SHOP", warning);
    }

    // ── Refusals ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_RowsFromTwoMonths_AreRefused()
    {
        var ex = Assert.Throws<FormatException>(() => Parse(
            Cash("2026-08-31", "CARD_TRANSACTION", "-3.00", "SHOP"),
            Cash("2026-09-01", "CARD_TRANSACTION", "-4.00", "SHOP")));

        Assert.Contains("one file per calendar month", ex.Message);
    }

    [Fact]
    public void Parse_AnUnknownType_IsRefused_NamingItsDateAndType()
    {
        var ex = Assert.Throws<FormatException>(() => Parse(
            Cash("2026-08-12", "CARD_REFUND", "3.00", "SHOP")));

        Assert.Contains("2026-08-12", ex.Message);
        Assert.Contains("CARD_REFUND", ex.Message);
    }

    [Fact]
    public void Parse_AKnownTypeInAnotherCategory_IsRefused() =>
        Assert.Throws<FormatException>(() => Parse(
            Cash("2026-08-12", "BUY", "-3.00", "SHOP", category: "CASH")));

    [Fact]
    public void Parse_ARowNotInEur_IsRefused()
    {
        var ex = Assert.Throws<FormatException>(() => Parse(
            Cash("2026-08-12", "CARD_TRANSACTION", "-3.00", "SHOP", currency: "USD")));

        Assert.Contains("2026-08-12", ex.Message);
        Assert.Contains("CARD_TRANSACTION", ex.Message);
        Assert.Contains("EUR", ex.Message);
    }

    [Fact]
    public void Parse_ARowOfAnotherAccount_IsRefused()
    {
        var ex = Assert.Throws<FormatException>(() => Parse(
            Cash("2026-08-12", "CARD_TRANSACTION", "-3.00", "SHOP", accountType: "CHILD")));

        Assert.Contains("2026-08-12", ex.Message);
        Assert.Contains("CHILD", ex.Message);
    }

    [Fact]
    public void Parse_ABuyOfSomethingOtherThanAFund_IsRefused() =>
        Assert.Throws<FormatException>(() => Parse(
            Buy("2026-08-07", "-100.00", "1.000000", "100.00", "trade-9", assetClass: "STOCK")));

    [Fact]
    public void Parse_AnExportWithoutRows_IsRefused() =>
        Assert.Throws<FormatException>(() => Parse());

    [Fact]
    public void Parse_ARowMissingFields_IsRefused() =>
        Assert.Throws<FormatException>(() => _parser.Parse("export.csv", [Header + "\n\"2026-08-01\",\"x\""]));
}
