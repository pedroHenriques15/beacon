using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

public partial class RevolutParser : IBankStatementParser
{
    public string BankName => "REVOLUT";

    public bool CanParse(string fullText) =>
        fullText.Contains("REVOPTP2") || fullText.Contains("Revolut Bank UAB");

    private static readonly CultureInfo PtCulture = CultureInfo.GetCultureInfo("pt-PT");

    private const string AmountPat = @"[\d\.]+,\d{2}";
    private const string DatePat   = @"\d{2}/\d{2}/\d{4}";

    [GeneratedRegex(@"IBAN\s+(PT\w+)")]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2})_(\d{4}-\d{2}-\d{2})")]
    private static partial Regex PeriodFromFilenameRegex();

    [GeneratedRegex(@"Conta \(Conta Corrente\)\s+([\d\.]+,\d{2})€\s+[\d\.]+,\d{2}€\s+[\d\.]+,\d{2}€\s+([\d\.]+,\d{2})€")]
    private static partial Regex SummaryRegex();

    private static readonly Regex TxRegex = new(
        $@"^({DatePat})\s+({DatePat})\s+(.*?)\s+({AmountPat})€\s+({AmountPat})€\s*$",
        RegexOptions.Compiled);

    public ParsedStatement Parse(string fileName, IReadOnlyList<string> pages)
    {
        var fullText = string.Join("\n", pages);

        var iban = IbanRegex().Match(fullText).Groups[1].Value;

        DateOnly periodFrom = default, periodTo = default;
        var pfm = PeriodFromFilenameRegex().Match(fileName);
        if (pfm.Success)
        {
            periodFrom = DateOnly.Parse(pfm.Groups[1].Value);
            periodTo   = DateOnly.Parse(pfm.Groups[2].Value);
        }

        decimal opening = 0, closing = 0;
        var sm = SummaryRegex().Match(fullText);
        if (sm.Success)
        {
            opening = ParseAmount(sm.Groups[1].Value);
            closing = ParseAmount(sm.Groups[2].Value);
        }

        var transactions = ParseTransactions(pages, opening);

        return new ParsedStatement(BankName, iban, periodFrom, periodTo,
            "EUR", opening, closing, fileName, transactions);
    }

    private static List<ParsedTransaction> ParseTransactions(
        IReadOnlyList<string> pages, decimal openingBalance)
    {
        var result  = new List<ParsedTransaction>();
        decimal balance = openingBalance;

        foreach (var page in pages)
        {
            foreach (var rawLine in page.Split('\n'))
            {
                var m = TxRegex.Match(rawLine.Trim());
                if (!m.Success) continue;

                var datePost = ParseDate(m.Groups[1].Value);
                var dateVal  = ParseDate(m.Groups[2].Value);
                var desc     = m.Groups[3].Value.Trim();
                var amount   = ParseAmount(m.Groups[4].Value);
                var saldo    = ParseAmount(m.Groups[5].Value);

                var type  = "unknown";
                var delta = Math.Round(saldo - balance, 2);
                if (Math.Abs(Math.Abs(delta) - amount) < 0.02m)
                    type = delta > 0 ? "credit" : "debit";

                balance = saldo;
                result.Add(new ParsedTransaction(datePost, dateVal, desc, amount, type, saldo));
            }
        }

        return result;
    }

    private static DateOnly ParseDate(string dmy)
    {
        var p = dmy.Split('/');
        return new DateOnly(int.Parse(p[2]), int.Parse(p[1]), int.Parse(p[0]));
    }

    private static decimal ParseAmount(string s) =>
        decimal.Parse(s.Replace(".", "").Replace(",", "."), CultureInfo.InvariantCulture);
}
