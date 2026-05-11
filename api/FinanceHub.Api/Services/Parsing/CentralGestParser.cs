using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceHub.Api.Services.Parsing;

public partial class CentralGestParser : ISalarySlipParser
{
    public string ParserName => "CentralGest";

    public bool CanParse(string fullText) =>
        fullText.Contains("CentralGest Software");

    public ParsedSalarySlip Parse(string fileName, IReadOnlyList<string> pages)
    {
        var fullText = string.Join("\n", pages);

        var employer    = ExtractEmployer(fullText);
        var employerNif = ExtractEmployerNif(fullText);
        var period      = ExtractPeriod(fullText);
        var (gross, net, totalEspecie) = ExtractTotals(fullText);
        var baseAmount  = ExtractBaseAmount(fullText);
        var hourlyRate  = ExtractHourlyRate(fullText);
        var lineItems   = ExtractLineItems(fullText);

        return new ParsedSalarySlip(
            employer, employerNif, period, gross, net, lineItems,
            BaseAmount: baseAmount,
            HoursWorked: CountWeekdayHours(period),
            HourlyRate: hourlyRate,
            TotalEspecie: totalEspecie);
    }

    private static string ExtractEmployer(string fullText)
    {
        var firstLine = fullText.Split('\n')
            .Select(l => l.Trim())
            .First(l => l.Length > 10);

        var m = DuplicateLineRegex().Match(firstLine);
        return m.Success ? m.Groups[1].Value.Trim() : firstLine;
    }

    private static string? ExtractEmployerNif(string fullText)
    {
        var m = ContribuinteRegex().Match(fullText);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static DateOnly ExtractPeriod(string fullText)
    {
        var m = PeriodRegex().Match(fullText);
        if (!m.Success)
            throw new InvalidOperationException("Could not find period (Mês:) in CentralGest payslip.");

        var month = PtMonthToNumber(m.Groups[1].Value.ToLowerInvariant());
        var year  = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return new DateOnly(year, month, 1);
    }

    private static (decimal Gross, decimal Net, decimal? TotalEspecie) ExtractTotals(string fullText)
    {
        var m = TotalsRegex().Match(fullText);
        if (!m.Success)
            throw new InvalidOperationException("Could not find gross/net totals in CentralGest payslip.");

        var gross      = ParseUs(m.Groups[1].Value);
        var totalAPagar = ParsePt(m.Groups[5].Value);
        decimal? especie = m.Groups[4].Success ? ParsePt(m.Groups[4].Value) : null;

        return (gross, totalAPagar, especie);
    }

    private static decimal? ExtractBaseAmount(string fullText)
    {
        var m = VencimentoRegex().Match(fullText);
        return m.Success ? ParsePt(m.Groups[1].Value) : null;
    }

    private static decimal CountWeekdayHours(DateOnly period)
    {
        var daysInMonth = DateTime.DaysInMonth(period.Year, period.Month);
        var count = 0;
        for (var day = 1; day <= daysInMonth; day++)
        {
            var dow = new DateOnly(period.Year, period.Month, day).DayOfWeek;
            if (dow is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                count++;
        }
        return count * 8m;
    }

    private static decimal? ExtractHourlyRate(string fullText)
    {
        var m = HourlyRateRegex().Match(fullText);
        return m.Success ? ParseUs(m.Groups[1].Value) : null;
    }

    private static List<ParsedSalaryLineItem> ExtractLineItems(string fullText)
    {
        var items = new List<ParsedSalaryLineItem>();

        var vm = VencimentoRegex().Match(fullText);
        if (vm.Success)
            items.Add(new ParsedSalaryLineItem("Vencimento", ParsePt(vm.Groups[1].Value), "income"));

        var ppr = PprRegex().Match(fullText);
        if (ppr.Success)
            items.Add(new ParsedSalaryLineItem(
                "PPR – Poupança Reforma",
                ParsePt(ppr.Groups[3].Value),
                "income",
                Quantity: ParseUs(ppr.Groups[1].Value),
                UnitValue: ParseUs(ppr.Groups[2].Value)));

        var tr = TicketsRegex().Match(fullText);
        if (tr.Success)
            items.Add(new ParsedSalaryLineItem(
                "Tickets Refeição",
                ParsePt(tr.Groups[3].Value),
                "income",
                Quantity: ParseUs(tr.Groups[1].Value),
                UnitValue: ParseUs(tr.Groups[2].Value)));

        var ss = SegSocialRegex().Match(fullText);
        if (ss.Success)
            items.Add(new ParsedSalaryLineItem(
                "Segurança Social",
                ParsePt(ss.Groups[1].Value),
                "deduction",
                Percentage: ParseUs(ss.Groups[2].Value),
                IncidenciaBase: ParseUs(ss.Groups[3].Value)));

        var irs = IrsRegex().Match(fullText);
        if (irs.Success)
            items.Add(new ParsedSalaryLineItem(
                "IRS",
                ParsePt(irs.Groups[1].Value),
                "tax",
                Percentage: ParseUs(irs.Groups[2].Value),
                IncidenciaBase: ParseUs(irs.Groups[3].Value)));

        return items;
    }

    public static decimal ParsePt(string s) =>
        decimal.Parse(s.Replace(" ", "").Replace(",", "."), CultureInfo.InvariantCulture);

    public static decimal ParseUs(string s) =>
        decimal.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);

    private static int PtMonthToNumber(string month) => month switch
    {
        "janeiro"   => 1,
        "fevereiro" => 2,
        "março"     => 3,
        "abril"     => 4,
        "maio"      => 5,
        "junho"     => 6,
        "julho"     => 7,
        "agosto"    => 8,
        "setembro"  => 9,
        "outubro"   => 10,
        "novembro"  => 11,
        "dezembro"  => 12,
        _ => throw new InvalidOperationException($"Unknown Portuguese month: '{month}'")
    };

    [GeneratedRegex(@"^(.+)\s+\1$")]
    private static partial Regex DuplicateLineRegex();

    [GeneratedRegex(@"N\.º Contribuinte:\s*(\d+)")]
    private static partial Regex ContribuinteRegex();

    [GeneratedRegex(@"Mês:\s*(\w+)\s*[-–]\s*(\d{4})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"(\d{1,3}(?:,\d{3})+\.\d{2})\s+(\d+\.\d{2})\s+(\d{1,3}(?:,\d{3})+\.\d{2})\s+([\d ]+,\d{2})\s+([\d ]+,\d{2})")]
    private static partial Regex TotalsRegex();

    [GeneratedRegex(@"Vencimento\s+([\d ]+,\d{2})")]
    private static partial Regex VencimentoRegex();

    [GeneratedRegex(@"(\d+\.\d{2})\s+CGD")]
    private static partial Regex HourlyRateRegex();

    [GeneratedRegex(@"PPR\s+([\d.]+)\s+([\d.]+)\s+([\d ]+,\d{2})")]
    private static partial Regex PprRegex();

    [GeneratedRegex(@"Tickets Refeição\s+([\d.]+)\s+([\d.]+)\s+([\d ]+,\d{2})")]
    private static partial Regex TicketsRegex();

    [GeneratedRegex(@"Segurança Social\s+([\d ]+,\d{2})\s+([\d.]+)\s+([\d,]+\.\d{2})")]
    private static partial Regex SegSocialRegex();

    [GeneratedRegex(@"IRS\s+([\d ]+,\d{2})\s+([\d.]+)\s+([\d.]+)")]
    private static partial Regex IrsRegex();
}
