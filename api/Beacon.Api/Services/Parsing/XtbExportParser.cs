using System.Globalization;
using System.Text.RegularExpressions;
using static System.FormattableString;

namespace Beacon.Api.Services.Parsing;

/// <summary>A holding listed on an XTB export's Open Positions sheet.</summary>
public record XtbHolding(string Ticker, decimal Quantity);

/// <summary>
/// One XTB account export: its period (Lisbon dates), its trades in time order, and the holdings
/// it listed when it was generated, which is not the period's end.
/// </summary>
public record XtbExport(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateTime GeneratedAtUtc,
    IReadOnlyList<ParsedTrade> Trades,
    IReadOnlyList<XtbHolding> Holdings);

/// <summary>
/// Reads XTB's monthly account export (<c>EUR_&lt;account&gt;_&lt;from&gt;_&lt;to&gt;.xlsx</c>,
/// ADR-034): three sheets, Cash Operations, Closed Positions and Open Positions, each opening with
/// an "Account number" row. XTB is no account in Beacon: the export yields trades, which become
/// investment lots, and the holdings to check them against, never a statement.
///
/// From Cash Operations, a "Stock purchase" is a buy and a "Stock sell" a sell, their quantity and
/// price read from the comment (<c>OPEN BUY 0.5 @ 600.00</c>; a split fill reads
/// <c>OPEN BUY 2/2.5 @ 100.00</c>, the fill being 2) and their operation id kept as the lot's
/// <c>ExternalId</c>. A deposit and a transfer between subaccounts move no investment and are
/// skipped. Anything not seen in a real export is refused rather than guessed: another row type,
/// an instrument other than an ETF, an amount that is not the quantity at the price (a commission,
/// a currency conversion), a position other than a bought one, an account not in EUR (ADR-005).
/// Times are UTC; a trade is dated by its day in Lisbon.
/// </summary>
public partial class XtbExportParser
{
    /// <summary>Starts every XTB lot's <c>ExternalId</c>, so its ids never meet another source's.</summary>
    public const string ExternalIdPrefix = "XTB:";

    private const string CashOperations = "Cash Operations";
    private const string ClosedPositions = "Closed Positions";
    private const string OpenPositions = "Open Positions";

    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    [GeneratedRegex(@"^([A-Z]{3})_\d+_\d{4}-\d{2}-\d{2}_\d{4}-\d{2}-\d{2}")]
    private static partial Regex FileNameRegex();

    [GeneratedRegex(@"^(?<side>OPEN|CLOSE) BUY (?<quantity>\d+(?:\.\d+)?)(?:/\d+(?:\.\d+)?)? @ (?<price>\d+(?:\.\d+)?)$")]
    private static partial Regex CommentRegex();

    public bool CanParse(XlsxWorkbook workbook) =>
        new[] { CashOperations, ClosedPositions, OpenPositions }.All(name =>
            workbook.Sheet(name) is { Rows: [["Account number", ..], ..] });

    /// <summary>The export's trades and holdings. Refuses a file it can't trust (<see cref="FormatException"/>).</summary>
    public XtbExport Parse(string fileName, XlsxWorkbook workbook)
    {
        var currency = FileNameRegex().Match(Path.GetFileName(fileName));
        if (currency.Success && currency.Groups[1].Value != "EUR")
            throw NotEur(currency.Groups[1].Value);

        var cash = workbook.Sheet(CashOperations)!;
        var from = LisbonDate(Utc(Labelled(cash, "Date from (UTC)"), "The period's start"));
        var to = LisbonDate(Utc(Labelled(cash, "Date to (UTC)"), "The period's end"));
        var trades = ReadTrades(cash);

        var open = workbook.Sheet(OpenPositions)!;
        var generatedAt = Utc(Labelled(open, "Data as of report generated"), "The report's time");
        var holdings = ReadHoldings(open);

        return new XtbExport(from, to, generatedAt, trades, holdings);
    }

