using Beacon.Api.Features.Salary.Commands.ParseSalarySlip;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
namespace Beacon.Api.Features.Upload.Commands.UnifiedUploadBatch;

public class UnifiedUploadBatchCommandHandler(
    IPdfExtractor extractor,
    BankStatementParserFactory bankFactory,
    GroceryReceiptParserFactory groceryFactory,
    SalarySlipParserFactory salaryFactory,
    Micro1InvoiceParser micro1Parser,
    DeelWithdrawalParser withdrawalParser,
    StatementUploadService statementService,
    GroceryReceiptUploadService groceryService,
    FileStorageService fileStorage,
    ILogger<UnifiedUploadBatchCommandHandler> logger)
{
    public async Task<List<UnifiedUploadItemResult>> HandleAsync(
        IReadOnlyList<(string FileName, MemoryStream Content)> files,
        CancellationToken ct = default)
    {
        var slots = new UnifiedUploadItemResult?[files.Count];
        var invoices = new List<PendingInvoice>();
        var withdrawals = new List<PendingWithdrawal>();

        for (var i = 0; i < files.Count; i++)
        {
            var (fileName, content) = files[i];
            slots[i] = await ClassifyAsync(i, fileName, content, invoices, withdrawals, ct);
        }

        foreach (var (index, result) in await CorrelateAsync(invoices, withdrawals))
            slots[index] = result;

        return slots.Where(r => r is not null).Select(r => r!).ToList();
    }

    private async Task<UnifiedUploadItemResult?> ClassifyAsync(
        int index, string fileName, MemoryStream content,
        List<PendingInvoice> invoices, List<PendingWithdrawal> withdrawals,
        CancellationToken ct)
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
                logger.LogWarning(ex, "Failed to extract text from {File}", fileName);
                return new UnifiedUploadItemResult(fileName, "Unknown", false, false,
                    $"Could not read PDF: {ex.Message}", null, null, null);
            }

            var fullText = string.Join("\n", pages);

            if (micro1Parser.CanParse(fullText))
            {
                try
                {
                    var invoiceUsd = micro1Parser.Parse(fileName, pages);
                    invoices.Add(new PendingInvoice(index, fileName, content, invoiceUsd));
                    return null;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{File} looked like a micro1 invoice but did not parse; falling back to standard detection", fileName);
                }
            }
            else if (withdrawalParser.CanParse(fullText))
            {
                try
                {
                    var withdrawal = withdrawalParser.Parse(pages);
                    withdrawals.Add(new PendingWithdrawal(index, fileName, withdrawal));
                    return null;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{File} looked like a Deel withdrawal but did not parse; falling back to standard detection", fileName);
                }
            }

            return await RunStandardCascadeAsync(fileName, content, pages, ct);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private async Task<UnifiedUploadItemResult> RunStandardCascadeAsync(
        string fileName, MemoryStream content, IReadOnlyList<string> pages, CancellationToken ct)
    {
        try
        {
            bankFactory.DetectParser(string.Join("\n", pages));
            content.Seek(0, SeekOrigin.Begin);
            var formFile = BuildFormFile(content, fileName);
            try
            {
                var result = await statementService.ImportAsync(formFile, pages, ct);
                logger.LogInformation("Unified upload: {File} detected as BankStatement ({Bank})", fileName, result.Bank);
                return new UnifiedUploadItemResult(fileName, "BankStatement", result.Imported,
                    !result.Imported && result.TransactionCount == 0, result.Message, result, null, null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Bank statement import failed for {File}", fileName);
                return new UnifiedUploadItemResult(fileName, "BankStatement", false, false, ex.Message, null, null, null);
            }
        }
        catch (NotSupportedException) { }

        try
        {
            groceryFactory.DetectParser(string.Join("\n", pages));
            content.Seek(0, SeekOrigin.Begin);
            var formFile = BuildFormFile(content, fileName);
            try
            {
                var result = await groceryService.ImportAsync(formFile, pages, ct);
                logger.LogInformation("Unified upload: {File} detected as GroceryReceipt ({Store})", fileName, result.StoreName);
                return new UnifiedUploadItemResult(fileName, "GroceryReceipt", !result.WasDuplicate,
                    result.WasDuplicate, null, null, result, null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Grocery receipt import failed for {File}", fileName);
                return new UnifiedUploadItemResult(fileName, "GroceryReceipt", false, false, ex.Message, null, null, null);
            }
        }
        catch (NotSupportedException) { }

        var salaryParser = salaryFactory.FindParser(string.Join("\n", pages));
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
                var parsed   = ParsedSalarySlipResponse.From(salaryParser.ParserName, slip, warnings);

                logger.LogInformation("Unified upload: {File} detected as SalarySlip ({Parser})", fileName, salaryParser.ParserName);
                return new UnifiedUploadItemResult(fileName, "SalarySlip", true, false, null,
                    null, null, new UnifiedSalaryResult(savedPath, fileName, parsed));
            }
            catch (Exception ex)
            {
                if (savedPath is not null) fileStorage.Delete(savedPath);
                logger.LogError(ex, "Salary slip processing failed for {File}", fileName);
                return new UnifiedUploadItemResult(fileName, "SalarySlip", false, false, ex.Message, null, null, null);
            }
        }

        logger.LogInformation("Unified upload: {File} not recognised by any parser", fileName);
        return new UnifiedUploadItemResult(fileName, "Unknown", false, false,
            "File format not recognised. Supported: ActivoBank, BPI, Revolut statements; Continente receipts; CentralGest, Domirest salary slips; micro1 invoices (paired with a Deel withdrawal).",
            null, null, null);
    }

    private async Task<List<(int Index, UnifiedUploadItemResult Result)>> CorrelateAsync(
        List<PendingInvoice> invoices, List<PendingWithdrawal> withdrawals)
    {
        var results = new List<(int, UnifiedUploadItemResult)>();

        var amounts = invoices.Select(i => AmountUtils.Round2(i.InvoiceUsd.GrossAmount))
            .Concat(withdrawals.Select(w => AmountUtils.Round2(w.Withdrawal.SourceAmountUsd)))
            .Distinct();

        foreach (var amount in amounts)
        {
            var inv = invoices.Where(i => AmountUtils.Round2(i.InvoiceUsd.GrossAmount) == amount).ToList();
            var wd  = withdrawals.Where(w => AmountUtils.Round2(w.Withdrawal.SourceAmountUsd) == amount).ToList();

            if (inv.Count == 1 && wd.Count == 1)
            {
                results.AddRange(await ReconcileAsync(inv[0], wd[0]));
                continue;
            }

            var ambiguous = inv.Count > 1 || wd.Count > 1;
            foreach (var i in inv)
                results.Add((i.Index, Blocked(i.FileName, InvoiceBlockReason(ambiguous))));
            foreach (var w in wd)
                results.Add((w.Index, Blocked(w.FileName, WithdrawalBlockReason(ambiguous))));
        }

        return results;
    }

    // On success the withdrawal is folded into the invoice's SalarySlip row (one result per pair, by design).
    // On failure both files get their own blocked row, so an uploaded PDF never silently vanishes from results.
    private async Task<List<(int, UnifiedUploadItemResult)>> ReconcileAsync(
        PendingInvoice invoice, PendingWithdrawal withdrawal)
    {
        string? savedPath = null;
        try
        {
            invoice.Content.Seek(0, SeekOrigin.Begin);
            var formFile = BuildFormFile(invoice.Content, invoice.FileName);
            savedPath = await fileStorage.SaveAsync(formFile);

            var slip     = Micro1Reconciler.Reconcile(invoice.InvoiceUsd, withdrawal.Withdrawal);
            var warnings = ParseVerifier.VerifySalarySlip(slip);
            var parsed   = ParsedSalarySlipResponse.From("Micro1", slip, warnings);

            logger.LogInformation("Unified upload: paired micro1 invoice {Invoice} + Deel withdrawal {Withdrawal}",
                invoice.FileName, withdrawal.FileName);

            return [(invoice.Index, new UnifiedUploadItemResult(invoice.FileName, "SalarySlip", true, false, null,
                null, null, new UnifiedSalaryResult(savedPath, invoice.FileName, parsed)))];
        }
        catch (Exception ex)
        {
            if (savedPath is not null) fileStorage.Delete(savedPath);
            logger.LogError(ex, "micro1 reconciliation failed for {File} + {Withdrawal}", invoice.FileName, withdrawal.FileName);
            var reason = $"Failed to combine this micro1 paycheck's two PDFs: {ex.Message}";
            return
            [
                (invoice.Index, Blocked(invoice.FileName, reason)),
                (withdrawal.Index, Blocked(withdrawal.FileName, reason)),
            ];
        }
    }

    private static string InvoiceBlockReason(bool ambiguous) => ambiguous
        ? "Multiple micro1 files in this upload share the same USD amount, so they can't be paired unambiguously. Upload each paycheck (its invoice + Deel withdrawal) in a separate batch."
        : "This micro1 invoice has no matching Deel withdrawal in this upload. Add the withdrawal statement and upload both together.";

    private static string WithdrawalBlockReason(bool ambiguous) => ambiguous
        ? "Multiple micro1 files in this upload share the same USD amount, so they can't be paired unambiguously. Upload each paycheck (its invoice + Deel withdrawal) in a separate batch."
        : "This Deel withdrawal has no matching micro1 invoice in this upload. Add the invoice and upload both together.";

    private static UnifiedUploadItemResult Blocked(string fileName, string reason) =>
        new(fileName, "Micro1Unpaired", false, false, reason, null, null, null);

    private static FormFile BuildFormFile(MemoryStream content, string fileName) =>
        new(content, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    private sealed record PendingInvoice(int Index, string FileName, MemoryStream Content, ParsedSalarySlip InvoiceUsd);
    private sealed record PendingWithdrawal(int Index, string FileName, DeelWithdrawal Withdrawal);
}
