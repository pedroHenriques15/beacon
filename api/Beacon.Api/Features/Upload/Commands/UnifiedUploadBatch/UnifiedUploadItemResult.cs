using Beacon.Api.Features.Salary.Commands.ParseSalarySlip;
using Beacon.Api.Services;

namespace Beacon.Api.Features.Upload.Commands.UnifiedUploadBatch;

public record UnifiedSalaryResult(
    string PdfPath,
    string FileName,
    ParsedSalarySlipResponse Parsed);

/// <summary>
/// A Mercor statement, read and stored but not a slip yet: its USD figures, and the EUR to suggest,
/// the sum of the Mercor credits imported for its month (ADR-033).
/// </summary>
public record UnifiedMercorResult(
    string PdfPath,
    string FileName,
    DateOnly Period,
    decimal TotalPayUsd,
    decimal HoursWorked,
    decimal PayRateUsd,
    decimal? SuggestedEur,
    IReadOnlyList<MercorPayout> Payouts);

public record MercorPayout(DateOnly Date, string Bank, decimal Amount);

public record UnifiedUploadItemResult(
    string FileName,
    string DocumentType,
    bool Success,
    bool WasDuplicate,
    string? Error,
    UploadResult? StatementResult,
    GroceryReceiptUploadResult? GroceryResult,
    UnifiedSalaryResult? SalaryResult,
    UnifiedMercorResult? MercorResult = null,
    TradesUploadResult? TradesResult = null);
