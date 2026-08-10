using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

/// <summary>
/// Parses a micro1 (Deel) invoice PDF. The invoice is denominated in <b>USD</b>, so the
/// <see cref="ParsedSalarySlip"/> it returns carries USD figures — it is <b>not</b> a complete
/// EUR salary slip on its own. It is deliberately <b>not</b> registered in
/// <see cref="SalarySlipParserFactory"/>: a lone invoice must never be imported as if it were EUR.
/// It is paired with a <see cref="DeelWithdrawal"/> and converted to EUR by <see cref="Micro1Reconciler"/>
/// inside the unified upload flow.
/// </summary>
public partial class Micro1InvoiceParser : ISalarySlipParser
{
    public string ParserName => "Micro1";

    public bool CanParse(string fullText) =>
        fullText.Contains("Micro1 Inc.") && fullText.Contains("Total USD");

    public ParsedSalarySlip Parse(string fileName, IReadOnlyList<string> pages)
    {
        var fullText = string.Join("\n", pages);

        var period    = ExtractPeriod(fullText);
        var totalUsd  = ExtractTotal(fullText);
        var basePay   = ExtractSummaryValue(BasePayRegex(), fullText, "Base Pay");
        var other     = ExtractSummaryValue(OtherRegex(), fullText, "Other");
        var hours     = ExtractSummaryValue(HoursRegex(), fullText, "Hours");
        var payRate   = ExtractSummaryValue(PayRateRegex(), fullText, "Pay Rate");

        var lineItems = new List<ParsedSalaryLineItem>
        {
            new("Base Pay", basePay, "income"),
            new("Other", other, "income"),
        };

        return new ParsedSalarySlip(
            "Micro1 Inc.", null, period, totalUsd, totalUsd, lineItems,
            BaseAmount: basePay,
            HoursWorked: hours,
            HourlyRate: payRate);
    }

    private static DateOnly ExtractPeriod(string fullText)
    {
        var m = PeriodRegex().Match(fullText);
        if (!m.Success)
            throw new InvalidOperationException("Could not find work period ('between …') in micro1 invoice.");

        var month = EnMonthToNumber(m.Groups[1].Value.ToLowerInvariant());
        var year  = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return new DateOnly(year, month, 1);
    }

    private static decimal ExtractTotal(string fullText)
    {
        var m = TotalRegex().Match(fullText);
        if (!m.Success)
            throw new InvalidOperationException("Could not find 'Total USD' in micro1 invoice.");
        return ParseUs(m.Groups[1].Value);
    }

    private static decimal ExtractSummaryValue(Regex regex, string fullText, string label)
    {
        var m = regex.Match(fullText);
        if (!m.Success)
            throw new InvalidOperationException($"Could not find '{label}' in micro1 invoice summary line.");
        return ParseUs(m.Groups[1].Value);
    }

    public static decimal ParseUs(string s) => AmountUtils.ParseUsd(s);

    private static int EnMonthToNumber(string month) => month switch
    {
        "january"   => 1,
        "february"  => 2,
        "march"     => 3,
        "april"     => 4,
        "may"       => 5,
        "june"      => 6,
        "july"      => 7,
        "august"    => 8,
        "september" => 9,
        "october"   => 10,
        "november"  => 11,
        "december"  => 12,
        _ => throw new InvalidOperationException($"Unknown English month: '{month}'")
    };

    [GeneratedRegex(@"between\s+([A-Za-z]+)\s+\d{1,2},\s*(\d{4})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"Total USD\s*\$?([\d,]+\.\d{2})")]
    private static partial Regex TotalRegex();

    [GeneratedRegex(@"Hours\s*[→>:\-]\s*([\d,]+(?:\.\d+)?)")]
    private static partial Regex HoursRegex();

    [GeneratedRegex(@"Pay Rate\s*[→>:\-]\s*\$([\d,]+(?:\.\d+)?)")]
    private static partial Regex PayRateRegex();

    [GeneratedRegex(@"Base Pay\s*[→>:\-]\s*\$([\d,]+(?:\.\d+)?)")]
    private static partial Regex BasePayRegex();

    [GeneratedRegex(@"\|\s*Other\s*[→>:\-]\s*\$([\d,]+(?:\.\d+)?)")]
    private static partial Regex OtherRegex();
}
