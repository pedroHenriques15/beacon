using System.Text;
using Beacon.Api.Data;
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

    private UnifiedUploadBatchCommandHandler MakeHandler(AppDbContext db, StubExtractor extractor)
    {
        var bankFactory = new BankStatementParserFactory(
            [new ActivoBankParser(), new BpiParser(), new RevolutParser()]);
        var groceryFactory = new GroceryReceiptParserFactory([new ContinenteParser()]);
        var salaryFactory = new SalarySlipParserFactory([new CentralGestParser(), new DomirestParser()]);
        var statementService = new StatementUploadService(
            db, extractor, bankFactory, _fileStorage, NullLogger<StatementUploadService>.Instance);
        var groceryService = new GroceryReceiptUploadService(
            db, extractor, groceryFactory, _fileStorage, NullLogger<GroceryReceiptUploadService>.Instance);

        return new UnifiedUploadBatchCommandHandler(
            extractor, bankFactory, groceryFactory, salaryFactory,
            statementService, groceryService, _fileStorage,
            NullLogger<UnifiedUploadBatchCommandHandler>.Instance);
    }

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
}
