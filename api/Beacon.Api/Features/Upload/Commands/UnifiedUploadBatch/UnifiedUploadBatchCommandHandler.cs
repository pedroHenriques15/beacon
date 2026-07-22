using Beacon.Api.Features.Salary.Commands.ParseSalarySlip;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
namespace Beacon.Api.Features.Upload.Commands.UnifiedUploadBatch;

public class UnifiedUploadBatchCommandHandler(
    PdfExtractorService extractor,
    BankStatementParserFactory bankFactory,
    GroceryReceiptParserFactory groceryFactory,
    SalarySlipParserFactory salaryFactory,
    StatementUploadService statementService,
    GroceryReceiptUploadService groceryService,
    FileStorageService fileStorage,
    ILogger<UnifiedUploadBatchCommandHandler> logger)
{
    public async Task<List<UnifiedUploadItemResult>> HandleAsync(
        IReadOnlyList<(string FileName, MemoryStream Content)> files,
        CancellationToken ct = default)
    {
        var results = new List<UnifiedUploadItemResult>();

        foreach (var (fileName, content) in files)
        {
            var result = await ProcessFileAsync(fileName, content, ct);
            results.Add(result);
        }

        return results;
    }

    private async Task<UnifiedUploadItemResult> ProcessFileAsync(
        string fileName, MemoryStream content, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"beacon_{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var fs = File.Create(tempPath))
            {
                content.Seek(0, SeekOrigin.Begin);
                await content.CopyToAsync(fs, ct);
            }

            IReadOnlyList<string> pages;
            try
            {
                pages = await extractor.ExtractPagesAsync(tempPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to extract text from {File}: {Error}", fileName, ex.Message);
                return new UnifiedUploadItemResult(fileName, "Unknown", false, false,
                    $"Could not read PDF: {ex.Message}", null, null, null);
            }

            var fullText = string.Join("\n", pages);

            try
            {
                bankFactory.DetectParser(fullText);
                content.Seek(0, SeekOrigin.Begin);
                var formFile = BuildFormFile(content, fileName);
                try
                {
                    var result = await statementService.ImportAsync(formFile, pages);
                    logger.LogInformation("Unified upload: {File} detected as BankStatement ({Bank})", fileName, result.Bank);
                    return new UnifiedUploadItemResult(fileName, "BankStatement", result.Imported,
                        !result.Imported && result.TransactionCount == 0, result.Message, result, null, null);
                }
                catch (Exception ex)
                {
                    return new UnifiedUploadItemResult(fileName, "BankStatement", false, false, ex.Message, null, null, null);
                }
            }
            catch (NotSupportedException) { }

            try
            {
                groceryFactory.DetectParser(fullText);
                content.Seek(0, SeekOrigin.Begin);
                var formFile = BuildFormFile(content, fileName);
                try
                {
                    var result = await groceryService.ImportAsync(formFile, pages);
                    logger.LogInformation("Unified upload: {File} detected as GroceryReceipt ({Store})", fileName, result.StoreName);
                    return new UnifiedUploadItemResult(fileName, "GroceryReceipt", !result.WasDuplicate,
                        result.WasDuplicate, null, null, result, null);
                }
                catch (Exception ex)
                {
                    return new UnifiedUploadItemResult(fileName, "GroceryReceipt", false, false, ex.Message, null, null, null);
                }
            }
            catch (NotSupportedException) { }

            var salaryParser = salaryFactory.FindParser(fullText);
            if (salaryParser is not null)
            {
                content.Seek(0, SeekOrigin.Begin);
                var formFile = BuildFormFile(content, fileName);
                string? savedPath = null;
                try
                {
                    savedPath = await fileStorage.SaveAsync(formFile);
                    var slip     = salaryParser.Parse(fileName, pages);
                    var warnings = ParseVerifier.VerifySalarySlip(slip);
                    var parsed = new ParsedSalarySlipResponse(
                        salaryParser.ParserName,
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

                    logger.LogInformation("Unified upload: {File} detected as SalarySlip ({Parser})", fileName, salaryParser.ParserName);
                    return new UnifiedUploadItemResult(fileName, "SalarySlip", true, false, null,
                        null, null, new UnifiedSalaryResult(savedPath, fileName, parsed));
                }
                catch (Exception ex)
                {
                    if (savedPath is not null) fileStorage.Delete(savedPath);
                    logger.LogWarning("Salary slip processing failed for {File}: {Error}", fileName, ex.Message);
                    return new UnifiedUploadItemResult(fileName, "SalarySlip", false, false, ex.Message, null, null, null);
                }
            }

            logger.LogInformation("Unified upload: {File} not recognised by any parser", fileName);
            return new UnifiedUploadItemResult(fileName, "Unknown", false, false,
                "File format not recognised. Supported: ActivoBank, BPI, Revolut statements; Continente receipts; CentralGest, Domirest salary slips.",
                null, null, null);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static FormFile BuildFormFile(MemoryStream content, string fileName) =>
        new(content, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };
}
