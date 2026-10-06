using Beacon.Api.Controllers;

namespace Beacon.Tests.Controllers;

public class UploadControllerTests
{
    [Theory]
    [InlineData("statement.pdf", true)]
    [InlineData("STATEMENT.PDF", true)]
    [InlineData("Extrato de transações.csv", true)]
    [InlineData("export.CSV", true)]
    [InlineData("EUR_10000001_2026-05-31_2026-06-30.xlsx", true)]
    [InlineData("export.xls", false)]
    [InlineData("notes.txt", false)]
    public void IsDocument_TakesPdfsCsvsAndXlsxs(string fileName, bool expected) =>
        Assert.Equal(expected, UploadController.IsDocument(fileName));
}
