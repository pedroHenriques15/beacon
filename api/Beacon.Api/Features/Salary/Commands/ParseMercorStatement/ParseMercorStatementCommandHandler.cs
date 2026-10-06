using Beacon.Api.Features.Salary.Commands.ParseSalarySlip;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;

namespace Beacon.Api.Features.Salary.Commands.ParseMercorStatement;

public class ParseMercorStatementCommandHandler(
    IPdfExtractor extractor,
    MercorStatementParser parser)
{
    public async Task<(ParsedSalarySlipResponse? Result, string? Error)> HandleAsync(
        ParseMercorStatementCommand command, CancellationToken ct = default)
    {
        if (command.EurReceived <= 0m)
            return (null, "Enter the EUR this Mercor statement paid; it must be more than zero.");

        IReadOnlyList<string> pages;
        try
        {
            pages = await extractor.ExtractPagesAsync(command.PdfPath, ct);
        }
        catch (Exception ex)
        {
            return (null, $"PDF extraction failed: {ex.Message}");
        }

        if (!parser.CanParse(string.Join("\n", pages)))
            return (null, "This file is not a Mercor statement.");

        try
        {
            var slip = MercorReconciler.Reconcile(parser.Parse(pages), command.EurReceived);
            var warnings = ParseVerifier.VerifySalarySlip(slip);
            return (ParsedSalarySlipResponse.From("Mercor", slip, warnings), null);
        }
        catch (Exception ex)
        {
            return (null, $"Parsing failed: {ex.Message}");
        }
    }
}
