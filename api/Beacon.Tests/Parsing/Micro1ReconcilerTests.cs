using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class Micro1ReconcilerTests
{
    private static ParsedSalarySlip InvoiceUsd() => new(
        "Micro1 Inc.", null, new DateOnly(2026, 7, 1),
        GrossAmount: 1541.50m, NetAmount: 1541.50m,
        LineItems:
        [
            new ParsedSalaryLineItem("Base Pay", 1436.50m, "income"),
            new ParsedSalaryLineItem("Other", 105.00m, "income"),
        ],
        BaseAmount: 1436.50m, HoursWorked: 28.73m, HourlyRate: 50m);

    private static DeelWithdrawal Withdrawal() => new(
        SourceAmountUsd: 1541.50m, ExchangeFeeUsd: 10.79m,
        ExchangeRate: 0.86788828m, TotalEur: 1328.49m);

    [Fact]
    public void Reconcile_ProducesEurGrossFromRate()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());
        Assert.Equal(1337.85m, slip.GrossAmount);
    }

    [Fact]
    public void Reconcile_NetIsWithdrawalTotalEur()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());
        Assert.Equal(1328.49m, slip.NetAmount);
    }

    [Fact]
    public void Reconcile_BasePayAndOtherConvertToEur()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());

        var basePay = slip.LineItems.First(i => i.Description == "Base Pay");
        var other   = slip.LineItems.First(i => i.Description == "Other");
        Assert.Equal(1246.72m, basePay.Amount);
        Assert.Equal("income", basePay.ItemType);
        // "Other" absorbs rounding so income items sum exactly to gross.
        Assert.Equal(91.13m, other.Amount);
        Assert.Equal("income", other.ItemType);
    }

    [Fact]
    public void Reconcile_BooksExchangeFeeAsEurDeduction()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());

        var fee = slip.LineItems.First(i => i.Description == "Deel exchange fee");
        Assert.Equal(9.36m, fee.Amount);
        Assert.Equal("deduction", fee.ItemType);
    }

    [Fact]
    public void Reconcile_ConvertsHourlyRateKeepsHours()
    {
        var slip = Micro1Reconciler.Reconcile(InvoiceUsd(), Withdrawal());
        Assert.Equal(43.39m, slip.HourlyRate);
        Assert.Equal(28.73m, slip.HoursWorked);
        Assert.Equal(1246.72m, slip.BaseAmount);
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
}
