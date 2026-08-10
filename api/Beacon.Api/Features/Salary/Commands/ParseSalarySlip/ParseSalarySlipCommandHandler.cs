using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;

namespace Beacon.Api.Features.Salary.Commands.ParseSalarySlip;

public record ParseSalarySlipCommand(string PdfPath);

public record ParsedSalarySlipResponse(
    string ParserName,
    string Employer,
    string? EmployerNif,
    DateOnly Period,
    decimal GrossAmount,
    decimal NetAmount,
    List<ParsedSalaryLineItemResponse> LineItems,
    decimal? BaseAmount,
    decimal? HoursWorked,
    decimal? HourlyRate,
    decimal? TotalEspecie,
    IReadOnlyList<string>? Warnings = null)
{
    /// <summary>Maps a parsed slip (+ its verification warnings) to the API response DTO. Shared by the
    /// salary parse endpoint and the unified upload handler so the two never drift out of shape.</summary>
    public static ParsedSalarySlipResponse From(
        string parserName, ParsedSalarySlip slip, IReadOnlyList<string> warnings) =>
        new(parserName,
            slip.Employer,
            slip.EmployerNif,
            slip.Period,
            slip.GrossAmount,
            slip.NetAmount,
            slip.LineItems
                .Select(li => new ParsedSalaryLineItemResponse(
                    li.Description, li.Amount, li.ItemType,
                    li.Quantity, li.UnitValue, li.Percentage, li.IncidenciaBase))
                .ToList(),
            slip.BaseAmount,
            slip.HoursWorked,
            slip.HourlyRate,
            slip.TotalEspecie,
            warnings.Count > 0 ? warnings : null);
}

public record ParsedSalaryLineItemResponse(
    string Description,
    decimal Amount,
    string ItemType,
    decimal? Quantity,
    decimal? UnitValue,
    decimal? Percentage,
    decimal? IncidenciaBase);

public class ParseSalarySlipCommandHandler(
    IPdfExtractor extractor,
    SalarySlipParserFactory factory)
{
    public async Task<(ParsedSalarySlipResponse? Result, string? Error)> HandleAsync(
        ParseSalarySlipCommand command, CancellationToken ct = default)
    {
        IReadOnlyList<string> pages;
        try
        {
            pages = await extractor.ExtractPagesAsync(command.PdfPath);
        }
        catch (Exception ex)
        {
            return (null, $"PDF extraction failed: {ex.Message}");
        }

        var fullText = string.Join("\n", pages);
        var parser   = factory.FindParser(fullText);

        if (parser is null)
            return (null, "No salary slip parser recognised this PDF format.");

        try
        {
            var slip     = parser.Parse(Path.GetFileName(command.PdfPath), pages);
            var warnings = ParseVerifier.VerifySalarySlip(slip);
            var response = ParsedSalarySlipResponse.From(parser.ParserName, slip, warnings);
            return (response, null);
        }
        catch (Exception ex)
        {
            return (null, $"Parsing failed: {ex.Message}");
        }
    }
}
