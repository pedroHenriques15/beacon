namespace FinanceHub.Api.Services.Parsing;

public class PlaceholderGroceryParser : IGroceryReceiptParser
{
    public string ParserName => "Placeholder";

    public bool CanParse(string fullText) => true;

    public ParsedGroceryReceipt Parse(string fileName, IReadOnlyList<string> pages)
    {
        return new ParsedGroceryReceipt(
            StoreName: "",
            ReceiptDate: DateOnly.FromDateTime(DateTime.Today),
            Total: 0m,
            Items: []
        );
    }
}
