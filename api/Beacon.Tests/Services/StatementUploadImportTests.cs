using System.Text;
using Beacon.Api.Data;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Services;

public class StatementUploadImportTests : IDisposable
{
    private readonly string _tempStorageRoot;
    private readonly FileStorageService _fileStorage;
    private readonly BankStatementParserFactory _parserFactory;

    public StatementUploadImportTests()
    {
        _tempStorageRoot = Path.Combine(Path.GetTempPath(), $"fh_upload_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempStorageRoot);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = _tempStorageRoot })
            .Build();
        _fileStorage = new FileStorageService(config, NullLogger<FileStorageService>.Instance);
        _parserFactory = new BankStatementParserFactory(
            [new ActivoBankParser(), new BpiParser(), new RevolutParser()]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempStorageRoot))
            Directory.Delete(_tempStorageRoot, recursive: true);
    }

    private sealed class StubExtractor(IReadOnlyList<string> pages) : IPdfExtractor
    {
        public Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken ct = default)
            => Task.FromResult(pages);
    }

    private static DbContextOptions<AppDbContext> DbOptions(string dbName) =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

    private StatementUploadService MakeService(AppDbContext db, IReadOnlyList<string> pages) =>
        new(db, new StubExtractor(pages), _parserFactory, _fileStorage,
            NullLogger<StatementUploadService>.Instance);

    private static FormFile MakeFormFile(string content, string fileName = "statement.pdf")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var ms = new MemoryStream(bytes);
        return new FormFile(ms, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };
    }

    private static string ActivoBankPage(string currency = "EURO") => $"""
        DEPOSITO A ORDEM: 123456789
        EXTRATO DE 2026/01/01 A 2026/01/31
        MOEDA BASE: {currency}
        SALDO INICIAL 1 000.00
        01.01 01.02 TRANSFERENCIA RECEBIDA 500.00 1 500.00
        01.15 01.15 PAGAMENTO SERVICOS 200.00 1 300.00
        SALDO FINAL 1 300.00
        ACTVPTPL
        """;

    [Fact]
    public async Task Import_ActivoBank_PersistsStatementTransactionsAndFile()
    {
        var dbName = nameof(Import_ActivoBank_PersistsStatementTransactionsAndFile);
        await using var db = new AppDbContext(DbOptions(dbName));
        var service = MakeService(db, [ActivoBankPage()]);

        var result = await service.ImportAsync(MakeFormFile("activo-file-1"));

        Assert.True(result.Imported);
        Assert.Equal("ACTIVOBANK", result.Bank);
        Assert.Equal(2, result.TransactionCount);

        await using var freshDb = new AppDbContext(DbOptions(dbName));
        var stmt = await freshDb.MonthlyStatements.Include(s => s.Transactions).SingleAsync();
        Assert.Equal(2, stmt.Transactions.Count);
        Assert.Equal(1300.00m, stmt.ClosingBalance);
        Assert.NotNull(stmt.PdfPath);
        Assert.True(File.Exists(stmt.PdfPath));
    }

    [Fact]
    public async Task Import_SameBytesTwice_RejectsByHash()
    {
        var dbName = nameof(Import_SameBytesTwice_RejectsByHash);
        await using var db = new AppDbContext(DbOptions(dbName));
        var service = MakeService(db, [ActivoBankPage()]);

        await service.ImportAsync(MakeFormFile("identical-bytes"));
        var second = await service.ImportAsync(MakeFormFile("identical-bytes"));

        Assert.False(second.Imported);
        Assert.Contains("already been imported", second.Message);

        await using var freshDb = new AppDbContext(DbOptions(dbName));
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
    }

    [Fact]
    public async Task Import_SamePeriodDifferentBytes_RejectsAsDuplicate()
    {
        var dbName = nameof(Import_SamePeriodDifferentBytes_RejectsAsDuplicate);
        await using var db = new AppDbContext(DbOptions(dbName));
        var service = MakeService(db, [ActivoBankPage()]);

        await service.ImportAsync(MakeFormFile("first-bytes"));
        var second = await service.ImportAsync(MakeFormFile("different-bytes"));

        Assert.False(second.Imported);
        Assert.Contains("already exists", second.Message);

        await using var freshDb = new AppDbContext(DbOptions(dbName));
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
        Assert.Single(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Import_UnrecognisedContent_Throws_AndPersistsNothing()
    {
        var dbName = nameof(Import_UnrecognisedContent_Throws_AndPersistsNothing);
        await using var db = new AppDbContext(DbOptions(dbName));
        var service = MakeService(db, ["completely unrelated text with no bank signals"]);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.ImportAsync(MakeFormFile("junk")));

        await using var freshDb = new AppDbContext(DbOptions(dbName));
        Assert.Equal(0, await freshDb.MonthlyStatements.CountAsync());
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Import_NonEurStatement_Throws_AndPersistsNothing()
    {
        var dbName = nameof(Import_NonEurStatement_Throws_AndPersistsNothing);
        await using var db = new AppDbContext(DbOptions(dbName));
        var service = MakeService(db, [ActivoBankPage(currency: "USD")]);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => service.ImportAsync(MakeFormFile("usd-file")));

        Assert.Contains("EUR", ex.Message);
        await using var freshDb = new AppDbContext(DbOptions(dbName));
        Assert.Equal(0, await freshDb.MonthlyStatements.CountAsync());
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }
}
