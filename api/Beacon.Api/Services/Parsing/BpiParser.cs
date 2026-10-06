using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

public partial class BpiParser : IBankStatementParser
{
    public string BankName => "BPI";

    public bool CanParse(string fullText) =>
        fullText.Contains("BBPIPTPL") || fullText.Contains("EXTRACTO INTEGRADO");

    private static readonly CultureInfo PtCulture = CultureInfo.GetCultureInfo("pt-PT");

    private const string AmountPat = @"-?\d{1,3}(?:\s\d{3})*,\d{2}";
    private const string DatePat = @"\d{2}/\d{2}";

    [GeneratedRegex(@"IBAN:\s*(PT[\d\s]+)")]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"Per[ií]odo De (\d{2})/(\d{2})/(\d{4}) a (\d{2})/(\d{2})/(\d{4})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"SALDO ANTERIOR CONTABILISTICO\s+(-?\d{1,3}(?:\s\d{3})*,\d{2})")]
    private static partial Regex OpeningRegex();

    [GeneratedRegex(@"SALDO ACTUAL CONTABILISTICO\s+(-?\d{1,3}(?:\s\d{3})*,\d{2})")]
    private static partial Regex ClosingRegex();

    [GeneratedRegex(@"ACTIVOS\s+(\d{1,3}(?:\s\d{3})*,\d{2})")]
    private static partial Regex ActivosRegex();

    private static readonly Regex TxRegex = new(
        $@"^({DatePat})(?:\s+({DatePat}))?\s+(.*?)\s+({AmountPat})\s+({AmountPat})\s*$",
        RegexOptions.Compiled);

    private const string QuantityPat = @"\d{1,3}(?:\s\d{3})*,\d+";

    // A row of the retirement plan (PPR) section: dates, wording, units, their average cost and
    // the amount (VALOR APLICADO).
    private static readonly Regex PprMovementRegex = new(
        $@"^({DatePat})\s+({DatePat})\s+(.+?)\s+{QuantityPat}\s+{QuantityPat}\s+({AmountPat})$",
        RegexOptions.Compiled);

    private static readonly Regex LeadingDateRegex = new($@"^{DatePat}\s", RegexOptions.Compiled);

    // The PPR section's wordings seen on real statements. Only a subscription becomes a row. A
    // redemption stays in PprBalance until its cash reaches the current account, where it is a
    // row of its own ("RESGATE ... PPR"); a class transfer or a correction moves no money in or out.
    private static readonly string[] PprSubscriptionWordings = ["SUBSCRICAO EMPRESA"];
    private static readonly string[] PprOtherWordings =
        ["RESG.FORA COND.GERAL", "SUBS.TRANSF.CLASSE", "RESGATE POR ERRO"];

    private static readonly string[] SkipContains =
        ["DATA DATA",
            "MOV VAL",
            "CONTA A ORDEM",
            "NIB:",
            "IBAN:",
            "SALDO ANTERIOR",
            "SALDO ACTUAL",
            "TOTAL DEP",
            "PLANOS DE POUPANÇA",
            "DESCRIÇÃO DO MOVIMENTO",
            "Sede:",
            "BPI Direto",
            "Capital Social"];

    public ParsedStatement Parse(string fileName, IReadOnlyList<string> pages)
    {
        var fullText = string.Join("\n", pages);

        var ibanRaw = IbanRegex().Match(fullText).Groups[1].Value;
        var iban = Regex.Replace(ibanRaw, @"\s+", "");

        var pm = PeriodRegex().Match(fullText);
        if (!pm.Success)
            throw new InvalidOperationException("Could not find period header in BPI statement. Check the PDF layout.");

        var periodFrom = new DateOnly(
            int.Parse(pm.Groups[3].Value),
            int.Parse(pm.Groups[2].Value),
            int.Parse(pm.Groups[1].Value));
        var periodTo = new DateOnly(
            int.Parse(pm.Groups[6].Value),
            int.Parse(pm.Groups[5].Value),
            int.Parse(pm.Groups[4].Value));

        decimal opening, closing;
        decimal? pprBalance = null;
        var activosMatch = ActivosRegex().Match(fullText);
        if (activosMatch.Success)
        {
            closing = ParseAmount(activosMatch.Groups[1].Value);
            var checkingOpening = ParseAmount(OpeningRegex().Match(fullText).Groups[1].Value);
            var checkingClosing = ParseAmount(ClosingRegex().Match(fullText).Groups[1].Value);
            opening = closing + checkingOpening - checkingClosing;
            pprBalance = closing - checkingClosing;
        }
        else
        {
            opening = ParseAmount(OpeningRegex().Match(fullText).Groups[1].Value);
            closing = ParseAmount(ClosingRegex().Match(fullText).Groups[1].Value);
        }

        var transactions = ParseTransactions(pages, periodFrom, periodTo);

        return new ParsedStatement(BankName, iban, periodFrom, periodTo,
            "EUR", opening, closing, fileName, transactions, pprBalance,
            PprSubscriptions: ParsePprSubscriptions(pages, periodFrom, periodTo));
    }

