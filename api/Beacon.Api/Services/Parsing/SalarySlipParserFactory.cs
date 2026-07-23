namespace Beacon.Api.Services.Parsing;

public class SalarySlipParserFactory(IEnumerable<ISalarySlipParser> parsers)
{
    public ISalarySlipParser? FindParser(string fullText) =>
        parsers.FirstOrDefault(p => p.CanParse(fullText));
}
