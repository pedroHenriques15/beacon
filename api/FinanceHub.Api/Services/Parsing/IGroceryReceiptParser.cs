namespace FinanceHub.Api.Services.Parsing;

public interface IGroceryReceiptParser
{
    string ParserName { get; }
    bool CanParse(string fullText);
    ParsedGroceryReceipt Parse(string fileName, IReadOnlyList<string> pages);
}
