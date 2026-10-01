using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Services;

public class ParseVerifierTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ParsedTransaction Tx(string type, decimal amount) =>
        new(DateOnly.MinValue, DateOnly.MinValue, "desc", amount, type, 0m);

    private static ParsedSalaryLineItem LineItem(string type, decimal amount) =>
        new("desc", amount, type);

    private static ParsedGroceryItem Item(decimal amount) =>
        new("item", amount, 1m);

    // ── VerifyStatement ──────────────────────────────────────────────────────

    [Fact]
    public void VerifyStatement_BalancedStatement_ReturnsNoWarnings()
    {
        var statement = new ParsedStatement(
            "BPI", null,
            DateOnly.MinValue, DateOnly.MinValue,
            "EUR", 100m, 150m, "file.pdf",
            [Tx("credit", 70m), Tx("debit", 20m)]);

        Assert.Empty(ParseVerifier.VerifyStatement(statement));
    }

    [Fact]
    public void VerifyStatement_BalanceMismatchBeyondThreshold_ReturnsWarning()
    {
        // opening 100 + credit 70 - debit 20 = 150, but closing is 149.97 (diff 0.03)
        var statement = new ParsedStatement(
            "BPI", null,
            DateOnly.MinValue, DateOnly.MinValue,
            "EUR", 100m, 149.97m, "file.pdf",
            [Tx("credit", 70m), Tx("debit", 20m)]);

        var warnings = ParseVerifier.VerifyStatement(statement);
        Assert.Single(warnings);
        Assert.Contains("Balance mismatch", warnings[0]);
    }

    [Fact]
    public void VerifyStatement_BalanceMismatchExactlyAtThreshold_ReturnsNoWarning()
    {
        // diff == 0.01 exactly → not strictly greater than 0.01 → no warning
        var statement = new ParsedStatement(
            "BPI", null,
            DateOnly.MinValue, DateOnly.MinValue,
            "EUR", 100m, 149.99m, "file.pdf",
            [Tx("credit", 70m), Tx("debit", 20m)]);

        Assert.Empty(ParseVerifier.VerifyStatement(statement));
    }

    [Fact]
    public void VerifyStatement_NoTransactions_ReturnsWarning()
    {
        var statement = new ParsedStatement(
            "BPI", null,
            DateOnly.MinValue, DateOnly.MinValue,
            "EUR", 100m, 100m, "file.pdf",
            []);

        var warnings = ParseVerifier.VerifyStatement(statement);
        Assert.Single(warnings);
        Assert.Contains("No transactions", warnings[0]);
    }

    [Fact]
    public void VerifyStatement_NoTransactions_DoesNotAlsoWarnAboutBalance()
    {
        // Even if balances differ, the no-transactions warning should be the only one.
        var statement = new ParsedStatement(
            "BPI", null,
            DateOnly.MinValue, DateOnly.MinValue,
            "EUR", 100m, 999m, "file.pdf",
            []);

        Assert.Single(ParseVerifier.VerifyStatement(statement));
    }

    [Fact]
    public void VerifyStatement_UnknownTypeWithSignificantAmount_ReturnsWarning()
    {
        var statement = new ParsedStatement(
            "BPI", null,
            DateOnly.MinValue, DateOnly.MinValue,
            "EUR", 100m, 100m, "file.pdf",
            [Tx("unknown", 50m)]);

        var warnings = ParseVerifier.VerifyStatement(statement);
        Assert.Contains(warnings, w => w.Contains("could not be classified"));
    }

    [Fact]
    public void VerifyStatement_UnknownTypeWithZeroAmount_ReturnsNoUnknownWarning()
    {
        var statement = new ParsedStatement(
            "BPI", null,
            DateOnly.MinValue, DateOnly.MinValue,
            "EUR", 100m, 100m, "file.pdf",
            [Tx("unknown", 0.00m)]);

        Assert.DoesNotContain(ParseVerifier.VerifyStatement(statement), w => w.Contains("could not be classified"));
    }

    // ── VerifySalarySlip ─────────────────────────────────────────────────────

    [Fact]
    public void VerifySalarySlip_MatchingAmounts_ReturnsNoWarnings()
    {
        var slip = new ParsedSalarySlip(
            "Employer", null,
            DateOnly.MinValue,
            GrossAmount: 2000m, NetAmount: 1500m,
            LineItems:
            [
                LineItem("income", 2000m),
                LineItem("deduction", 300m),
                LineItem("tax", 200m)
            ]);

        Assert.Empty(ParseVerifier.VerifySalarySlip(slip));
    }

    [Fact]
    public void VerifySalarySlip_GrossMismatch_ReturnsWarning()
    {
        var slip = new ParsedSalarySlip(
            "Employer", null,
            DateOnly.MinValue,
            GrossAmount: 2000m, NetAmount: 1500m,
            LineItems:
            [
                LineItem("income", 1950m),   // 50 less than gross
                LineItem("deduction", 200m),
                LineItem("tax", 250m)
            ]);

        var warnings = ParseVerifier.VerifySalarySlip(slip);
        Assert.Contains(warnings, w => w.Contains("Gross amount mismatch"));
    }

    [Fact]
    public void VerifySalarySlip_NetMismatch_ReturnsWarning()
    {
        // income matches gross, but net is wrong
        var slip = new ParsedSalarySlip(
            "Employer", null,
            DateOnly.MinValue,
            GrossAmount: 2000m, NetAmount: 1600m,
            LineItems:
            [
                LineItem("income", 2000m),
                LineItem("deduction", 300m),
                LineItem("tax", 200m)
            ]);

        var warnings = ParseVerifier.VerifySalarySlip(slip);
        Assert.Contains(warnings, w => w.Contains("Net amount mismatch"));
    }

    [Fact]
    public void VerifySalarySlip_BothGrossAndNetMismatch_ReturnsTwoWarnings()
    {
        var slip = new ParsedSalarySlip(
            "Employer", null,
            DateOnly.MinValue,
            GrossAmount: 2000m, NetAmount: 1600m,
            LineItems:
            [
                LineItem("income", 1900m),  // gross mismatch
                LineItem("deduction", 300m),
                LineItem("tax", 200m)        // computed net = 1400, stated = 1600 → mismatch
            ]);

        var warnings = ParseVerifier.VerifySalarySlip(slip);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void VerifySalarySlip_NoLineItems_ReturnsSingleWarning()
    {
        var slip = new ParsedSalarySlip(
            "Employer", null,
            DateOnly.MinValue,
            GrossAmount: 2000m, NetAmount: 1500m,
            LineItems: []);

        var warnings = ParseVerifier.VerifySalarySlip(slip);
        Assert.Single(warnings);
        Assert.Contains("No line items", warnings[0]);
    }

    [Fact]
    public void VerifySalarySlip_MismatchWithinThreshold_ReturnsNoWarning()
    {
        // net computed = 1499.99, stated = 1500.00 → diff 0.01 → no warning
        var slip = new ParsedSalarySlip(
            "Employer", null,
            DateOnly.MinValue,
            GrossAmount: 2000m, NetAmount: 1500m,
            LineItems:
            [
                LineItem("income", 2000m),
                LineItem("deduction", 300.01m),
                LineItem("tax", 200m)
            ]);

        Assert.Empty(ParseVerifier.VerifySalarySlip(slip));
    }

    // ── VerifyGroceryReceipt ─────────────────────────────────────────────────

    [Fact]
    public void VerifyGroceryReceipt_SumMatchesTotal_ReturnsNoWarnings()
    {
        var receipt = new ParsedGroceryReceipt(
            "Continente", DateOnly.MinValue, 30m,
            [Item(10m), Item(20m)]);

        Assert.Empty(ParseVerifier.VerifyGroceryReceipt(receipt));
    }

    [Fact]
    public void VerifyGroceryReceipt_SumMismatch_ReturnsWarning()
    {
        var receipt = new ParsedGroceryReceipt(
            "Continente", DateOnly.MinValue, 35m,
            [Item(10m), Item(20m)]);   // sum 30, total 35 → diff 5

        var warnings = ParseVerifier.VerifyGroceryReceipt(receipt);
        Assert.Single(warnings);
        Assert.Contains("Total mismatch", warnings[0]);
    }

    [Fact]
    public void VerifyGroceryReceipt_MismatchExactlyAtThreshold_ReturnsNoWarning()
    {
        // diff == 0.01 → not strictly greater → no warning
        var receipt = new ParsedGroceryReceipt(
            "Continente", DateOnly.MinValue, 30.01m,
            [Item(10m), Item(20m)]);

        Assert.Empty(ParseVerifier.VerifyGroceryReceipt(receipt));
    }

    [Fact]
    public void VerifyGroceryReceipt_MismatchJustOverThreshold_ReturnsWarning()
    {
        // diff == 0.02 → warning
        var receipt = new ParsedGroceryReceipt(
            "Continente", DateOnly.MinValue, 30.02m,
            [Item(10m), Item(20m)]);

        Assert.Single(ParseVerifier.VerifyGroceryReceipt(receipt));
    }

    [Fact]
    public void VerifyGroceryReceipt_NoItems_ReturnsSingleWarning()
    {
        var receipt = new ParsedGroceryReceipt(
            "Continente", DateOnly.MinValue, 30m, []);

        var warnings = ParseVerifier.VerifyGroceryReceipt(receipt);
        Assert.Single(warnings);
        Assert.Contains("No items", warnings[0]);
    }

    [Fact]
    public void VerifyGroceryReceipt_NoItems_DoesNotAlsoWarnAboutTotal()
    {
        var receipt = new ParsedGroceryReceipt(
            "Continente", DateOnly.MinValue, 99m, []);

        Assert.Single(ParseVerifier.VerifyGroceryReceipt(receipt));
    }
}
