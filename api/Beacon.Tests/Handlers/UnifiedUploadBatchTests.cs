using System.Text;
using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Features.Upload.Commands.UnifiedUploadBatch;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Handlers;

public class UnifiedUploadBatchTests : IDisposable
{
    private readonly string _tempStorageRoot;
    private readonly FileStorageService _fileStorage;

    public UnifiedUploadBatchTests()
    {
        _tempStorageRoot = Path.Combine(Path.GetTempPath(), $"fh_unified_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempStorageRoot);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = _tempStorageRoot })
            .Build();
        _fileStorage = new FileStorageService(config, NullLogger<FileStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempStorageRoot))
            Directory.Delete(_tempStorageRoot, recursive: true);
    }

    private sealed class StubExtractor(IReadOnlyList<string> pages) : IPdfExtractor
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(pages);
        }
    }

    private sealed class PerFileExtractor : IPdfExtractor
    {
        public Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([File.ReadAllText(pdfPath)]);
    }

    private UnifiedUploadBatchCommandHandler MakeHandler(AppDbContext db, IPdfExtractor extractor)
    {
        var bankFactory = new BankStatementParserFactory(
            [new ActivoBankParser(), new BpiParser(), new RevolutParser()]);
        var groceryFactory = new GroceryReceiptParserFactory([new ContinenteParser()]);
        var salaryFactory = new SalarySlipParserFactory([new CentralGestParser(), new DomirestParser()]);
        var statementService = new StatementUploadService(
            db, extractor, bankFactory, _fileStorage,
            new SavingsPlanImportService(db, NullLogger<SavingsPlanImportService>.Instance),
            NullLogger<StatementUploadService>.Instance);
        var groceryService = new GroceryReceiptUploadService(
            db, extractor, groceryFactory, _fileStorage, NullLogger<GroceryReceiptUploadService>.Instance);

        return new UnifiedUploadBatchCommandHandler(
            extractor, bankFactory, groceryFactory, salaryFactory,
            new Micro1InvoiceParser(), new DeelWithdrawalParser(),
            statementService, groceryService, _fileStorage,
            NullLogger<UnifiedUploadBatchCommandHandler>.Instance);
    }

    private static string InvoiceText(string total = "1,541.50", string basePay = "1436.50", string other = "105.00") => $"""
        INVOICE
        BILL TO Micro1 Inc.
        Invoice for work between July 1, 2026 to July 15, 2026
        Other: Project → Titan | Hours → 28.73 | Pay Rate → $50 | Base Pay → ${basePay} | Other → ${other} USD ${total}
        Total USD ${total}
        """;

    private static string WithdrawalText(string source = "1,541.50", string total = "1,328.49") => $"""
        Confirmation Statement
        Deel transaction ID 98712773
        Source amount ${source}
        Exchange fees -$10.79
        Exchange rate 1.00 USD = 0.86788828 EUR
        Total sent €{total}
        """;

    private static AppDbContext CreateDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private const string ActivoBankPage = """
        DEPOSITO A ORDEM: 123456789
        EXTRATO DE 2026/01/01 A 2026/01/31
        MOEDA BASE: EURO
        SALDO INICIAL 1 000.00
        01.01 01.02 TRANSFERENCIA RECEBIDA 500.00 1 500.00
        SALDO FINAL 1 500.00
        ACTVPTPL
        """;

    private static (string, MemoryStream) MakeFile(string name, string content) =>
        (name, new MemoryStream(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public async Task Handle_BankStatement_DetectsImportsAndExtractsOnce()
    {
        var dbName = nameof(Handle_BankStatement_DetectsImportsAndExtractsOnce);
        await using var db = CreateDb(dbName);
        var extractor = new StubExtractor([ActivoBankPage]);
        var handler = MakeHandler(db, extractor);

        var results = await handler.HandleAsync([MakeFile("jan.pdf", "activo-bytes")]);

        var item = Assert.Single(results);
        Assert.Equal("BankStatement", item.DocumentType);
        Assert.True(item.Success);
        Assert.Equal(1, extractor.Calls);

        await using var freshDb = CreateDb(dbName);
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
    }

    [Fact]
    public async Task Handle_UnrecognisedFile_ReturnsUnknownWithoutPersisting()
    {
        var dbName = nameof(Handle_UnrecognisedFile_ReturnsUnknownWithoutPersisting);
        await using var db = CreateDb(dbName);
        var handler = MakeHandler(db, new StubExtractor(["no known signals here"]));

        var results = await handler.HandleAsync([MakeFile("junk.pdf", "junk-bytes")]);

        var item = Assert.Single(results);
        Assert.Equal("Unknown", item.DocumentType);
        Assert.False(item.Success);
        Assert.Contains("not recognised", item.Error);

        await using var freshDb = CreateDb(dbName);
        Assert.Equal(0, await freshDb.MonthlyStatements.CountAsync());
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Handle_MixedBatch_FailedFileDoesNotAbortOthers()
    {
        var dbName = nameof(Handle_MixedBatch_FailedFileDoesNotAbortOthers);
        await using var db = CreateDb(dbName);

        var goodExtractor = new StubExtractor([ActivoBankPage]);
        var handler = MakeHandler(db, goodExtractor);

        var results = await handler.HandleAsync(
            [MakeFile("a.pdf", "bytes-a"), MakeFile("b.pdf", "bytes-a")]);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.True(results[1].WasDuplicate);
        Assert.Contains("already been imported", results[1].Error);
    }

    [Fact]
    public async Task Handle_Micro1InvoicePlusWithdrawal_PairsIntoOneEurSalarySlip()
    {
        var dbName = nameof(Handle_Micro1InvoicePlusWithdrawal_PairsIntoOneEurSalarySlip);
        await using var db = CreateDb(dbName);
        var handler = MakeHandler(db, new PerFileExtractor());

        var results = await handler.HandleAsync(
            [MakeFile("invoice.pdf", InvoiceText()), MakeFile("withdrawal.pdf", WithdrawalText())]);

        // The withdrawal is folded into the invoice's result — one salary slip, no leftover.
        var item = Assert.Single(results);
        Assert.Equal("SalarySlip", item.DocumentType);
        Assert.True(item.Success);
        Assert.Equal("invoice.pdf", item.FileName);

        var parsed = item.SalaryResult!.Parsed;
        Assert.Equal("Micro1", parsed.ParserName);
        Assert.Equal("Micro1 Inc.", parsed.Employer);
        Assert.Equal(1337.85m, parsed.GrossAmount);
        Assert.Equal(1328.49m, parsed.NetAmount);
        Assert.Equal(1246.72m, parsed.LineItems.First(li => li.Description == "Base Pay").Amount);
        Assert.Equal(91.13m, parsed.LineItems.First(li => li.Description == "Other").Amount);
        Assert.Equal(9.36m, parsed.LineItems.First(li => li.Description == "Deel exchange fee").Amount);
        Assert.Null(parsed.Warnings);
    }

    [Fact]
    public async Task Handle_LoneInvoice_IsBlockedNotImported()
    {
        var dbName = nameof(Handle_LoneInvoice_IsBlockedNotImported);
        await using var db = CreateDb(dbName);
        var handler = MakeHandler(db, new PerFileExtractor());

        var results = await handler.HandleAsync([MakeFile("invoice.pdf", InvoiceText())]);

        var item = Assert.Single(results);
        Assert.Equal("Micro1Unpaired", item.DocumentType);
        Assert.False(item.Success);
        Assert.Contains("no matching Deel withdrawal", item.Error);
        Assert.Null(item.SalaryResult);
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Handle_LoneWithdrawal_IsBlockedNotImported()
    {
        var dbName = nameof(Handle_LoneWithdrawal_IsBlockedNotImported);
        await using var db = CreateDb(dbName);
        var handler = MakeHandler(db, new PerFileExtractor());

        var results = await handler.HandleAsync([MakeFile("withdrawal.pdf", WithdrawalText())]);

        var item = Assert.Single(results);
        Assert.Equal("Micro1Unpaired", item.DocumentType);
        Assert.False(item.Success);
        Assert.Contains("no matching micro1 invoice", item.Error);
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Handle_TwoInvoicesSameAmount_AreBlockedAsAmbiguous()
    {
        var dbName = nameof(Handle_TwoInvoicesSameAmount_AreBlockedAsAmbiguous);
        await using var db = CreateDb(dbName);
        var handler = MakeHandler(db, new PerFileExtractor());

        var results = await handler.HandleAsync(
            [MakeFile("inv-a.pdf", InvoiceText()), MakeFile("inv-b.pdf", InvoiceText())]);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("Micro1Unpaired", r.DocumentType));
        Assert.All(results, r => Assert.False(r.Success));
        Assert.All(results, r => Assert.Contains("same USD amount", r.Error));
    }

    [Fact]
    public async Task Handle_BankStatementMentioningMicro1_FallsBackToBankImport()
    {
        // Trips micro1 CanParse ("Micro1 Inc." + "Total USD") but isn't an invoice — must still import as a bank statement.
        var dbName = nameof(Handle_BankStatementMentioningMicro1_FallsBackToBankImport);
        await using var db = CreateDb(dbName);
        var handler = MakeHandler(db, new PerFileExtractor());

        var text = ActivoBankPage + "\nNote: incoming payment from Micro1 Inc. Total USD $1,541.50";
        var results = await handler.HandleAsync([MakeFile("jan.pdf", text)]);

        var item = Assert.Single(results);
        Assert.Equal("BankStatement", item.DocumentType);
        Assert.True(item.Success);

        await using var freshDb = CreateDb(dbName);
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
    }

    [Fact]
    public async Task Handle_MixedBatch_BankStatementPlusMicro1Pair_ImportsEachCorrectly()
    {
        var dbName = nameof(Handle_MixedBatch_BankStatementPlusMicro1Pair_ImportsEachCorrectly);
        await using var db = CreateDb(dbName);
        var handler = MakeHandler(db, new PerFileExtractor());

        var results = await handler.HandleAsync(
        [
            MakeFile("jan.pdf", ActivoBankPage),
            MakeFile("invoice.pdf", InvoiceText()),
            MakeFile("withdrawal.pdf", WithdrawalText()),
        ]);

        Assert.Equal(2, results.Count); // bank statement + paired slip (withdrawal folded in)
        Assert.Contains(results, r => r.DocumentType == "BankStatement" && r.Success);
        Assert.Contains(results, r => r.DocumentType == "SalarySlip" && r.Success);

        await using var freshDb = CreateDb(dbName);
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
    }
}
