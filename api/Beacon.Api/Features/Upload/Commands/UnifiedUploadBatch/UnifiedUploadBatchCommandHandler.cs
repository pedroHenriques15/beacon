using Beacon.Api.Data;
using Beacon.Api.Features.Salary.Commands.ParseSalarySlip;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Microsoft.EntityFrameworkCore;
namespace Beacon.Api.Features.Upload.Commands.UnifiedUploadBatch;

public class UnifiedUploadBatchCommandHandler(
    IPdfExtractor extractor,
    BankStatementParserFactory bankFactory,
    GroceryReceiptParserFactory groceryFactory,
    SalarySlipParserFactory salaryFactory,
    Micro1InvoiceParser micro1Parser,
    DeelWithdrawalParser withdrawalParser,
    MercorStatementParser mercorParser,
    XtbExportParser xtbParser,
    StatementUploadService statementService,
    GroceryReceiptUploadService groceryService,
    XtbUploadService xtbService,
    FileStorageService fileStorage,
    AppDbContext db,
    ILogger<UnifiedUploadBatchCommandHandler> logger)
{
    public async Task<List<UnifiedUploadItemResult>> HandleAsync(
        IReadOnlyList<(string FileName, MemoryStream Content)> files,
        CancellationToken ct = default)
    {
        var slots = new UnifiedUploadItemResult?[files.Count];
        var invoices = new List<PendingInvoice>();
        var withdrawals = new List<PendingWithdrawal>();
        var mercors = new List<PendingMercor>();
        var xtbs = new List<PendingXtb>();

        for (var i = 0; i < files.Count; i++)
        {
            var (fileName, content) = files[i];
            slots[i] = XlsxWorkbook.IsXlsxFile(fileName)
                ? HoldXtb(i, fileName, content, xtbs)
                : await ClassifyAsync(i, fileName, content, invoices, withdrawals, mercors, ct);
        }

        foreach (var (index, result) in await CorrelateAsync(invoices, withdrawals))
            slots[index] = result;

        foreach (var (index, result) in await ImportXtbAsync(xtbs, ct))
            slots[index] = result;

        // After every file is in, so a bank statement in this same upload counts towards the suggestion.
        foreach (var mercor in mercors)
            slots[mercor.Index] = await AskForEurAsync(mercor, ct);

        return slots.Where(r => r is not null).Select(r => r!).ToList();
    }

    private async Task<UnifiedUploadItemResult?> ClassifyAsync(
        int index, string fileName, MemoryStream content,
        List<PendingInvoice> invoices, List<PendingWithdrawal> withdrawals, List<PendingMercor> mercors,
        CancellationToken ct)
    {
        // A CSV export is its own text: it goes to the parsers as the single page, without
        // pdfplumber, and is never a micro1 document (ADR-031).
        if (CsvText.IsCsvFile(fileName))
        {
            string text;
            try
            {
                text = CsvText.Decode(content.ToArray());
            }
            catch (FormatException ex)
            {
                return new UnifiedUploadItemResult(fileName, "Unknown", false, false, ex.Message, null, null, null);
            }
            return await RunStandardCascadeAsync(fileName, content, [text], ct);
        }

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
            else if (mercorParser.CanParse(fullText))
            {
                // The statement's own title, which no other document carries: one that fails to parse
                // is reported with the reason rather than passed to the cascade as unrecognised.
                return await HoldMercorAsync(index, fileName, content, pages, mercors, ct);
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
                var slip = salaryParser.Parse(fileName, pages);
                var warnings = ParseVerifier.VerifySalarySlip(slip);
                var parsed = ParsedSalarySlipResponse.From(salaryParser.ParserName, slip, warnings);

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

        return Unrecognised(fileName);
    }

    private UnifiedUploadItemResult Unrecognised(string fileName)
    {
        logger.LogInformation("Unified upload: {File} not recognised by any parser", fileName);
        return new UnifiedUploadItemResult(fileName, "Unknown", false, false,
            "File format not recognised. Supported: ActivoBank, BPI, Revolut statements (PDF); Trade Republic transaction exports (CSV); XTB account exports (XLSX); Continente receipts; CentralGest, Domirest salary slips; micro1 invoices (paired with a Deel withdrawal); Mercor statements.",
            null, null, null);
    }

    // An XLSX is a broker's export, read in .NET (ADR-034). It is held until every file is in, so
    // the exports of one upload are applied oldest first.
    private UnifiedUploadItemResult? HoldXtb(int index, string fileName, MemoryStream content, List<PendingXtb> xtbs)
    {
        XlsxWorkbook workbook;
        try
        {
            content.Seek(0, SeekOrigin.Begin);
            workbook = XlsxWorkbook.Read(content);
        }
        catch (FormatException ex)
        {
            return new UnifiedUploadItemResult(fileName, "Unknown", false, false, ex.Message, null, null, null);
        }

        if (!xtbParser.CanParse(workbook)) return Unrecognised(fileName);
        try
        {
            xtbs.Add(new PendingXtb(index, fileName, xtbParser.Parse(fileName, workbook)));
            return null;
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "{File} looked like an XTB export but was refused", fileName);
            return new UnifiedUploadItemResult(fileName, "BrokerExport", false, false, ex.Message, null, null, null);
        }
    }

    // Oldest period first, so a sell never comes before the buy it sells; a second download of a
    // month adds nothing. Then the newest export's holdings are compared with Beacon's.
    private async Task<List<(int Index, UnifiedUploadItemResult Result)>> ImportXtbAsync(
        List<PendingXtb> xtbs, CancellationToken ct)
    {
        var results = new List<(int Index, UnifiedUploadItemResult Result)>();
        (PendingXtb Xtb, TradesUploadResult Result)? latest = null;
        foreach (var xtb in xtbs.OrderBy(x => x.Export.PeriodFrom).ThenBy(x => x.Export.GeneratedAtUtc))
        {
            try
            {
                var result = await xtbService.ImportAsync(xtb.Export, ct);
                logger.LogInformation("Unified upload: {File} detected as an XTB export", xtb.FileName);
                results.Add((xtb.Index, TradesItem(xtb.FileName, result)));
                latest = (xtb, result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "XTB import failed for {File}", xtb.FileName);
                results.Add((xtb.Index, new UnifiedUploadItemResult(xtb.FileName, "BrokerExport", false, false,
                    ex.Message, null, null, null)));
            }
        }

        if (latest is { } last)
        {
            IReadOnlyList<string> warnings;
            try
            {
                warnings = await xtbService.CheckHoldingsAsync(last.Xtb.Export, ct);
            }
            catch (Exception ex)
            {
                // The trades are in: a check that can't run must not fail the upload.
                logger.LogError(ex, "Could not compare XTB holdings for {File}", last.Xtb.FileName);
                warnings = [$"The holdings could not be compared with XTB's: {ex.Message}"];
            }
            if (warnings.Count > 0)
                results[results.FindIndex(r => r.Index == last.Xtb.Index)] =
                    (last.Xtb.Index, TradesItem(last.Xtb.FileName, last.Result with { Warnings = warnings }));
        }
        return results;
    }

    private static UnifiedUploadItemResult TradesItem(string fileName, TradesUploadResult result) =>
        new(fileName, "BrokerExport",
            Success: result.Added > 0 || result.TradeCount == 0,
            WasDuplicate: result.TradeCount > 0 && result.Added == 0,
            Error: null, null, null, null, TradesResult: result);

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
            var wd = withdrawals.Where(w => AmountUtils.Round2(w.Withdrawal.SourceAmountUsd) == amount).ToList();

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

            var slip = Micro1Reconciler.Reconcile(invoice.InvoiceUsd, withdrawal.Withdrawal);
            var warnings = ParseVerifier.VerifySalarySlip(slip);
            var parsed = ParsedSalarySlipResponse.From("Micro1", slip, warnings);

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

    // A Mercor statement is in USD and never a slip on its own (ADR-033): it is stored and held until
    // the owner gives the EUR it paid, through POST /api/salary/parse-mercor.
    private async Task<UnifiedUploadItemResult?> HoldMercorAsync(
        int index, string fileName, MemoryStream content, IReadOnlyList<string> pages,
        List<PendingMercor> mercors, CancellationToken ct)
    {
        try
        {
            var statement = mercorParser.Parse(pages);
            content.Seek(0, SeekOrigin.Begin);
            var savedPath = await fileStorage.SaveAsync(BuildFormFile(content, fileName));
            mercors.Add(new PendingMercor(index, fileName, savedPath, statement));
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mercor statement processing failed for {File}", fileName);
            return new UnifiedUploadItemResult(fileName, "SalarySlip", false, false,
                $"Could not read this Mercor statement: {ex.Message}", null, null, null);
        }
    }

    private async Task<UnifiedUploadItemResult> AskForEurAsync(PendingMercor mercor, CancellationToken ct)
    {
        var statement = mercor.Statement;
        IReadOnlyList<MercorPayout> payouts;
        try
        {
            payouts = await FindMercorPayoutsAsync(statement.Period, ct);
        }
        catch (Exception ex)
        {
            // The suggestion is a convenience: without it the owner types the amount.
            logger.LogError(ex, "Could not look up Mercor payouts for {File}", mercor.FileName);
            payouts = [];
        }

        logger.LogInformation("Unified upload: {File} detected as a Mercor statement, waiting for the EUR received",
            mercor.FileName);
        return new UnifiedUploadItemResult(mercor.FileName, "MercorNeedsEur", true, false, null, null, null, null,
            new UnifiedMercorResult(
                mercor.PdfPath, mercor.FileName, statement.Period,
                statement.TotalPayUsd, statement.HoursWorked, statement.PayRateUsd,
                payouts.Count > 0 ? payouts.Sum(p => p.Amount) : null,
                payouts));
    }

    /// <summary>Credits from any bank that name Mercor and fall in the statement's month.</summary>
    private async Task<IReadOnlyList<MercorPayout>> FindMercorPayoutsAsync(DateOnly period, CancellationToken ct)
    {
        var from = new DateOnly(period.Year, period.Month, 1);
        var to = from.AddMonths(1);
        return await db.Transactions
            .AsNoTracking()
            .Where(t => t.Type == "credit"
                && t.DatePosting >= from && t.DatePosting < to
                && t.Description.ToLower().Contains("mercor"))
            .OrderBy(t => t.DatePosting).ThenBy(t => t.Id)
            .Select(t => new MercorPayout(t.DatePosting, t.Statement.Bank, t.Amount))
            .ToListAsync(ct);
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
            ContentType = CsvText.IsCsvFile(fileName) ? "text/csv" : "application/pdf",
        };

    private sealed record PendingInvoice(int Index, string FileName, MemoryStream Content, ParsedSalarySlip InvoiceUsd);
    private sealed record PendingWithdrawal(int Index, string FileName, DeelWithdrawal Withdrawal);
    private sealed record PendingMercor(int Index, string FileName, string PdfPath, MercorStatement Statement);
    private sealed record PendingXtb(int Index, string FileName, XtbExport Export);
}
