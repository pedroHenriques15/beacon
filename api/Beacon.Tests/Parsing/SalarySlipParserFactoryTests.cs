using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class SalarySlipParserFactoryTests
{
    private static SalarySlipParserFactory CreateFactory() =>
        new([new DomirestParser(), new CentralGestParser()]);

    [Fact]
    public void FindParser_ReturnsDomirestParser_ForDomirestText()
    {
        var factory = CreateFactory();
        var parser  = factory.FindParser("some text DOMIREST RECIBO DE REMUNERAÇÕES");

        Assert.NotNull(parser);
        Assert.Equal("Domirest", parser.ParserName);
    }

    [Fact]
    public void FindParser_ReturnsCentralGestParser_ForCentralGestText()
    {
        var factory = CreateFactory();
        var parser  = factory.FindParser("Recibo de Remuneração CentralGest Software - RECIBA5.RPT");

        Assert.NotNull(parser);
        Assert.Equal("CentralGest", parser.ParserName);
    }

    [Fact]
    public void FindParser_ReturnsNull_ForUnknownText()
    {
        var factory = CreateFactory();
        var parser  = factory.FindParser("This is a completely unrelated document");

        Assert.Null(parser);
    }

    [Fact]
    public void FindParser_ReturnsNull_ForEmptyText()
    {
        var factory = CreateFactory();
        var parser  = factory.FindParser(string.Empty);

        Assert.Null(parser);
    }

    [Fact]
    public void FindParser_EmptyFactory_ReturnsNull()
    {
        var factory = new SalarySlipParserFactory(Array.Empty<ISalarySlipParser>());
        var parser  = factory.FindParser("DOMIREST anything");

        Assert.Null(parser);
    }

    [Theory]
    [InlineData("DOMIREST", "Domirest")]
    [InlineData("CentralGest Software", "CentralGest")]
    public void FindParser_ReturnsCorrectParserForSignalText(string signal, string expectedParserName)
    {
        var factory = CreateFactory();
        var parser  = factory.FindParser($"prefix {signal} suffix");

        Assert.NotNull(parser);
        Assert.Equal(expectedParserName, parser.ParserName);
    }

    [Fact]
    public void FindParser_WithBothSignals_ReturnsFirstRegisteredParser()
    {
        var factory = CreateFactory();
        var parser  = factory.FindParser("DOMIREST CentralGest Software mixed document");

        Assert.NotNull(parser);
        Assert.Equal("Domirest", parser.ParserName);
    }
}
