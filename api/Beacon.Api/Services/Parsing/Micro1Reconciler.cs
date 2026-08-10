namespace Beacon.Api.Services.Parsing;

/// <summary>
/// Combines a micro1 <b>USD</b> invoice with its Deel <b>EUR</b> withdrawal into a single EUR
/// <see cref="ParsedSalarySlip"/>. USD figures are converted at Deel's real exchange rate; the net is
/// the EUR that actually reached the bank ("Total sent"); the Deel exchange fee is booked as a EUR
/// deduction so <see cref="ParseVerifier.VerifySalarySlip"/> reconciles (income − deductions = net).
/// </summary>
public static class Micro1Reconciler
{
    public static ParsedSalarySlip Reconcile(ParsedSalarySlip invoiceUsd, DeelWithdrawal withdrawal)
    {
        var rate = withdrawal.ExchangeRate;

        var netEur = withdrawal.TotalEur;
        var feeEur = AmountUtils.Round2(withdrawal.ExchangeFeeUsd * rate);
        var grossEur = netEur + feeEur;

        var basePayEur = AmountUtils.Round2((invoiceUsd.BaseAmount ?? 0m) * rate);
        var otherEur   = grossEur - basePayEur;

        var hourlyRateEur = invoiceUsd.HourlyRate is { } r ? AmountUtils.Round2(r * rate) : (decimal?)null;

        var lineItems = new List<ParsedSalaryLineItem>
        {
            new("Base Pay", basePayEur, "income"),
            new("Other", otherEur, "income"),
            new("Deel exchange fee", feeEur, "deduction"),
        };

        return new ParsedSalarySlip(
            invoiceUsd.Employer,
            invoiceUsd.EmployerNif,
            invoiceUsd.Period,
            grossEur,
            netEur,
            lineItems,
            BaseAmount: basePayEur,
            HoursWorked: invoiceUsd.HoursWorked,
            HourlyRate: hourlyRateEur);
    }
}
