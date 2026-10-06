using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Beacon.Tests.Parsing;

/// <summary>
/// Builds synthetic XTB account exports in the real file's shape: the three sheets, their title
/// rows and columns, text as shared strings, numbers and UTC times as numbers. The account,
/// amounts, quantities, prices and ids are invented.
/// </summary>
internal static class XtbWorkbook
{
    public const string FileName = "EUR_10000001_2026-05-31_2026-06-30.xlsx";

    /// <summary>The June 2026 period in UTC, as XTB writes it: Lisbon's midnights, an hour earlier.</summary>
    public static readonly DateTime JuneFrom = new(2026, 5, 31, 23, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime JuneTo = new(2026, 6, 30, 22, 59, 59, DateTimeKind.Utc);
    public static readonly DateTime Generated = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    public static object?[] Purchase(
        DateTime utc, string ticker, decimal amount, string comment, object id,
        string category = "ETF", string product = "Investment Plans", string instrument = "Example S&P 500") =>
        ["Stock purchase", instrument, ticker, category, utc, amount, id, comment, product, "777001"];

    public static object?[] Sell(DateTime utc, string ticker, decimal amount, string comment, object id) =>
        ["Stock sell", "Example All-World", ticker, "ETF", utc, amount, id, comment, "Investment Plans", "777002"];

    public static object?[] Operation(string type, DateTime utc, decimal amount, string id, string comment = "") =>
        [type, "", "", "", utc, amount, id, comment, "My Trades", ""];

    /// <summary>One bought position on the Open Positions sheet.</summary>
    public static object?[] Position(string ticker, decimal volume, string type = "BUY") =>
        ["Investment Plan", "888001", ticker, "", type, volume, volume * 100m, 100m, 90m, Generated];

    /// <summary>
    /// A whole export. <paramref name="positions"/> are listed under one total row per ticker, as
    /// XTB does.
    /// </summary>
    public static MemoryStream Export(
        IEnumerable<object?[]> operations,
        IEnumerable<object?[]>? positions = null,
        DateTime? from = null,
        DateTime? to = null,
        DateTime? generated = null,
        string summaryCurrency = "EUR")
    {
        var period = (string title) => new List<object?[]>
        {
            new object?[] { "Account number", "10000001" },
            new object?[] { title, "" },
            new object?[] { "Date from (UTC)", from ?? JuneFrom },
            new object?[] { "Date to (UTC)", to ?? JuneTo },
        };

        var closed = period("Closed Positions");
        closed.Add(["Instrument", "Ticker", "Category", "Type", "Volume", "Open Price", "Open Time (UTC)",
            "Close Price", "Close Time (UTC)", "Product", "Profit/Loss", "Gross Profit", "Purchase Value",
            "Sale Value", "Stop Loss", "Take Profit", "Commission", "Margin", "Swap", "Rollover",
            "Open Conversion Rate", "Close Conversion Rate", "Close Origin", "Position ID", "Comment"]);
        closed.Add(["Profit/loss"]);

        var ops = operations.ToList();
        var cash = period("Cash Operations");
        cash.Add(["Type", "Instrument", "Ticker", "Category", "Time", "Amount", "ID", "Comment", "Product", "Position ID"]);
        cash.AddRange(ops);
        cash.Add(["Total", null, null, null, null, ops.Sum(o => (decimal)o[5]!)]);

        var held = (positions ?? []).ToList();
        var open = new List<object?[]>
        {
            new object?[] { "Account number", "10000001" },
            new object?[] { "Open Positions", "" },
            new object?[] { "Data as of report generated", generated ?? Generated },
            new object?[] { "Product", "Metric", "Amount", "Currency" },
            new object?[] { "My Trades", "Open position value", 0m, summaryCurrency },
            new object?[] { "Investment Plans", "Open position value", held.Sum(p => (decimal)p[6]!), summaryCurrency },
            new object?[] { "Note", "Summary values and open positions are shown as of the report generation time" },
            new object?[]
            {
                "Product", "Instrument/Position", "Ticker", "Category", "Type", "Volume", "Value", "Current price",
                "Open price", "Open time (UTC)", "Stop Loss", "Take Profit", "Net Profit %", "Net Profit",
                "Gross Profit", "Margin", "Open Commission", "Swap", "Rollover",
            },
        };
        foreach (var ticker in held.GroupBy(p => (string)p[2]!))
        {
            open.Add(["Investment Plan", "Example S&P 500", ticker.Key, "ETF", "", ticker.Sum(p => (decimal)p[5]!)]);
            open.AddRange(ticker);
        }

        return Write(("Closed Positions", closed), ("Cash Operations", cash), ("Open Positions", open));
    }

    /// <summary>Any workbook: each row's values from column A, a null leaving the cell out.</summary>
    public static MemoryStream Write(params (string Name, IReadOnlyList<object?[]> Rows)[] sheets)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheetList = workbookPart.Workbook.AppendChild(new Sheets());
            var strings = new List<string>();

            uint sheetId = 1;
            foreach (var (name, rows) in sheets)
            {
                var data = new SheetData();
                for (var r = 0; r < rows.Count; r++)
                {
                    var row = new Row { RowIndex = (uint)r + 1 };
                    for (var c = 0; c < rows[r].Length; c++)
                        if (rows[r][c] is { } value)
                            row.Append(Cell($"{(char)('A' + c)}{r + 1}", value, strings));
                    data.Append(row);
                }
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new Worksheet(data);
                sheetList.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId++, Name = name });
            }

            var sharedStrings = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStrings.SharedStringTable = new SharedStringTable(strings.Select(s => new SharedStringItem(new Text(s))));
        }
        stream.Position = 0;
        return stream;
    }

    private static Cell Cell(string reference, object value, List<string> strings)
    {
        if (value is string text)
        {
            var index = strings.IndexOf(text);
            if (index < 0)
            {
                strings.Add(text);
                index = strings.Count - 1;
            }
            return new Cell
            {
                CellReference = reference,
                DataType = CellValues.SharedString,
                CellValue = new CellValue(index.ToString(CultureInfo.InvariantCulture)),
            };
        }

        var number = value switch
        {
            DateTime time => time.ToOADate().ToString("R", CultureInfo.InvariantCulture),
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new ArgumentException($"No cell for a {value.GetType().Name}."),
        };
        return new Cell { CellReference = reference, CellValue = new CellValue(number) };
    }
}
