namespace FinanceHub.Api.Services.Parsing;

public class BankStatementParserFactory
{
    private readonly Dictionary<string, IBankStatementParser> _parsers;

    public BankStatementParserFactory(IEnumerable<IBankStatementParser> parsers)
        => _parsers = parsers.ToDictionary(p => p.BankName, StringComparer.OrdinalIgnoreCase);

    public IBankStatementParser GetParser(string bankName)
        => _parsers.TryGetValue(bankName, out var p) ? p
            : throw new NotSupportedException($"No parser registered for '{bankName}'. Supported: {string.Join(", ", _parsers.Keys)}");

    public IBankStatementParser DetectParser(string fullText)
        => _parsers.Values.FirstOrDefault(p => p.CanParse(fullText))
            ?? throw new NotSupportedException("Could not detect bank from statement content. Supported banks: " + string.Join(", ", _parsers.Keys));
}
