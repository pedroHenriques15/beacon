namespace Beacon.Api.Services.Parsing;

public class BankStatementParserFactory
{
    private readonly Dictionary<string, IBankStatementParser> _parsers;
    private readonly List<IBankStatementParser> _ordered;

    public BankStatementParserFactory(IEnumerable<IBankStatementParser> parsers)
    {
        _ordered = parsers.ToList();
        _parsers = _ordered.ToDictionary(p => p.BankName, StringComparer.OrdinalIgnoreCase);
    }

    public IBankStatementParser GetParser(string bankName)
        => _parsers.TryGetValue(bankName, out var p) ? p
            : throw new NotSupportedException($"No parser registered for '{bankName}'. Supported: {string.Join(", ", _parsers.Keys)}");

    public IBankStatementParser DetectParser(string fullText)
        => _ordered.FirstOrDefault(p => p.CanParse(fullText))
            ?? throw new NotSupportedException("Could not detect bank from statement content. Supported banks: " + string.Join(", ", _parsers.Keys));
}
