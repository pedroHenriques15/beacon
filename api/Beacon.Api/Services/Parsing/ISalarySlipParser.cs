namespace Beacon.Api.Services.Parsing;

public interface ISalarySlipParser
{
    string ParserName { get; }
    bool CanParse(string fullText);
    ParsedSalarySlip Parse(string fileName, IReadOnlyList<string> pages);
}
