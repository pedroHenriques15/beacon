namespace Beacon.Api.Models;

/// <summary>
/// A further PDF a salary slip was built from, beside its own <see cref="SalarySlip.PdfPath"/>: a
/// Deel withdrawal confirmation, or the PDF of a second pay run merged into the month (ADR-039).
/// </summary>
public class SalarySlipFile
{
    public int Id { get; set; }
    public int SalarySlipId { get; set; }
    public SalarySlip SalarySlip { get; set; } = null!;
    public string PdfPath { get; set; } = string.Empty;
    public string? SourceFile { get; set; }
}
