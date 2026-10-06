using System.Globalization;
using System.Text.RegularExpressions;

namespace Beacon.Api.Services.Parsing;

/// <summary>
/// Parser for Trade Republic's transaction export ("Extrato de transações", a CSV), one calendar
/// month per file (ADR-031). The upload passes the file's text as the single page, and the header
/// line is the detection signal.
///
/// Each row is one transaction whose cash effect is <c>amount + fee + tax</c>, all three signed;
/// a row that moves no money is skipped with a warning. Rows come in no particular order, so they
/// are sorted by <c>datetime</c>. The export has no balances: they count from 0
/// (<see cref="ParsedStatement.BalancesRelative"/>) and the upload shifts them by the previous
/// month's closing. A <c>BUY</c> row carries its trade. An account, row type or currency not seen
/// in a real export yet is refused rather than guessed.
/// </summary>
public partial class TradeRepublicCsvParser : IBankStatementParser
{
    public string BankName => "TRADE REPUBLIC";

    private static readonly string[] Header =
    [
        "datetime", "date", "account_type", "category", "type", "asset_class", "name", "symbol",
        "shares", "price", "amount", "fee", "tax", "currency", "original_amount",
        "original_currency", "fx_rate", "description", "transaction_id", "counterparty_name",
        "counterparty_iban", "payment_reference", "mcc_code",
    ];

    private static readonly Dictionary<string, int> Column =
        Header.Select((name, index) => (name, index)).ToDictionary(c => c.name, c => c.index);

    // The (category, type) pairs seen in real exports. A sell, a dividend or a card refund is
    // refused until its spelling has been seen.
    private static readonly HashSet<(string Category, string Type)> KnownTypes =
    [
        ("CASH", "CARD_TRANSACTION"),
        ("CASH", "CARD_TRANSACTION_INTERNATIONAL"),
        ("CASH", "CARD_ORDERING_FEE"),
        ("CASH", "TRANSFER_INSTANT_INBOUND"),
        ("CASH", "TRANSFER_INSTANT_OUTBOUND"),
        ("CASH", "INTEREST_PAYMENT"),
        ("CASH", "BENEFITS_SAVEBACK"),
        ("TRADING", "BUY"),
    ];

    // ISIN: 2 country letters + 9 alphanumeric + 1 check digit (e.g. IE00BK5BQT80).
    [GeneratedRegex(@"^[A-Z]{2}[A-Z0-9]{9}[0-9]$")]
    private static partial Regex IsinRegex();

    private sealed record Row(
        DateTimeOffset At, DateOnly Date, string Type, decimal Net, string Description, ParsedTrade? Trade);

