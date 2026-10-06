namespace Beacon.Api.Features.Salary.Commands.ParseMercorStatement;

/// <summary>
/// Converts a stored Mercor statement (<paramref name="PdfPath"/>, resolved under the storage root) to a
/// EUR slip for review, at the <paramref name="EurReceived"/> the owner gives (ADR-033).
/// </summary>
public record ParseMercorStatementCommand(string PdfPath, decimal EurReceived);
