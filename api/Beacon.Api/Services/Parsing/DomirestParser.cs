using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

public partial class DomirestParser : ISalarySlipParser
{
    public string ParserName => "Domirest";

    public bool CanParse(string fullText) =>
        fullText.Contains("DOMIREST");

    private static readonly Dictionary<string, string> NameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Remuner. Normal"]      = "Remuneração Normal",
        ["Premio Produtividade"] = "Prémio de Produtividade",
        ["Sub.Kms/Deslocaç"]     = "Subsídio de Deslocação (Km)",
    };

    private static string Normalize(string raw) => NameMap.TryGetValue(raw, out var n) ? n : raw;

    public ParsedSalarySlip Parse(string fileName, IReadOnlyList<string> pages)
    {
        var fullText = string.Join("\n", pages);

        var employer    = ExtractEmployer(fullText);
        var employerNif = ExtractEmployerNif(fullText);
        var period      = ExtractPeriod(fullText);
        var (gross, net) = ExtractTotals(fullText);
        var baseAmount  = ExtractBaseAmount(fullText);
        var lineItems   = ExtractLineItems(fullText, out var hoursWorked, out var hourlyRate);

        return new ParsedSalarySlip(
            employer, employerNif, period, gross, net, lineItems,
            BaseAmount: baseAmount,
            HoursWorked: hoursWorked,
            HourlyRate: hourlyRate,
            TotalEspecie: null);
    }

    private static string ExtractEmployer(string fullText)
    {
        var m = EmployerRegex().Match(fullText);
        return m.Success ? m.Groups[1].Value.Trim() : "Unknown";
    }

    private static string? ExtractEmployerNif(string fullText)
    {
        var m = NifRegex().Match(fullText);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static DateOnly ExtractPeriod(string fullText)
    {
        var m = DateRegex().Match(fullText);
        if (!m.Success)
            throw new InvalidOperationException("Could not find a date in Domirest payslip.");

        var month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var year  = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return new DateOnly(year, month, 1);
    }

    private static (decimal Gross, decimal Net) ExtractTotals(string fullText)
    {
        var m = TotalsRegex().Match(fullText);
        if (!m.Success)
            throw new InvalidOperationException("Could not find gross/net totals in Domirest payslip.");

        return (ParsePt(m.Groups[1].Value), ParsePt(m.Groups[2].Value));
    }

    private static decimal? ExtractBaseAmount(string fullText)
    {
        var m = BaseAmountRegex().Match(fullText);
        return m.Success ? ParsePt(m.Groups[1].Value) : null;
    }

    private static List<ParsedSalaryLineItem> ExtractLineItems(
        string fullText,
        out decimal? hoursWorked,
        out decimal? hourlyRate)
    {
        hoursWorked = null;
        hourlyRate  = null;

        var items     = new List<ParsedSalaryLineItem>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in IncomeLineRegex().Matches(fullText))
        {
            var rawName  = m.Groups[1].Value.Trim();
            var cleanName = Normalize(rawName);

            if (!seenNames.Add(cleanName)) continue;

            var qty      = ParsePt(m.Groups[2].Value);
            var unitVal  = ParsePt(m.Groups[3].Value);
            var amount   = ParsePt(m.Groups[4].Value);

            if (cleanName == "Remuneração Normal")
            {
                hoursWorked = qty;
                hourlyRate  = unitVal;
            }

            items.Add(new ParsedSalaryLineItem(
                cleanName, amount, "income",
                Quantity: qty,
                UnitValue: unitVal));
        }

        var ss = SegSocialRegex().Match(fullText);
        if (ss.Success && seenNames.Add("Segurança Social"))
            items.Add(new ParsedSalaryLineItem(
                "Segurança Social",
                ParsePt(ss.Groups[3].Value),
                "deduction",
                Percentage: ParsePt(ss.Groups[1].Value),
                IncidenciaBase: ParsePt(ss.Groups[2].Value)));

        var irs = IrsRegex().Match(fullText);
        if (irs.Success)
        {
            var irsAmount = ParsePt(irs.Groups[1].Value);
            if (irsAmount > 0 && seenNames.Add("IRS"))
            {
                decimal? irsIncidencia = null;
                var irsInc = IrsIncidenciaRegex().Match(fullText);
                if (irsInc.Success)
                    irsIncidencia = ParsePt(irsInc.Groups[1].Value);

                items.Add(new ParsedSalaryLineItem(
                    "IRS", irsAmount, "tax",
                    IncidenciaBase: irsIncidencia));
            }
        }

        return items;
    }

    public static decimal ParsePt(string s) =>
        decimal.Parse(s.Trim().Replace(" ", "").Replace(",", "."), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(.+?)\s+RECIBO DE REMUNERA", RegexOptions.Multiline)]
    private static partial Regex EmployerRegex();

    [GeneratedRegex(@"\bNIF:\s*(\d+)")]
    private static partial Regex NifRegex();

    [GeneratedRegex(@"\b(\d{2})-(\d{2})-(\d{4})\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"(\d[\d ]*,\d{2})\s+\d[\d ]*,\d{2}\s+Transfer[eê]ncia Banc[aá]ria\s+(\d[\d ]*,\d{2})")]
    private static partial Regex TotalsRegex();

    [GeneratedRegex(@"^\d+\s+(.+?)\s+([\d,]+)\s+([\d,]+)\s+([\d,]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex IncomeLineRegex();

    [GeneratedRegex(@"Segurança Social\s+([\d,]+)%\s+([\d,]+)\s+([\d,]+)")]
    private static partial Regex SegSocialRegex();

    [GeneratedRegex(@"Reten[çc][aã]o:\s*([\d,]+)")]
    private static partial Regex IrsRegex();

    [GeneratedRegex(@"Incid[eê]ncia:\s*([\d,]+)")]
    private static partial Regex IrsIncidenciaRegex();

    [GeneratedRegex(@"REMUNERA[ÇC][AÃ]O BASE[^\n]*\n\d+\s+([\d ]+,\d{2})", RegexOptions.Multiline)]
    private static partial Regex BaseAmountRegex();
}
