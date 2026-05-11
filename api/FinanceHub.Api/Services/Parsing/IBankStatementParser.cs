namespace FinanceHub.Api.Services.Parsing;

public interface IBankStatementParser
{
    string BankName { get; }
    bool CanParse(string fullText);
    ParsedStatement Parse(string fileName, IReadOnlyList<string> pages);
}
