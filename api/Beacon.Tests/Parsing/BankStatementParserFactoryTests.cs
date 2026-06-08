using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class BankStatementParserFactoryTests
{
    private static BankStatementParserFactory CreateFactory()
    {
        var parsers = new IBankStatementParser[]
        {
            new ActivoBankParser(),
            new BpiParser(),
            new RevolutParser()
        };
        return new BankStatementParserFactory(parsers);
    }

    [Theory]
    [InlineData("ACTIVOBANK")]
    [InlineData("activobank")]
    [InlineData("BPI")]
    [InlineData("bpi")]
    [InlineData("REVOLUT")]
    [InlineData("revolut")]
    public void GetParser_ReturnsParserForKnownBankName(string bankName)
    {
        var factory = CreateFactory();
        var parser  = factory.GetParser(bankName);
        Assert.NotNull(parser);
    }

    [Fact]
    public void GetParser_ThrowsForUnknownBankName()
    {
        var factory = CreateFactory();
        Assert.Throws<NotSupportedException>(() => factory.GetParser("UNKNOWN_BANK"));
    }

    [Fact]
    public void GetParser_IsCaseInsensitive()
    {
        var factory = CreateFactory();
        var p1 = factory.GetParser("BPI");
        var p2 = factory.GetParser("bpi");
        Assert.Same(p1, p2);
    }

    [Theory]
    [InlineData("ACTVPTPL",           "ACTIVOBANK")]
    [InlineData("ActivoBank",         "ACTIVOBANK")]
    [InlineData("EXTRATO COMBINADO",  "ACTIVOBANK")]
    [InlineData("BBPIPTPL",           "BPI")]
    [InlineData("EXTRACTO INTEGRADO", "BPI")]
    [InlineData("REVOPTP2",           "REVOLUT")]
    [InlineData("Revolut Bank UAB",   "REVOLUT")]
    public void DetectParser_IdentifiesCorrectBankFromText(string signal, string expectedBank)
    {
        var factory = CreateFactory();
        var parser  = factory.DetectParser($"some content {signal} more content");
        Assert.Equal(expectedBank, parser.BankName);
    }

    [Fact]
    public void DetectParser_ThrowsForUnrecognisedContent()
    {
        var factory = CreateFactory();
        Assert.Throws<NotSupportedException>(() => factory.DetectParser("completely unrelated text"));
    }

    [Fact]
    public void GetParser_ErrorMessage_ListsSupportedBanks()
    {
        var factory = CreateFactory();
        var ex = Assert.Throws<NotSupportedException>(() => factory.GetParser("UNKNOWN"));
        Assert.Contains("ACTIVOBANK", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BPI",        ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOLUT",    ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectParser_ErrorMessage_ListsSupportedBanks()
    {
        var factory = CreateFactory();
        var ex = Assert.Throws<NotSupportedException>(() => factory.DetectParser("nope"));
        Assert.Contains("ACTIVOBANK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Factory_WithEmptyParsers_DetectThrows()
    {
        var factory = new BankStatementParserFactory(Array.Empty<IBankStatementParser>());
        Assert.Throws<NotSupportedException>(() => factory.DetectParser("anything"));
    }
}
