using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

public partial class ActivoBankParser : IBankStatementParser
{
    public string BankName => "ACTIVOBANK";

    public bool CanParse(string fullText) =>
        fullText.Contains("ACTVPTPL") || fullText.Contains("EXTRATO COMBINADO");

    private const string AmountPat = @"\d{1,3}(?:\s\d{3})*\.\d{2}";
    private const string DatePat   = @"\d{1,2}\.\d{2}";

    [GeneratedRegex(@"DEPOSITO A ORDEM:\s*(\d+)")]
    private static partial Regex AccountRegex();

    [GeneratedRegex(@"EXTRATO DE (\d{4})/(\d{2})/(\d{2}) A (\d{4})/(\d{2})/(\d{2})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"MOEDA BASE:\s*(\w+)")]
    private static partial Regex CurrencyRegex();

    [GeneratedRegex(@"SALDO INICIAL\s+(\d{1,3}(?:\s\d{3})*\.\d{2})")]
    private static partial Regex OpeningRegex();

    [GeneratedRegex(@"SALDO FINAL\s+(\d{1,3}(?:\s\d{3})*\.\d{2})")]
    private static partial Regex ClosingRegex();

    private static readonly Regex TxRegex = new(
        $@"^({DatePat})\s+({DatePat})\s+(.*?)\s+({AmountPat})\s+({AmountPat})\s*$",
        RegexOptions.Compiled);

    private static readonly Regex OpeningLineRegex = new(
        $@"^SALDO INICIAL\s+({AmountPat})$",
        RegexOptions.Compiled);

    private static readonly string[] SkipPrefixes =
        ["A TRANSPORTAR", "TRANSPORTE", "SALDO FINAL", "SALDO DISPONIVEL", "ULTRAPASSAGEM"];

    public ParsedStatement Parse(string fileName, IReadOnlyList<string> pages)
    {
        var fullText = string.Join("\n", pages);

        var account  = AccountRegex().Match(fullText).Groups[1].Value;
        var currencyRaw = CurrencyRegex().Match(fullText).Groups[1].Value;
        var currency = currencyRaw.ToUpper() switch
        {
            "EURO" => "EUR",
            var c when c.Length == 3 => c,
            _ => "EUR"
        };

        var periodMatch = PeriodRegex().Match(fullText);
        int year        = int.Parse(periodMatch.Groups[1].Value);
        int periodMonth = int.Parse(periodMatch.Groups[2].Value);
        var periodFrom  = new DateOnly(year, periodMonth, int.Parse(periodMatch.Groups[3].Value));
        var periodTo    = new DateOnly(
            int.Parse(periodMatch.Groups[4].Value),
            int.Parse(periodMatch.Groups[5].Value),
            int.Parse(periodMatch.Groups[6].Value));

        var openingStr = OpeningRegex().Match(fullText).Groups[1].Value;
        var closingStr = ClosingRegex().Match(fullText).Groups[1].Value;
        var opening    = ParseAmount(openingStr);
        var closing    = ParseAmount(closingStr);

        var transactions = ParseTransactions(pages, year, periodMonth);

        return new ParsedStatement(BankName, account, periodFrom, periodTo,
            currency,
            opening, closing, fileName, transactions);
    }

    private static List<ParsedTransaction> ParseTransactions(
        IReadOnlyList<string> pages, int year, int periodMonth)
    {
        var result  = new List<ParsedTransaction>();
        decimal? balance = null;

        foreach (var page in pages)
        {
            foreach (var rawLine in page.Split('\n'))
            {
                var line = rawLine.Trim();

                var openingMatch = OpeningLineRegex.Match(line);
                if (openingMatch.Success)
                {
                    balance = ParseAmount(openingMatch.Groups[1].Value);
                    continue;
                }

                if (SkipPrefixes.Any(p => line.StartsWith(p)))
                    continue;

                var m = TxRegex.Match(line);
                if (!m.Success) continue;

                var datePost = ParseDate(m.Groups[1].Value, year, periodMonth);
                var dateVal  = ParseDate(m.Groups[2].Value, year, periodMonth);
                var desc     = m.Groups[3].Value.Trim();
                var amount   = ParseAmount(m.Groups[4].Value);
                var saldo    = ParseAmount(m.Groups[5].Value);

                var type = "unknown";
                if (balance.HasValue)
                {
                    var delta = Math.Round(saldo - balance.Value, 2);
                    if (Math.Abs(Math.Abs(delta) - amount) < 0.02m)
                        type = delta > 0 ? "credit" : "debit";
                }

                balance = saldo;
                result.Add(new ParsedTransaction(datePost, dateVal, desc, amount, type, saldo));
            }
        }

        return result;
    }

    private static DateOnly ParseDate(string md, int year, int periodMonth)
    {
        var parts = md.Split('.');
        int month = int.Parse(parts[0]);
        int day   = int.Parse(parts[1]);
        if (month > periodMonth + 1) year--;
        return new DateOnly(year, month, day);
    }

    private static decimal ParseAmount(string s) =>
        decimal.Parse(s.Replace(" ", ""), CultureInfo.InvariantCulture);
}
