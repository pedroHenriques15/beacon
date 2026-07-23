using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

public static partial class MealCardTextParser
{
    [GeneratedRegex(
        @"^(\d{2}/\d{2}/\d{4})(.+)\s+PT\s*(?:0\.00)?(-?\d+,\d{2})\s*€(-?)\s*$",
        RegexOptions.Compiled)]
    private static partial Regex LinePattern();

    public static ParsedStatement Parse(string rawText)
    {
        var lines = rawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var transactions = new List<ParsedTransaction>();

        foreach (var line in lines)
        {
            var m = LinePattern().Match(line);
            if (!m.Success) continue;

            var dateStr   = m.Groups[1].Value;
            var desc      = m.Groups[2].Value.Trim();
            var amountStr = m.Groups[3].Value.Replace(",", ".");
            var isCredit  = m.Groups[4].Value == "-";

            if (!DateOnly.TryParseExact(dateStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                continue;

            transactions.Add(new ParsedTransaction(
                DatePosting: date,
                DateValue:   date,
                Description: desc,
                Amount:      Math.Abs(amount),
                Type:        isCredit ? "credit" : "debit",
                Balance:     0m));
        }

        if (transactions.Count == 0)
            throw new FormatException("No valid transactions found in the provided text.");

        var periodFrom = transactions.Min(t => t.DatePosting);
        var periodTo   = transactions.Max(t => t.DatePosting);

        return new ParsedStatement(
            Bank:           "MEAL CARD",
            Account:        string.Empty,
            PeriodFrom:     periodFrom,
            PeriodTo:       periodTo,
            Currency:       "EUR",
            OpeningBalance: 0m,
            ClosingBalance: 0m,
            SourceFile:     "meal-card-text-import",
            Transactions:   transactions);
    }
}
