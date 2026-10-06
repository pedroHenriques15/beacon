using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

/// <summary>
/// Reads Mercor's monthly "Line Item Statement", a one-page PDF in <b>USD</b> that says nothing about
/// the EUR that reached the bank. Plain class (not an <see cref="ISalarySlipParser"/>): a statement is
/// never a EUR slip on its own, so it is converted by <see cref="MercorReconciler"/> once the owner
/// gives the EUR received (ADR-033).
/// </summary>
public partial class MercorStatementParser
{
    public bool CanParse(string fullText) => fullText.Contains("Mercor Line Item Statement");

    public MercorStatement Parse(IReadOnlyList<string> pages)
    {
        var fullText = string.Join("\n", pages);

        var period = ExtractPeriod(fullText);
        var totalPay = AmountUtils.ParseUsd(Require(TotalPayRegex(), fullText, "Total Pay"));
        var shiftPay = AmountUtils.ParseUsd(Require(ShiftPayRegex(), fullText, "Total Shift Pay"));

        // The lines come in no date order, and each carries a doubled "$$" before its rate and amount.
        var lines = HourlyLineRegex().Matches(fullText)
            .Select(m => (Rate: AmountUtils.ParseUsd(m.Groups[1].Value), Amount: AmountUtils.ParseUsd(m.Groups[2].Value)))
            .ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("Could not find any hourly payment in Mercor statement.");
        if (lines.Any(l => l.Rate <= 0m))
            throw new InvalidOperationException("A Mercor hourly payment has no pay rate.");

        var linesSum = lines.Sum(l => l.Amount);
        if (linesSum != shiftPay)
            throw new InvalidOperationException(
                $"The hourly payments in this Mercor statement sum to ${linesSum:F2}, but its Total Shift Pay is ${shiftPay:F2}.");
        if (totalPay < shiftPay)
            throw new InvalidOperationException(
                $"This Mercor statement's Total Pay (${totalPay:F2}) is less than its Total Shift Pay (${shiftPay:F2}).");

        // HOURS WORKED is cut to the minute (a few cents of pay can show 00:00), so hours come from
        // what each line paid at its rate.
        var hours = lines.Sum(l => l.Amount / l.Rate);
        if (hours <= 0m)
            throw new InvalidOperationException("This Mercor statement has no hours worked.");

        return new MercorStatement(
            period,
            TotalPayUsd: totalPay,
            ShiftPayUsd: shiftPay,
            HoursWorked: AmountUtils.Round2(hours),
            // One rate for every line gives that rate back; mixed rates give their average by hours.
            PayRateUsd: AmountUtils.Round2(shiftPay / hours));
    }

    private static DateOnly ExtractPeriod(string fullText)
    {
        var m = PeriodRegex().Match(fullText);
        if (!m.Success
            || !DateOnly.TryParseExact(m.Groups[1].Value, "MMMM d, yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start))
            throw new InvalidOperationException("Could not find 'Statement Period' in Mercor statement.");
        return new DateOnly(start.Year, start.Month, 1);
    }

    private static string Require(Regex regex, string fullText, string label)
    {
        var m = regex.Match(fullText);
        if (!m.Success)
            throw new InvalidOperationException($"Could not find '{label}' in Mercor statement.");
        return m.Groups[1].Value;
    }

    [GeneratedRegex(@"Statement Period:\s*([A-Za-z]+ \d{1,2}, \d{4})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"Total Pay\s+\$+([\d,]+\.\d{2})")]
    private static partial Regex TotalPayRegex();

    [GeneratedRegex(@"Total Shift Pay\s+\$+([\d,]+\.\d{2})")]
    private static partial Regex ShiftPayRegex();

    // "... $$40.00 02:30 $$100.00": pay rate, hours worked, amount paid, at the end of the line.
    [GeneratedRegex(@"(?m)\$+([\d,]+(?:\.\d+)?)\s+\d+:\d{2}\s+\$+([\d,]+\.\d{2})\s*$")]
    private static partial Regex HourlyLineRegex();
}

/// <summary>A Mercor month in USD, as its statement gives it.</summary>
public record MercorStatement(
    DateOnly Period,
    decimal TotalPayUsd,
    decimal ShiftPayUsd,
    decimal HoursWorked,
    decimal PayRateUsd);
