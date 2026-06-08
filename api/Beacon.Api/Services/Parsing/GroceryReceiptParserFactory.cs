namespace Beacon.Api.Services.Parsing;

public class GroceryReceiptParserFactory
{
    private readonly IReadOnlyList<IGroceryReceiptParser> _parsers;

    public GroceryReceiptParserFactory(IEnumerable<IGroceryReceiptParser> parsers)
        => _parsers = parsers.ToList();

    public IGroceryReceiptParser DetectParser(string fullText)
        => _parsers.FirstOrDefault(p => p.CanParse(fullText))
            ?? throw new NotSupportedException("No grocery receipt parser could handle this document.");
}