    /// <summary>A UTC time's day in Lisbon, where the owner trades.</summary>
    public static DateOnly LisbonDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Lisbon));

    private static List<ParsedTrade> ReadTrades(XlsxSheet sheet)
    {
        var (columns, rows) = Table(sheet, row => Cell(row, 0) == "Type",
            ["Type", "Instrument", "Ticker", "Category", "Time", "Amount", "ID", "Comment", "Product"]);

        var trades = new List<(DateTime At, ParsedTrade Trade)>();
        foreach (var row in rows)
        {
            string Field(string name) => Cell(row, columns[name]);

            var type = Field("Type");
            if (type == "Total") break;
            var at = Utc(Field("Time"), $"The time of XTB's \"{type}\" row");
            switch (type)
            {
                case "Deposit":
                case "Subaccount transfer":
                    continue;
                case "Stock purchase":
                case "Stock sell":
                    trades.Add((at, ReadTrade(Field, sell: type == "Stock sell", at)));
                    break;
                default:
                    throw new FormatException(
                        $"XTB's Cash Operations has a \"{type}\" row on {LisbonDate(at):yyyy-MM-dd}; only stock " +
                        "purchases and sells, deposits and subaccount transfers are read.");
            }
        }
        // The sheet lists the newest first; a sell must come after the buys it sells.
        return trades.OrderBy(t => t.At).Select(t => t.Trade).ToList();
    }

    private static ParsedTrade ReadTrade(Func<string, string> field, bool sell, DateTime at)
    {
        var ticker = field("Ticker");
        var date = LisbonDate(at);
        var what = $"XTB's {(sell ? "sell" : "purchase")} of {(ticker.Length > 0 ? ticker : "an instrument")} on {date:yyyy-MM-dd}";

        if (ticker.Length == 0)
            throw new FormatException($"{what} has no ticker.");
        if (field("Category") != "ETF")
            throw new FormatException(
                $"{what} trades a {field("Category")}; only ETFs are imported as investments.");

        var comment = CommentRegex().Match(field("Comment"));
        var side = sell ? "CLOSE" : "OPEN";
        if (!comment.Success || comment.Groups["side"].Value != side)
            throw new FormatException(
                $"{what} has a comment Beacon can't read (\"{field("Comment")}\"), not \"{side} BUY quantity @ price\".");

        var quantity = Number(comment.Groups["quantity"].Value, what);
        var price = Number(comment.Groups["price"].Value, what);
        var amount = Number(field("Amount"), what);
        if (quantity <= 0 || price <= 0 || (sell ? amount <= 0 : amount >= 0))
            throw new FormatException($"{what} has a quantity, price or amount of the wrong sign.");

        // The cash is the quantity at the price, give or take the cent and the quantity's last
        // digit: anything more is a commission or a currency conversion, which no export has shown.
        if (Math.Abs(Math.Abs(amount) - quantity * price) > price * 0.0001m + 0.01m)
            throw new FormatException(Invariant($"{what} moved {Math.Abs(amount):0.00} for {quantity} at {price}; ") +
                "a commission or a currency conversion is not read yet.");

        var id = field("ID");
        if (id.Length == 0)
            throw new FormatException($"{what} has no ID.");

        var instrument = field("Instrument");
        return new ParsedTrade(
            Isin: null,
            Ticker: ticker,
            AssetName: instrument.Length > 0 ? instrument : ticker,
            Date: date,
            Quantity: sell ? -quantity : quantity,
            PricePerUnit: price,
            Fees: 0m,
            ExternalId: ExternalIdPrefix + WholeNumber(id),
            Note: sell ? "XTB sell"
                : field("Product") == "Investment Plans" ? "XTB investment plan"
                : "XTB buy");
    }

    /// <summary>
    /// The bought positions, summed by ticker. The sheet also lists each instrument's total, a row
    /// without a type, which is skipped; one with neither table holds nothing.
    /// </summary>
    private static List<XtbHolding> ReadHoldings(XlsxSheet sheet)
    {
        var summary = sheet.Rows.ToList().FindIndex(r => Cell(r, 0) == "Product" && Cell(r, 1) == "Metric");
        if (summary >= 0)
        {
            var currencyColumn = sheet.Rows[summary].ToList().IndexOf("Currency");
            foreach (var row in sheet.Rows.Skip(summary + 1).TakeWhile(r => Cell(r, 1) != "Instrument/Position"))
                if (currencyColumn >= 0 && Cell(row, currencyColumn) is { Length: > 0 } currency && currency != "EUR")
                    throw NotEur(currency);
        }

        if (!sheet.Rows.Any(r => Cell(r, 1) == "Instrument/Position")) return [];
        var (columns, rows) = Table(sheet, row => Cell(row, 1) == "Instrument/Position", ["Ticker", "Type", "Volume"]);

        var held = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var ticker = Cell(row, columns["Ticker"]);
            switch (Cell(row, columns["Type"]))
            {
                case "":
                    continue;
                case "BUY":
                    var volume = Number(Cell(row, columns["Volume"]), $"XTB's open position in {ticker}");
                    held[ticker] = held.GetValueOrDefault(ticker) + volume;
                    break;
                case var type:
                    throw new FormatException(
                        $"XTB's Open Positions holds a {type} position in {ticker}; only bought positions are read.");
            }
        }
        return held.Select(h => new XtbHolding(h.Key, h.Value)).ToList();
    }

    /// <summary>The column of each header name, and the rows under the header row.</summary>
    private static (Dictionary<string, int> Columns, List<IReadOnlyList<string>> Rows) Table(
        XlsxSheet sheet, Func<IReadOnlyList<string>, bool> isHeader, string[] required)
    {
        var start = sheet.Rows.ToList().FindIndex(r => isHeader(r));
        if (start < 0)
            throw new FormatException($"XTB's {sheet.Name} sheet has no table.");

        var columns = new Dictionary<string, int>();
        for (var i = 0; i < sheet.Rows[start].Count; i++)
            columns.TryAdd(sheet.Rows[start][i].Trim(), i);
        var missing = required.Where(name => !columns.ContainsKey(name)).ToList();
        if (missing.Count > 0)
            throw new FormatException($"XTB's {sheet.Name} sheet has no {string.Join(", ", missing)} column.");

        return (columns, sheet.Rows.Skip(start + 1).ToList());
    }

    /// <summary>The value beside a label in the first column ("Date from (UTC)").</summary>
    private static string Labelled(XlsxSheet sheet, string label) =>
        sheet.Rows.FirstOrDefault(r => Cell(r, 0) == label) is { } row
            ? Cell(row, 1)
            : throw new FormatException($"XTB's {sheet.Name} sheet has no \"{label}\" row.");

    private static string Cell(IReadOnlyList<string> row, int index) =>
        index < row.Count ? row[index].Trim() : "";

    /// <summary>A cell holding a date: Excel's serial number of days, here in UTC.</summary>
    private static DateTime Utc(string serial, string what)
    {
        if (double.TryParse(serial, NumberStyles.Float, CultureInfo.InvariantCulture, out var days)
            && days is > 0 and < 2958466)
            return DateTime.SpecifyKind(DateTime.FromOADate(days), DateTimeKind.Utc);
        throw new FormatException($"{what} is not a date (\"{serial}\").");
    }

    private static decimal Number(string value, string what) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new FormatException($"{what} has a number Beacon can't read (\"{value}\").");

    /// <summary>An id as XTB shows it, even when the cell stored it as a number ("1.23456789E9").</summary>
    private static string WholeNumber(string id) =>
        id.All(char.IsAsciiDigit) ? id
        : decimal.TryParse(id, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n == decimal.Truncate(n)
            ? n.ToString("0", CultureInfo.InvariantCulture)
            : id;

    private static FormatException NotEur(string currency) =>
        new($"This XTB export is for an account in {currency}; Beacon imports EUR only.");
}
