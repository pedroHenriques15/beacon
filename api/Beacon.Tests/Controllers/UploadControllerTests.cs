using Beacon.Api.Controllers;

namespace Beacon.Tests.Controllers;

public class UploadControllerTests
{
    [Theory]
    [InlineData("statement.pdf", true)]
    [InlineData("STATEMENT.PDF", true)]
    [InlineData("Extrato de transações.csv", true)]
    [InlineData("export.CSV", true)]
    [InlineData("export.xlsx", false)]
    [InlineData("notes.txt", false)]
    public void IsDocument_TakesPdfsAndCsvs(string fileName, bool expected) =>
        Assert.Equal(expected, UploadController.IsDocument(fileName));
}
