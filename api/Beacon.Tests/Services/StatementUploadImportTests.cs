using System.Text;
using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Features.Shared;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Beacon.Tests.Parsing;
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
            [new ActivoBankParser(), new BpiParser(), new RevolutParser(), new TradeRepublicCsvParser()]);
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

    private sealed class NoExtractor : IPdfExtractor
    {
        public Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken ct = default)
            => throw new InvalidOperationException("A CSV never goes through pdfplumber.");
    }

    private DbContextOptions<AppDbContext> DbOptions() => _database.Options;

    private StatementUploadService MakeService(AppDbContext db, IReadOnlyList<string> pages) =>
        MakeService(db, new StubExtractor(pages));

    private StatementUploadService MakeService(AppDbContext db, IPdfExtractor extractor) =>
        new(db, extractor, _parserFactory, _fileStorage,
            new TradeImportService(db, TestPricing.Queue()),
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

    // ── Trade Republic CSV export (ADR-031) ─────────────────────────────────────

    private static readonly string July = TradeRepublicCsv.File(
        TradeRepublicCsv.Cash("2026-07-14", "TRANSFER_INSTANT_INBOUND", "200.00", "Incoming transfer from EXAMPLE PERSON"),
        TradeRepublicCsv.Cash("2026-07-14", "CARD_ORDERING_FEE", "0.000000", "Trade Republic Card", time: "11:00:00", fee: "-5.00"),
        TradeRepublicCsv.Cash("2026-07-21", "CARD_TRANSACTION", "-6.400000", "EXAMPLE TRAINS"));

    private static readonly string August = TradeRepublicCsv.File(
        TradeRepublicCsv.Cash("2026-08-01", "BENEFITS_SAVEBACK", "1.500000", "Saveback cash reward r1"),
        TradeRepublicCsv.Buy("2026-08-03", "-1.50", "0.009000", "166.6600000000", "trade-1"),
        TradeRepublicCsv.Buy("2026-08-07", "-99.95", "0.600000", "166.5800000000", "trade-2",
            fee: "-1.00", tax: "-0.02", description: $"Buy trade {TradeRepublicCsv.Isin} Example World Fund, quantity: 0.600000"),
        TradeRepublicCsv.Cash("2026-08-12", "CARD_TRANSACTION", "-20.00", "MINI MERCADO"));

    private static readonly string September = TradeRepublicCsv.File(
        TradeRepublicCsv.Cash("2026-09-02", "CARD_TRANSACTION", "-3.00", "MINI MERCADO"));

    private static FormFile Csv(string content)
    {
        var file = MakeFormFile(content, "Extrato de transações.csv");
        file.ContentType = "text/csv";
        return file;
    }

    private Task<UploadResult> ImportCsvAsync(AppDbContext db, string content) =>
        MakeService(db, new NoExtractor()).ImportAsync(Csv(content));

    [Fact]
    public async Task Import_TradeRepublicCsv_FirstMonth_OpensAtZeroAndSaysSo()
    {
        await using var db = new AppDbContext(DbOptions());

        var result = await ImportCsvAsync(db, July);

        Assert.True(result.Imported);
        Assert.Equal("TRADE REPUBLIC", result.Bank);
        Assert.Equal(3, result.TransactionCount);
        Assert.Contains(result.Warnings!, w => w.Contains("opening balance was taken as 0.00"));

        await using var freshDb = new AppDbContext(DbOptions());
        var stmt = await freshDb.MonthlyStatements.Include(s => s.Transactions).SingleAsync();
        Assert.Equal(new DateOnly(2026, 7, 1), stmt.PeriodFrom);
        Assert.Equal(new DateOnly(2026, 7, 31), stmt.PeriodTo);
        Assert.Equal(string.Empty, stmt.Account);
        Assert.Equal(0m, stmt.OpeningBalance);
        Assert.Equal(188.60m, stmt.ClosingBalance);
        Assert.Equal([200.00m, 195.00m, 188.60m],
            stmt.Transactions.OrderBy(t => t.Id).Select(t => t.Balance));
        Assert.Equal(".csv", Path.GetExtension(stmt.PdfPath));
        Assert.Equal(July, File.ReadAllText(_fileStorage.GetFullPath(stmt.PdfPath!)));
    }

    [Fact]
    public async Task Import_TradeRepublicCsv_NextMonth_OpensAtThePreviousClosing()
    {
        await using var db = new AppDbContext(DbOptions());
        await ImportCsvAsync(db, July);

        var result = await ImportCsvAsync(db, August);

        Assert.True(result.Imported);
        Assert.Null(result.Warnings);
        await using var freshDb = new AppDbContext(DbOptions());
        var august = await freshDb.MonthlyStatements.Include(s => s.Transactions)
            .SingleAsync(s => s.PeriodFrom == new DateOnly(2026, 8, 1));
        Assert.Equal(188.60m, august.OpeningBalance);
        Assert.Equal(188.60m + 1.50m - 1.50m - 100.97m - 20.00m, august.ClosingBalance);
        Assert.Equal(august.ClosingBalance, august.Transactions.OrderBy(t => t.Id).Last().Balance);
    }

    [Fact]
    public async Task Import_TradeRepublicCsv_AMonthMissingBefore_IsRefusedNamingIt()
    {
        await using var db = new AppDbContext(DbOptions());
        await ImportCsvAsync(db, July);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => ImportCsvAsync(db, September));

        Assert.Contains("August 2026", ex.Message);
        await using var freshDb = new AppDbContext(DbOptions());
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
        Assert.Single(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Import_TradeRepublicCsv_AnEarlierMonthAfterALaterOne_WarnsTheLaterWasNotRecomputed()
    {
        await using var db = new AppDbContext(DbOptions());
        await ImportCsvAsync(db, August);

        var result = await ImportCsvAsync(db, July);

        Assert.True(result.Imported);
        Assert.Contains(result.Warnings!, w => w.Contains("later TRADE REPUBLIC statement"));
    }

    [Fact]
    public async Task Import_TradeRepublicCsv_BuysAreExcludedWithNoCategory_AndBecomeLots()
    {
        await using var db = new AppDbContext(DbOptions());
        var shopping = new Category { Name = "Shopping", Color = "#000000" };
        db.Categories.Add(shopping);
        await db.SaveChangesAsync();
        // A space matches every row of the month, the buys included: a rule never categorises a buy.
        db.CategoryRules.Add(new CategoryRule { CategoryId = shopping.Id, Pattern = " " });
        await db.SaveChangesAsync();

        var result = await ImportCsvAsync(db, August);

        Assert.Equal(0, result.UnknownCount);
        await using var freshDb = new AppDbContext(DbOptions());
        var txs = await freshDb.Transactions.ToListAsync();
        var buys = txs.Where(t => t.Description.Contains(TradeRepublicCsv.Isin)).ToList();
        Assert.Equal(2, buys.Count);
        Assert.All(buys, b =>
        {
            Assert.True(b.IsExcluded);
            Assert.Null(b.CategoryId);
            Assert.Null(b.CategoryRuleId);
            Assert.Equal("debit", b.Type);
        });
        Assert.Equal([1.50m, 100.97m], buys.Select(b => b.Amount).Order());
        Assert.All(txs.Except(buys), t =>
        {
            Assert.False(t.IsExcluded);
            Assert.Equal(shopping.Id, t.CategoryId);
        });

        var asset = await freshDb.InvestmentAssets.Include(a => a.Lots).SingleAsync();
        Assert.Equal(TradeRepublicCsv.Isin, asset.Isin);
        Assert.Collection(asset.Lots.OrderBy(l => l.Date),
            l => Assert.Equal((0.009m, 0m, "trade-1"), (l.Quantity, l.Fees!.Value, l.ExternalId)),
            l => Assert.Equal((0.6m, 1.02m, "trade-2"), (l.Quantity, l.Fees!.Value, l.ExternalId)));
    }

    [Fact]
    public async Task Import_TradeRepublicCsv_TheSameMonthAgain_IsRefused()
    {
        await using var db = new AppDbContext(DbOptions());
        await ImportCsvAsync(db, July);

        // A fresh export of the month: different bytes, same month.
        var again = July + TradeRepublicCsv.Cash("2026-07-30", "CARD_TRANSACTION", "-1.00", "LATE SHOP") + "\n";
        var result = await ImportCsvAsync(db, again);

        Assert.False(result.Imported);
        Assert.Contains("already exists", result.Message);
        await using var freshDb = new AppDbContext(DbOptions());
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
    }

    [Fact]
    public async Task Import_TradeRepublicCsv_AMonthAStatementAlreadyPartlyCovers_IsRefused()
    {
        await using var db = new AppDbContext(DbOptions());
        // A statement imported from the PDF, which started on the account's first day.
        db.MonthlyStatements.Add(new MonthlyStatement
        {
            Bank = "TRADE REPUBLIC",
            PeriodFrom = new DateOnly(2026, 7, 14),
            PeriodTo = new DateOnly(2026, 7, 31),
        });
        await db.SaveChangesAsync();

        var result = await ImportCsvAsync(db, July);

        Assert.False(result.Imported);
        Assert.Contains("already exists", result.Message);
    }

    [Fact]
    public async Task Import_TradeRepublicCsv_ReimportedAfterTheStatementIsDeleted_AddsNoLotTwice()
    {
        await using var db = new AppDbContext(DbOptions());
        await ImportCsvAsync(db, August);
        db.MonthlyStatements.Remove(await db.MonthlyStatements.SingleAsync());
        await db.SaveChangesAsync();

        var result = await ImportCsvAsync(db, August);

        Assert.True(result.Imported);
        await using var freshDb = new AppDbContext(DbOptions());
        Assert.Equal(2, await freshDb.InvestmentLots.CountAsync());
    }

    [Fact]
    public async Task Import_TradeRepublicCsv_ARefusedRow_IsNotSupported_AndPersistsNothing()
    {
        await using var db = new AppDbContext(DbOptions());
        var refund = TradeRepublicCsv.File(
            TradeRepublicCsv.Cash("2026-08-12", "CARD_REFUND", "3.00", "MINI MERCADO"));

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => ImportCsvAsync(db, refund));

        Assert.Contains("CARD_REFUND", ex.Message);
        await using var freshDb = new AppDbContext(DbOptions());
        Assert.Equal(0, await freshDb.MonthlyStatements.CountAsync());
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Import_TradeRepublicPdf_IsNotRecognised()
    {
        await using var db = new AppDbContext(DbOptions());
        var service = MakeService(db, ["TRADE REPUBLIC BANK GMBH, SUCURSAL EM PORTUGAL\nBIC TRBKPTP2XXX"]);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.ImportAsync(MakeFormFile("tr-pdf")));
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
