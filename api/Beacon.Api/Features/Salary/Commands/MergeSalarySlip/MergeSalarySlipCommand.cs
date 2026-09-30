using Beacon.Api.Features.Salary.Commands.CreateSalarySlip;

namespace Beacon.Api.Features.Salary.Commands.MergeSalarySlip;

/// <summary>
/// Folds a second slip's figures into the slip <paramref name="Id"/> instead of creating a new row.
/// Exists for pay cycles that bill more than once per calendar month (micro1/Deel invoices each half
/// of the month), which the one-slip-per-(profile, period) unique index cannot represent.
/// </summary>
public record MergeSalarySlipCommand(
    int Id,
    decimal GrossAmount,
    decimal NetAmount,
    string? Notes,
    string? PdfPath,
    string? SourceFile,
    List<CreateLineItemRequest> LineItems,
    decimal? BaseAmount = null,
    decimal? HoursWorked = null,
    decimal? HourlyRate = null,
    decimal? TotalEspecie = null);
