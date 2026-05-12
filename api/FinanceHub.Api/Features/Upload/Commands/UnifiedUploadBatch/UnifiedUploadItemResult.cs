using FinanceHub.Api.Features.Salary.Commands.ParseSalarySlip;
using FinanceHub.Api.Services;

namespace FinanceHub.Api.Features.Upload.Commands.UnifiedUploadBatch;

public record UnifiedSalaryResult(
    string PdfPath,
    string FileName,
    ParsedSalarySlipResponse Parsed);

public record UnifiedUploadItemResult(
    string FileName,
    string DocumentType,
    bool Success,
    bool WasDuplicate,
    string? Error,
    UploadResult? StatementResult,
    GroceryReceiptUploadResult? GroceryResult,
    UnifiedSalaryResult? SalaryResult);
