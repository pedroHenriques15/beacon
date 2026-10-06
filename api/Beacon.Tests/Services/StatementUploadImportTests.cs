using System.Text;
using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Features.Shared;
using Beacon.Api.Models;
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
    private readonly SqliteTestDatabase _database = new();

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
            [new ActivoBankParser(), new BpiParser(), new RevolutParser(), new TradeRepublicParser()]);
    }

    public void Dispose()
    {
        _database.Dispose();
        if (Directory.Exists(_tempStorageRoot))
            Directory.Delete(_tempStorageRoot, recursive: true);
    }

    private sealed class StubExtractor(IReadOnlyList<string> pages) : IPdfExtractor
    {
        public Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken ct = default)
            => Task.FromResult(pages);
    }

    private DbContextOptions<AppDbContext> DbOptions() => _database.Options;

    private StatementUploadService MakeService(AppDbContext db, IReadOnlyList<string> pages) =>
        new(db, new StubExtractor(pages), _parserFactory, _fileStorage,
            new SavingsPlanImportService(db, TestPricing.Queue(), NullLogger<SavingsPlanImportService>.Instance),
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
        await using var db = new AppDbContext(DbOptions());
        var service = MakeService(db, [ActivoBankPage()]);

        var result = await service.ImportAsync(MakeFormFile("activo-file-1"));

        Assert.True(result.Imported);
        Assert.Equal("ACTIVOBANK", result.Bank);
        Assert.Equal(2, result.TransactionCount);

        await using var freshDb = new AppDbContext(DbOptions());
        var stmt = await freshDb.MonthlyStatements.Include(s => s.Transactions).SingleAsync();
        Assert.Equal(2, stmt.Transactions.Count);
        Assert.Equal(1300.00m, stmt.ClosingBalance);
        Assert.NotNull(stmt.PdfPath);
        Assert.Equal(Path.GetFileName(stmt.PdfPath), stmt.PdfPath);
        Assert.True(File.Exists(_fileStorage.GetFullPath(stmt.PdfPath)));
    }

    private static async Task<Category> SeedExcludedRuleAsync(AppDbContext db, string pattern)
    {
        var excluded = new Category { Name = ExcludedCategory.Name, Color = "#64748b", IsProtected = true };
        db.Categories.Add(excluded);
        await db.SaveChangesAsync();
        db.CategoryRules.Add(new CategoryRule { CategoryId = excluded.Id, Pattern = pattern });
        await db.SaveChangesAsync();
        return excluded;
    }

    [Fact]
    public async Task Import_RuleToExcludedCategory_StoresTheRowExcluded()
    {
        await using var db = new AppDbContext(DbOptions());
        var excluded = await SeedExcludedRuleAsync(db, "TRANSFERENCIA RECEBIDA");
        var service = MakeService(db, [ActivoBankPage()]);

        var result = await service.ImportAsync(MakeFormFile("activo-excluded-rule"));

        Assert.True(result.Imported);
        await using var freshDb = new AppDbContext(DbOptions());
        var txs = await freshDb.Transactions.ToListAsync();
        var transfer = txs.Single(t => t.Description.Contains("TRANSFERENCIA RECEBIDA"));
        Assert.Equal(excluded.Id, transfer.CategoryId);
        Assert.True(transfer.IsExcluded);
        Assert.False(txs.Single(t => t.Description.Contains("PAGAMENTO SERVICOS")).IsExcluded);
    }

    private static string BpiPageWithPpr() => """
        EXTRACTO INTEGRADO
        IBAN: PT50 0000 0000 0000 0000 0000 0
        Período De 01/02/2026 a 28/02/2026
        SALDO ANTERIOR CONTABILISTICO 1 000,00
        SALDO ACTUAL CONTABILISTICO 1 000,00
        ACTIVOS 2 100,00
        DEPÓSITOS À ORDEM
        PLANOS DE POUPANÇA
        """;

    [Fact]
    public async Task Import_BpiPprRowMatchedToExcluded_StoresItExcluded()
    {
        await using var db = new AppDbContext(DbOptions());
        var excluded = await SeedExcludedRuleAsync(db, "BPI Reforma");
        db.MonthlyStatements.Add(new MonthlyStatement
        {
            Bank = "BPI",
            Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1),
            PeriodTo = new DateOnly(2026, 1, 31),
            PprBalance = 1000m,
        });
        await db.SaveChangesAsync();
        var service = MakeService(db, [BpiPageWithPpr()]);

        var result = await service.ImportAsync(MakeFormFile("bpi-ppr-file"));

        Assert.True(result.Imported);
        await using var freshDb = new AppDbContext(DbOptions());
        var synthetic = await freshDb.Transactions.SingleAsync(t => t.Description == "BPI Reforma - Ganhos");
        Assert.Equal(100m, synthetic.Amount);
        Assert.Equal(excluded.Id, synthetic.CategoryId);
        Assert.True(synthetic.IsExcluded);
    }

    [Fact]
    public async Task Import_SameBytesTwice_RejectsByHash()
    {
        await using var db = new AppDbContext(DbOptions());
        var service = MakeService(db, [ActivoBankPage()]);

        await service.ImportAsync(MakeFormFile("identical-bytes"));
        var second = await service.ImportAsync(MakeFormFile("identical-bytes"));

        Assert.False(second.Imported);
        Assert.Contains("already been imported", second.Message);

        await using var freshDb = new AppDbContext(DbOptions());
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
    }

    [Fact]
    public async Task Import_SamePeriodDifferentBytes_RejectsAsDuplicate()
    {
        await using var db = new AppDbContext(DbOptions());
        var service = MakeService(db, [ActivoBankPage()]);

        await service.ImportAsync(MakeFormFile("first-bytes"));
        var second = await service.ImportAsync(MakeFormFile("different-bytes"));

        Assert.False(second.Imported);
        Assert.Contains("already exists", second.Message);

        await using var freshDb = new AppDbContext(DbOptions());
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
        Assert.Single(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Import_UnrecognisedContent_Throws_AndPersistsNothing()
    {
        await using var db = new AppDbContext(DbOptions());
        var service = MakeService(db, ["completely unrelated text with no bank signals"]);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.ImportAsync(MakeFormFile("junk")));

        await using var freshDb = new AppDbContext(DbOptions());
        Assert.Equal(0, await freshDb.MonthlyStatements.CountAsync());
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }

    private static string TradeRepublicPage() => """
        TRADE REPUBLIC BANK GMBH, SUCURSAL EM PORTUGAL
        TEST USER DATE 01 Aug 2026 - 05 Aug 2026
        IBAN PT50000000000000000000000
        BIC TRBKPTP2XXX
        Checking Account €1,000.00 €0.00 €57.30 €942.70
        ACCOUNT TRANSACTIONS
        02 Aug Card
        MINI MERCADO €7.30 €992.70
        2026 Transaction
        03 Aug Savings plan execution IE00BK5BQT80 Vanguard Funds PLC - Vanguard FTSE All-
        Trade €50.00€942.70
        2026 World UCITS ETF (USD) Accumulating, quantity: 0.303000
        """;

    [Fact]
    public async Task Import_TradeRepublic_ExcludesSavingsPlanRowsButNotSpending()
    {
        await using var db = new AppDbContext(DbOptions());
        var service = MakeService(db, [TradeRepublicPage()]);

        var result = await service.ImportAsync(MakeFormFile("trade-republic-file"));

        Assert.True(result.Imported);
        Assert.Equal("TRADE REPUBLIC", result.Bank);

        await using var freshDb = new AppDbContext(DbOptions());
        var stmt = await freshDb.MonthlyStatements.Include(s => s.Transactions).SingleAsync();

        var card = stmt.Transactions.Single(t => t.Description.Contains("MINI MERCADO"));
        var savings = stmt.Transactions.Single(t => t.Description.Contains("Savings plan execution"));

        Assert.False(card.IsExcluded);
        Assert.True(savings.IsExcluded);
        Assert.Equal("debit", savings.Type);

        var asset = await freshDb.InvestmentAssets.Include(a => a.Lots).SingleAsync();
        Assert.Equal("IE00BK5BQT80", asset.Isin);
        Assert.Equal("ETF", asset.AssetType);
        var lot = Assert.Single(asset.Lots);
        Assert.Equal(0.303000m, lot.Quantity);
        Assert.Equal(new DateOnly(2026, 8, 3), lot.Date);
    }

    [Fact]
    public async Task Import_NonEurStatement_Throws_AndPersistsNothing()
    {
        await using var db = new AppDbContext(DbOptions());
        var service = MakeService(db, [ActivoBankPage(currency: "USD")]);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => service.ImportAsync(MakeFormFile("usd-file")));

        Assert.Contains("EUR", ex.Message);
        await using var freshDb = new AppDbContext(DbOptions());
        Assert.Equal(0, await freshDb.MonthlyStatements.CountAsync());
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }
}
