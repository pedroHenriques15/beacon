using Beacon.Api.Features.Salary.Commands.ParseMercorStatement;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Beacon.Tests.Parsing;
using Xunit;

namespace Beacon.Tests.Handlers;

public class ParseMercorStatementTests
{
    private sealed class StubExtractor(string text) : IPdfExtractor
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>([text]);
        }
    }

    private static ParseMercorStatementCommandHandler MakeHandler(IPdfExtractor extractor) =>
        new(extractor, new MercorStatementParser());

    [Fact]
    public async Task Handle_ReturnsAEurSlipAtTheEurReceived()
    {
        var handler = MakeHandler(new StubExtractor(MercorStatementText.Page()));

        var (result, error) = await handler.HandleAsync(new ParseMercorStatementCommand("stored.pdf", 124.50m));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("Mercor", result.ParserName);
        Assert.Equal("Mercor", result.Employer);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Period);
        Assert.Equal(124.50m, result.GrossAmount);
        Assert.Equal(124.50m, result.NetAmount);
        Assert.Equal(3.63m, result.HoursWorked);
        Assert.Equal(34.30m, result.HourlyRate);
        var line = Assert.Single(result.LineItems);
        Assert.Equal(("Base Pay", 124.50m, "income"), (line.Description, line.Amount, line.ItemType));
        Assert.Null(result.Warnings);
    }

    [Fact]
    public async Task Handle_WithoutEur_IsRefusedBeforeReadingTheFile()
    {
        var extractor = new StubExtractor(MercorStatementText.Page());
        var handler = MakeHandler(extractor);

        var (result, error) = await handler.HandleAsync(new ParseMercorStatementCommand("stored.pdf", 0m));

        Assert.Null(result);
        Assert.Contains("more than zero", error);
        Assert.Equal(0, extractor.Calls);
    }

    [Fact]
    public async Task Handle_AFileThatIsNotAMercorStatement_IsRefused()
    {
        var handler = MakeHandler(new StubExtractor("CentralGest Software\nRecibo de Vencimento"));

        var (result, error) = await handler.HandleAsync(new ParseMercorStatementCommand("stored.pdf", 124.50m));

        Assert.Null(result);
        Assert.Equal("This file is not a Mercor statement.", error);
    }

    [Fact]
    public async Task Handle_AStatementThatDoesNotAddUp_SaysWhy()
    {
        var handler = MakeHandler(new StubExtractor(MercorStatementText.Page(shiftPay: "150.00", totalPay: "150.00")));

        var (result, error) = await handler.HandleAsync(new ParseMercorStatementCommand("stored.pdf", 124.50m));

        Assert.Null(result);
        Assert.Contains("Total Shift Pay", error);
    }
}