    private static List<ParsedPprSubscription> ParsePprSubscriptions(
        IReadOnlyList<string> pages, DateOnly periodFrom, DateOnly periodTo)
    {
        var result = new List<ParsedPprSubscription>();
        bool inPlans = false;

        foreach (var page in pages)
        {
            foreach (var rawLine in page.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.Contains("TOTAL PLANOS DE POUPANÇA")) { inPlans = false; continue; }
                if (line.Contains("PLANOS DE POUPANÇA")) { inPlans = true; continue; }
                if (!inPlans || !LeadingDateRegex.IsMatch(line)) continue;

                var m = PprMovementRegex.Match(line);
                if (!m.Success)
                    throw new FormatException(
                        $"A movement in the BPI statement's retirement plan section could not be read: \"{line}\".");

                var wording = m.Groups[3].Value;
                if (PprSubscriptionWordings.Contains(wording))
                    result.Add(new ParsedPprSubscription(
                        ParseDate(m.Groups[1].Value, periodFrom, periodTo),
                        ParseDate(m.Groups[2].Value, periodFrom, periodTo),
                        wording, ParseAmount(m.Groups[4].Value)));
                else if (!PprOtherWordings.Contains(wording))
                    throw new FormatException(
                        $"The BPI statement's retirement plan section has a movement Beacon doesn't know: \"{line}\".");
            }
        }

        return result;
    }

    private static List<ParsedTransaction> ParseTransactions(
        IReadOnlyList<string> pages, DateOnly periodFrom, DateOnly periodTo)
    {
        var result = new List<ParsedTransaction>();
        bool inCurrentAccount = false;

        foreach (var page in pages)
        {
            foreach (var rawLine in page.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.Contains("DEPÓSITOS À ORDEM")) { inCurrentAccount = true; continue; }
                if (line.Contains("PLANOS DE POUPANÇA") || line.Contains("TOTAL DEPÓSITOS"))
                { inCurrentAccount = false; continue; }
                if (!inCurrentAccount) continue;
                if (SkipContains.Any(s => line.Contains(s))) continue;

                var m = TxRegex.Match(line);
                if (!m.Success) continue;

                var datePostRaw = m.Groups[1].Value;
                var dateValRaw = m.Groups[2].Success ? m.Groups[2].Value : datePostRaw;
                var desc = m.Groups[3].Value.Trim();
                var signedAmt = ParseAmount(m.Groups[4].Value);
                var saldo = ParseAmount(m.Groups[5].Value);

                var type = signedAmt >= 0 ? "credit" : "debit";
                var amount = Math.Abs(signedAmt);

                result.Add(new ParsedTransaction(
                    ParseDate(datePostRaw, periodFrom, periodTo),
                    ParseDate(dateValRaw, periodFrom, periodTo),
                    desc, amount, type, saldo));
            }
        }

        return result;
    }

    // Rows carry a day and a month only. A statement from December into January holds dates of
    // both years, and a row can fall a few days outside the period, so the year is the one that
    // puts the date nearest the period.
    private static DateOnly ParseDate(string dm, DateOnly periodFrom, DateOnly periodTo)
    {
        var p = dm.Split('/');
        int day = int.Parse(p[0]), month = int.Parse(p[1]);
        return new[] { periodFrom.Year, periodTo.Year }
            .Distinct()
            .Select(year => new DateOnly(year, month, day))
            .MinBy(date => Math.Max(periodFrom.DayNumber - date.DayNumber, date.DayNumber - periodTo.DayNumber));
    }

    private static decimal ParseAmount(string s) =>
        string.IsNullOrEmpty(s) ? 0m :
        decimal.Parse(s.Replace(" ", ""), PtCulture);
}
