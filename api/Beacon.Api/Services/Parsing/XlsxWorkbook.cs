using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Beacon.Api.Services.Parsing;

/// <summary>One worksheet: its name and its rows, each a list of cell texts by column (A first).</summary>
public record XlsxSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// An uploaded Excel workbook (.xlsx) as plain text, read with the Open XML SDK (ADR-034). A cell
/// is its text, a shared or inline string resolved; a number or a date is the stored value as
/// written in the file, invariant culture, and a date is an OLE Automation serial (days since
/// 1899-12-30). A missing cell is an empty string, and blank rows are left out.
/// </summary>
public class XlsxWorkbook
{
    public required IReadOnlyList<XlsxSheet> Sheets { get; init; }

    public static bool IsXlsxFile(string fileName) =>
        fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    public XlsxSheet? Sheet(string name) => Sheets.FirstOrDefault(s => s.Name == name);

    /// <summary>Reads every worksheet. Refuses a file that is not an Excel workbook.</summary>
    public static XlsxWorkbook Read(Stream content)
    {
        try
        {
            using var document = SpreadsheetDocument.Open(content, isEditable: false);
            var workbookPart = document.WorkbookPart
                ?? throw new FormatException("The file is not an Excel workbook (.xlsx).");
            var strings = workbookPart.SharedStringTablePart?.SharedStringTable?
                .Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];

            var sheets = new List<XlsxSheet>();
            foreach (var sheet in workbookPart.Workbook?.Sheets?.Elements<Sheet>() ?? [])
            {
                if (sheet.Id?.Value is not { } id || workbookPart.GetPartById(id) is not WorksheetPart part)
                    continue;
                var rows = (part.Worksheet?.Descendants<Row>() ?? [])
                    .Select(row => ReadRow(row, strings))
                    .Where(cells => cells.Any(c => c.Length > 0))
                    .ToList();
                sheets.Add(new XlsxSheet(sheet.Name?.Value ?? "", rows));
            }
            return new XlsxWorkbook { Sheets = sheets };
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException)
        {
            throw new FormatException("The file is not an Excel workbook (.xlsx).", ex);
        }
    }

    private static IReadOnlyList<string> ReadRow(Row row, List<string> strings)
    {
        var cells = new List<string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var column = ColumnIndex(cell.CellReference?.Value) ?? cells.Count;
            while (cells.Count <= column) cells.Add("");
            cells[column] = CellText(cell, strings);
        }
        return cells;
    }

    private static string CellText(Cell cell, List<string> strings)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText ?? "";
        var value = cell.CellValue?.Text ?? "";
        if (cell.DataType?.Value == CellValues.SharedString)
            return int.TryParse(value, out var index) && index >= 0 && index < strings.Count ? strings[index] : "";
        return value;
    }

    /// <summary>"B12" → 1: the column letters, base 26, A being 0. Null without a reference.</summary>
    private static int? ColumnIndex(string? reference)
    {
        var index = 0;
        foreach (var c in (reference ?? "").TakeWhile(char.IsAsciiLetter))
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        return index > 0 ? index - 1 : null;
    }
}
