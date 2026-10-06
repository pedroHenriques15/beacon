namespace Beacon.Api.Services.Parsing;

/// <summary>
/// Turns a Mercor <b>USD</b> statement into a EUR <see cref="ParsedSalarySlip"/> at the EUR the owner
/// says it paid (ADR-033). No fee is known, so gross and net are both the EUR received and the slip has
/// no deduction; the month's rate (EUR ÷ USD total) converts Base Pay and the hourly rate, and "Other"
/// absorbs any pay beyond the hourly lines plus the rounding, so the income items sum exactly to gross.
/// </summary>
public static class MercorReconciler
{
    public const string Employer = "Mercor";

    public static ParsedSalarySlip Reconcile(MercorStatement statementUsd, decimal eurReceived)
    {
        if (eurReceived <= 0m)
            throw new InvalidOperationException("The EUR received must be more than zero.");

        var eur = AmountUtils.Round2(eurReceived);
        var rate = eur / statementUsd.TotalPayUsd;

        var lineItems = new List<ParsedSalaryLineItem>();
        decimal basePayEur;
        if (statementUsd.ShiftPayUsd == statementUsd.TotalPayUsd)
        {
            // Hourly pay only: the whole EUR is base pay, so rounding can't leave a stray one-cent "Other".
            basePayEur = eur;
            lineItems.Add(new ParsedSalaryLineItem("Base Pay", basePayEur, "income"));
        }
        else
        {
            basePayEur = AmountUtils.Round2(statementUsd.ShiftPayUsd * rate);
            lineItems.Add(new ParsedSalaryLineItem("Base Pay", basePayEur, "income"));
            lineItems.Add(new ParsedSalaryLineItem("Other", eur - basePayEur, "income"));
        }

        return new ParsedSalarySlip(
            Employer,
            null,
            statementUsd.Period,
            eur,
            eur,
            lineItems,
            BaseAmount: basePayEur,
            HoursWorked: statementUsd.HoursWorked,
            HourlyRate: AmountUtils.Round2(statementUsd.PayRateUsd * rate));
    }
}
