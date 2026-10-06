using System.IO.Compression;
using System.Text;
using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class XlsxWorkbookTests
{
    [Fact]
    public void Read_GivesEachSheetsRows_TextResolved_AndCellsInTheirColumns()
    {
        var file = XtbWorkbook.Write(
            ("First", [["a", null, 2.5m], [null], ["b"]]),
            ("Second", [[null, "x"]]));

        var workbook = XlsxWorkbook.Read(file);

        Assert.Equal(["First", "Second"], workbook.Sheets.Select(s => s.Name));
        Assert.Equal([["a", "", "2.5"], ["b"]], workbook.Sheet("First")!.Rows);
        Assert.Equal([["", "x"]], workbook.Sheet("Second")!.Rows);
    }

    [Fact]
    public void Read_AFileThatIsNotAZip_IsRefused()
    {
        var ex = Assert.Throws<FormatException>(() =>
            XlsxWorkbook.Read(new MemoryStream(Encoding.UTF8.GetBytes("not a workbook"))));

        Assert.Contains("not an Excel workbook", ex.Message);
    }

    [Fact]
    public void Read_AZipThatIsNotAWorkbook_IsRefused()
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry("notes.txt").Open()))
            writer.Write("hello");
        zip.Position = 0;

        Assert.Throws<FormatException>(() => XlsxWorkbook.Read(zip));
    }

    [Theory]
    [InlineData("EUR_1_2026-05-31_2026-06-30.xlsx", true)]
    [InlineData("export.XLSX", true)]
    [InlineData("export.xls", false)]
    [InlineData("export.csv", false)]
    public void IsXlsxFile_GoesByTheExtension(string fileName, bool expected) =>
        Assert.Equal(expected, XlsxWorkbook.IsXlsxFile(fileName));
}
