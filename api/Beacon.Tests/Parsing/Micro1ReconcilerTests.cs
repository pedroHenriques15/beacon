using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class Micro1ReconcilerTests
{
    private static ParsedSalarySlip InvoiceUsd() => new(
        "Micro1 Inc.", null, new DateOnly(2026, 7, 1),
        GrossAmount: 1600.00m, NetAmount: 1600.00m,
        LineItems:
        [
            new ParsedSalaryLineItem("Base Pay", 1525.00m, "income"),
            new ParsedSalaryLineItem("Other", 75.00m, "income"),
        ],
        BaseAmount: 1525.00m, HoursWorked: 30.50m, HourlyRate: 50m);

    private static DeelWithdrawal Withdrawal() => new(
        SourceAmountUsd: 1600.00m, ExchangeFeeUsd: 12.00m,
        ExchangeRate: 0.87654321m, TotalEur: 1391.95m);

    [Fact]
    public void Reconcile_ProducesEurGrossFromRate()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());
        Assert.Equal(1402.47m, slip.GrossAmount);
    }

    [Fact]
    public void Reconcile_NetIsWithdrawalTotalEur()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());
        Assert.Equal(1391.95m, slip.NetAmount);
    }

    [Fact]
    public void Reconcile_BasePayAndOtherConvertToEur()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());

        var basePay = slip.LineItems.First(i => i.Description == "Base Pay");
        var other = slip.LineItems.First(i => i.Description == "Other");
        Assert.Equal(1336.73m, basePay.Amount);
        Assert.Equal("income", basePay.ItemType);
        // "Other" absorbs rounding so income items sum exactly to gross.
        Assert.Equal(65.74m, other.Amount);
        Assert.Equal("income", other.ItemType);
    }

    [Fact]
    public void Reconcile_BooksExchangeFeeAsEurDeduction()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());

        var fee = slip.LineItems.First(i => i.Description == "Deel exchange fee");
        Assert.Equal(10.52m, fee.Amount);
        Assert.Equal("deduction", fee.ItemType);
    }

    [Fact]
    public void Reconcile_ConvertsHourlyRateKeepsHours()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());
        Assert.Equal(43.83m, slip.HourlyRate);
        Assert.Equal(30.50m, slip.HoursWorked);
        Assert.Equal(1336.73m, slip.BaseAmount);
    }

    [Fact]
    public void Reconcile_PreservesEmployerAndPeriod()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());
        Assert.Equal("Micro1 Inc.", slip.Employer);
        Assert.Equal(new DateOnly(2026, 7, 1), slip.Period);
    }

    [Fact]
    public void Reconcile_PassesParseVerifierWithNoWarnings()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());
        var warnings = ParseVerifier.VerifySalarySlip(slip);
        Assert.Empty(warnings);
    }

    // A base-pay-only invoice (no "Other" earnings) — the whole gross is base pay.
    private static ParsedSalarySlip BasePayOnlyInvoiceUsd() => new(
        "Micro1 Inc.", null, new DateOnly(2026, 6, 1),
        GrossAmount: 360.00m, NetAmount: 360.00m,
        LineItems: [new ParsedSalaryLineItem("Base Pay", 360.00m, "income")],
        BaseAmount: 360.00m, HoursWorked: 7.20m, HourlyRate: 50m);

    private static DeelWithdrawal BasePayOnlyWithdrawal() => new(
        SourceAmountUsd: 360.00m, ExchangeFeeUsd: 2.40m,
        ExchangeRate: 0.87654321m, TotalEur: 313.45m);

    [Fact]
    public void Reconcile_BasePayOnlyInvoice_EmitsNoOtherLineItem()
    {
        var slip = Micro1Reconciler.Reconcile(BasePayOnlyInvoiceUsd(), BasePayOnlyWithdrawal());

        Assert.DoesNotContain(slip.LineItems, i => i.Description == "Other");
        var basePay = slip.LineItems.First(i => i.Description == "Base Pay");
        Assert.Equal(slip.GrossAmount, basePay.Amount);
        Assert.Equal(slip.GrossAmount, slip.BaseAmount);
    }

    [Fact]
    public void Reconcile_BasePayOnlyInvoice_PassesParseVerifierWithNoWarnings()
    {
        var slip = Micro1Reconciler.Reconcile(BasePayOnlyInvoiceUsd(), BasePayOnlyWithdrawal());
        Assert.Empty(ParseVerifier.VerifySalarySlip(slip));
    }
}
