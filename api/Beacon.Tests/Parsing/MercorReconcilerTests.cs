using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class MercorReconcilerTests
{
    private static MercorStatement HourlyOnly() => new(
        new DateOnly(2026, 8, 1),
        TotalPayUsd: 145.17m, ShiftPayUsd: 145.17m, HoursWorked: 3.63m, PayRateUsd: 40.00m);

    private static MercorStatement WithOtherPay() => HourlyOnly() with { TotalPayUsd = 160.17m };

    [Fact]
    public void Reconcile_GrossAndNetAreTheEurReceived()
    {
        var slip = MercorReconciler.Reconcile(HourlyOnly(), 124.50m);

        Assert.Equal(124.50m, slip.GrossAmount);
        Assert.Equal(124.50m, slip.NetAmount);
        Assert.DoesNotContain(slip.LineItems, i => i.ItemType != "income");
    }

    [Fact]
    public void Reconcile_HourlyOnly_IsAllBasePay()
    {
        var slip = MercorReconciler.Reconcile(HourlyOnly(), 124.50m);

        var line = Assert.Single(slip.LineItems);
        Assert.Equal("Base Pay", line.Description);
        Assert.Equal(124.50m, line.Amount);
        Assert.Equal(124.50m, slip.BaseAmount);
    }

    [Fact]
    public void Reconcile_PayBeyondTheHourlyLines_IsOther_AndAbsorbsTheRounding()
    {
        var slip = MercorReconciler.Reconcile(WithOtherPay(), 137.40m);

        // 145.17 × (137.40 ÷ 160.17) = 124.5324…
        Assert.Equal(124.53m, slip.LineItems.Single(i => i.Description == "Base Pay").Amount);
        Assert.Equal(12.87m, slip.LineItems.Single(i => i.Description == "Other").Amount);
        Assert.Equal(slip.GrossAmount, slip.LineItems.Sum(i => i.Amount));
    }

    [Fact]
    public void Reconcile_ConvertsThePayRate_AndKeepsTheHours()
    {
        var slip = MercorReconciler.Reconcile(HourlyOnly(), 124.50m);

        // 40 × (124.50 ÷ 145.17) = 34.3046…
        Assert.Equal(34.30m, slip.HourlyRate);
        Assert.Equal(3.63m, slip.HoursWorked);
    }

    [Fact]
    public void Reconcile_IsAMercorSlipForTheStatementMonth()
    {
        var slip = MercorReconciler.Reconcile(HourlyOnly(), 124.50m);

        Assert.Equal("Mercor", slip.Employer);
        Assert.Null(slip.EmployerNif);
        Assert.Equal(new DateOnly(2026, 8, 1), slip.Period);
    }

    [Fact]
    public void Reconcile_PassesParseVerifierWithNoWarnings()
    {
        foreach (var eur in new[] { 124.50m, 137.40m, 0.01m, 9999.99m })
        {
            Assert.Empty(ParseVerifier.VerifySalarySlip(MercorReconciler.Reconcile(HourlyOnly(), eur)));
            Assert.Empty(ParseVerifier.VerifySalarySlip(MercorReconciler.Reconcile(WithOtherPay(), eur)));
        }
    }

    [Fact]
    public void Reconcile_RefusesNoEur()
    {
        Assert.Throws<InvalidOperationException>(() => MercorReconciler.Reconcile(HourlyOnly(), 0m));
        Assert.Throws<InvalidOperationException>(() => MercorReconciler.Reconcile(HourlyOnly(), -5m));
    }
}