    public bool CanParse(string fullText)
    {
        var text = fullText.TrimStart('﻿');
        var end = text.IndexOfAny(['\r', '\n']);
        try
        {
            var rows = CsvText.ReadRows(end < 0 ? text : text[..end]);
            return rows.Count == 1 && rows[0].SequenceEqual(Header);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public ParsedStatement Parse(string fileName, IReadOnlyList<string> pages)
    {
        var lines = CsvText.ReadRows(string.Join("\n", pages).TrimStart('﻿'));
        if (lines.Count == 0 || !lines[0].SequenceEqual(Header))
            throw new FormatException("This is not a Trade Republic transaction export: its header line differs.");

        var rows = lines.Skip(1).Select((fields, i) => ReadRow(fields, lineNumber: i + 2)).ToList();
        if (rows.Count == 0)
            throw new FormatException("This Trade Republic export has no transactions.");

        var first = rows.Min(r => r.Date);
        var last = rows.Max(r => r.Date);
        if (first.Year != last.Year || first.Month != last.Month)
            throw new FormatException(
                $"This Trade Republic export runs from {first:yyyy-MM-dd} to {last:yyyy-MM-dd}. " +
                "Export one file per calendar month and upload each one.");

        var transactions = new List<ParsedTransaction>();
        var warnings = new List<string>();
        var balance = 0m;
        foreach (var row in rows.OrderBy(r => r.At))
        {
            if (row.Net == 0)
            {
                warnings.Add($"Skipped the {row.Type} row of {row.Date:yyyy-MM-dd} (\"{row.Description}\"): it moves no money.");
                continue;
            }

            balance += row.Net;
            transactions.Add(new ParsedTransaction(
                row.Date, row.Date, row.Description, Math.Abs(row.Net),
                row.Net > 0 ? "credit" : "debit", balance, row.Trade));
        }

        var periodFrom = new DateOnly(first.Year, first.Month, 1);
        return new ParsedStatement(
            BankName, Account: string.Empty, periodFrom, periodFrom.AddMonths(1).AddDays(-1),
            "EUR", OpeningBalance: 0m, ClosingBalance: balance, fileName, transactions,
            BalancesRelative: true, Warnings: warnings.Count > 0 ? warnings : null);
    }

    private static Row ReadRow(string[] fields, int lineNumber)
    {
        if (fields.Length != Header.Length)
            throw new FormatException(
                $"Line {lineNumber} of the Trade Republic export has {fields.Length} fields, not {Header.Length}.");

        string Field(string name) => fields[Column[name]].Trim();

        if (!DateOnly.TryParseExact(Field("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !DateTimeOffset.TryParse(Field("datetime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            throw new FormatException($"Line {lineNumber} of the Trade Republic export has no valid date.");

        var category = Field("category");
        var type = Field("type");
        var what = $"The {type} row of {date:yyyy-MM-dd}";

        if (Field("account_type") != "DEFAULT")
            throw new FormatException(
                $"{what} belongs to the \"{Field("account_type")}\" account; only the main account (DEFAULT) is supported.");
        if (!KnownTypes.Contains((category, type)))
            throw new FormatException(
                $"{what} has a type Beacon does not import yet ({category} / {type}).");
        if (Field("currency") != "EUR")
            throw new FormatException(
                $"{what} is in {Field("currency")}; only EUR is supported.");

        var amount = Amount(Field("amount"), what);
        var fee = Amount(Field("fee"), what);
        var tax = Amount(Field("tax"), what);
        var net = Math.Round(amount + fee + tax, 2, MidpointRounding.AwayFromZero);

        var description = Field("description");
        if (description.Length == 0)
            description = Field("name").Length > 0 ? Field("name") : type;

        var trade = type == "BUY" ? ReadTrade(Field, date, net, fee + tax, description, what) : null;
        return new Row(at, date, type, net, description, trade);
    }

    private static ParsedTrade ReadTrade(
        Func<string, string> field, DateOnly date, decimal net, decimal feeAndTax, string description, string what)
    {
        var isin = field("symbol");
        if (field("asset_class") != "FUND")
            throw new FormatException(
                $"{what} buys a {field("asset_class")}; only funds (ETFs) are imported as investments.");
        if (!IsinRegex().IsMatch(isin))
            throw new FormatException($"{what} has no ISIN (\"{isin}\").");

        var shares = Amount(field("shares"), what);
        var price = Amount(field("price"), what);
        if (shares <= 0 || price <= 0 || net >= 0)
            throw new FormatException($"{what} is not a buy: shares, price or amount have the wrong sign.");

        var name = field("name");
        var transactionId = field("transaction_id");
        return new ParsedTrade(
            isin,
            Ticker: null,
            AssetName: name.Length > 0 ? name : $"ETF {isin}",
            date,
            Quantity: shares,
            PricePerUnit: price,
            Fees: Math.Abs(feeAndTax),
            ExternalId: transactionId.Length > 0 ? transactionId : null,
            Note: description.StartsWith("Savings plan execution", StringComparison.Ordinal)
                ? "Trade Republic savings plan"
                : "Trade Republic buy");
    }

    private static decimal Amount(string value, string what)
    {
        if (value.Length == 0) return 0m;
        if (decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount))
            return amount;
        throw new FormatException($"{what} has an amount that is not a number (\"{value}\").");
    }
}
