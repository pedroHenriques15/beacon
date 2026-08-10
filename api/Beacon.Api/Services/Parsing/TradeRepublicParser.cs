using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

/// <summary>
/// Parser for Trade Republic (Portugal branch) account statements.
///
/// Trade Republic's transaction table does not linearise cleanly: pdfplumber emits each logical
/// row across three physical lines, and the TYPE column can wrap. A single row looks like:
///
///     02 Aug Card                    &lt;- date line: "02" "Aug" + trailing "Card"
///     MINI MERCADO €7.30 €7,640.62   &lt;- money line: leading "MINI MERCADO" + amount + balance
///     2026 Transaction               &lt;- year line: "2026" + trailing "Transaction"
///
/// so parsing is block-based (keyed off the date line) rather than a single-line regex. The
/// money direction is derived from the running-balance delta, exactly like <see cref="RevolutParser"/>.
/// </summary>
public partial class TradeRepublicParser : IBankStatementParser
{
    public string BankName => "TRADE REPUBLIC";

    public bool CanParse(string fullText) =>
        fullText.Contains("TRBKPTP2") || fullText.Contains("TRADE REPUBLIC BANK GMBH");

    [GeneratedRegex(@"IBAN\s+(PT\d+)")]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"DATE\s+(\d{2} (?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) \d{4})\s*-\s*(\d{2} (?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) \d{4})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"Checking Account\s+€([\d,]+\.\d{2})\s+€([\d,]+\.\d{2})\s+€([\d,]+\.\d{2})\s+€([\d,]+\.\d{2})")]
    private static partial Regex SummaryRegex();

    [GeneratedRegex(@"^(\d{2}) (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(?:\s+(.*))?$")]
    private static partial Regex DateLineRegex();

    [GeneratedRegex(@"^(\d{4})(?:\s+(.*))?$")]
    private static partial Regex YearLineRegex();

    [GeneratedRegex(@"€(-?[\d,]+\.\d{2})")]
    private static partial Regex EuroRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public ParsedStatement Parse(string fileName, IReadOnlyList<string> pages)
    {
        var fullText = string.Join("\n", pages);

        var iban = IbanRegex().Match(fullText).Groups[1].Value;

        DateOnly periodFrom = default, periodTo = default;
        var pm = PeriodRegex().Match(fullText);
        if (pm.Success)
        {
            periodFrom = ParseFullDate(pm.Groups[1].Value);
            periodTo   = ParseFullDate(pm.Groups[2].Value);
        }

        decimal opening = 0, closing = 0;
        var sm = SummaryRegex().Match(fullText);
        if (sm.Success)
        {
            opening = ParseAmount(sm.Groups[1].Value);
            closing = ParseAmount(sm.Groups[4].Value);
        }

        var transactions = ParseTransactions(pages, opening);

        if (periodFrom == default || periodTo == default)
        {
            if (transactions.Count > 0)
            {
                periodFrom = transactions.Min(t => t.DatePosting);
                periodTo   = transactions.Max(t => t.DatePosting);
            }
            else
            {
                throw new NotSupportedException(
                    "Could not determine the Trade Republic statement period: no 'DATE ... - ...' " +
                    "header was found and no transactions were parsed to derive it from.");
            }
        }

        return new ParsedStatement(BankName, iban, periodFrom, periodTo,
            "EUR", opening, closing, fileName, transactions);
    }

    private static List<ParsedTransaction> ParseTransactions(
        IReadOnlyList<string> pages, decimal openingBalance)
    {
        var result  = new List<ParsedTransaction>();
        decimal balance = openingBalance;

        var lines = new List<string>();
        foreach (var page in pages)
        {
            foreach (var raw in page.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("BALANCE OVERVIEW", StringComparison.OrdinalIgnoreCase))
                    goto collected;
                if (line.Length > 0) lines.Add(line);
            }
        }
        collected:

        for (int i = 0; i < lines.Count; i++)
        {
            var dm = DateLineRegex().Match(lines[i]);
            if (!dm.Success) continue;

            int next = i + 1;
            while (next < lines.Count && !DateLineRegex().IsMatch(lines[next])) next++;

            var day          = dm.Groups[1].Value;
            var mon          = dm.Groups[2].Value;
            var dateTrailing = dm.Groups[3].Value.Trim();

            string moneyLeading = "", yearTrailing = "", yearValue = "";
            decimal? amount = null, saldo = null;

            for (int k = i + 1; k < next; k++)
            {
                if (amount is null)
                {
                    var euros = EuroRegex().Matches(lines[k]);
                    if (euros.Count >= 2)
                    {
                        amount       = ParseAmount(euros[0].Groups[1].Value);
                        saldo        = ParseAmount(euros[^1].Groups[1].Value);
                        moneyLeading = lines[k][..lines[k].IndexOf('€')].Trim();
                        continue;
                    }
                }

                if (yearValue.Length == 0)
                {
                    var ym = YearLineRegex().Match(lines[k]);
                    if (ym.Success)
                    {
                        yearValue    = ym.Groups[1].Value;
                        yearTrailing = ym.Groups[2].Value.Trim();
                    }
                }
            }

            i = next - 1; // resume the outer loop on the next date line

            if (amount is null || saldo is null || yearValue.Length == 0) continue;

            var date = ParseFullDate($"{day} {mon} {yearValue}");

            var description = WhitespaceRegex().Replace(
                string.Join(" ", new[] { moneyLeading, dateTrailing, yearTrailing }
                    .Where(p => p.Length > 0)),
                " ").Trim();

            var type  = "unknown";
            var delta = Math.Round(saldo.Value - balance, 2);
            if (Math.Abs(Math.Abs(delta) - amount.Value) < 0.02m)
                type = delta > 0 ? "credit" : "debit";
            balance = saldo.Value;

            result.Add(new ParsedTransaction(date, date, description, amount.Value, type, saldo.Value));
        }

        return result;
    }

    private static DateOnly ParseFullDate(string dayMonYear) =>
        DateOnly.ParseExact(dayMonYear, "dd MMM yyyy", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string s) =>
        decimal.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);
}
