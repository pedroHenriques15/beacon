using System.Text;
using Beacon.Api.Data;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Features.Upload.Commands.UnifiedUploadBatch;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Beacon.Tests.Parsing;
using Beacon.Tests.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Handlers;

public class UnifiedUploadBatchTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

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
        _database.Dispose();
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
            [new ActivoBankParser(), new BpiParser(), new RevolutParser(), new TradeRepublicCsvParser()]);
        var groceryFactory = new GroceryReceiptParserFactory([new ContinenteParser()]);
        var salaryFactory = new SalarySlipParserFactory([new CentralGestParser(), new DomirestParser()]);
        var tradeImport = new TradeImportService(db, TestPricing.Queue());
        var statementService = new StatementUploadService(
            db, extractor, bankFactory, _fileStorage, tradeImport,
            NullLogger<StatementUploadService>.Instance);
        var groceryService = new GroceryReceiptUploadService(
            db, extractor, groceryFactory, _fileStorage, NullLogger<GroceryReceiptUploadService>.Instance);

        return new UnifiedUploadBatchCommandHandler(
            extractor, bankFactory, groceryFactory, salaryFactory,
            new Micro1InvoiceParser(), new DeelWithdrawalParser(), new MercorStatementParser(), new XtbExportParser(),
            statementService, groceryService,
            new XtbUploadService(db, tradeImport, NullLogger<XtbUploadService>.Instance),
            _fileStorage, db, NullLogger<UnifiedUploadBatchCommandHandler>.Instance);
    }

    private static string InvoiceText(string total = "1,600.00", string basePay = "1525.00", string other = "75.00") => $"""
        INVOICE
        BILL TO Micro1 Inc.
        Invoice for work between July 1, 2026 to July 15, 2026
        Other: Project → Example Group | Hours → 30.50 | Pay Rate → $50 | Base Pay → ${basePay} | Other → ${other} USD ${total}
        Total USD ${total}
        """;

    private static string WithdrawalText(string source = "1,600.00", string total = "1,391.95") => $"""
        Confirmation Statement
        Deel transaction ID 99999999
        Source amount ${source}
        Exchange fees -$12.00
        Exchange rate 1.00 USD = 0.87654321 EUR
        Total sent €{total}
        """;

    private AppDbContext CreateDb() => _database.CreateContext();

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
        await using var db = CreateDb();
        var extractor = new StubExtractor([ActivoBankPage]);
        var handler = MakeHandler(db, extractor);

        var results = await handler.HandleAsync([MakeFile("jan.pdf", "activo-bytes")]);

        var item = Assert.Single(results);
        Assert.Equal("BankStatement", item.DocumentType);
        Assert.True(item.Success);
        Assert.Equal(1, extractor.Calls);

        await using var freshDb = CreateDb();
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
    }

    [Fact]
    public async Task Handle_UnrecognisedFile_ReturnsUnknownWithoutPersisting()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db, new StubExtractor(["no known signals here"]));

        var results = await handler.HandleAsync([MakeFile("junk.pdf", "junk-bytes")]);

        var item = Assert.Single(results);
        Assert.Equal("Unknown", item.DocumentType);
        Assert.False(item.Success);
        Assert.Contains("not recognised", item.Error);

        await using var freshDb = CreateDb();
        Assert.Equal(0, await freshDb.MonthlyStatements.CountAsync());
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Handle_MixedBatch_FailedFileDoesNotAbortOthers()
    {
        await using var db = CreateDb();

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
    public async Task Handle_TradeRepublicCsv_ImportsWithoutTheExtractor_AndKeepsTheCsv()
    {
        await using var db = CreateDb();
        var extractor = new StubExtractor(["never used"]);
        var handler = MakeHandler(db, extractor);
        var csv = TradeRepublicCsv.File(
            TradeRepublicCsv.Cash("2026-08-02", "CARD_TRANSACTION", "-7.30", "MINI MERCADO"));

        var results = await handler.HandleAsync([MakeFile("Extrato de transações.csv", "\uFEFF" + csv)]);

        var item = Assert.Single(results);
        Assert.Equal("BankStatement", item.DocumentType);
        Assert.True(item.Success);
        Assert.Equal(0, extractor.Calls);

        await using var freshDb = CreateDb();
        var stmt = await freshDb.MonthlyStatements.SingleAsync();
        Assert.Equal("TRADE REPUBLIC", stmt.Bank);
        Assert.Equal(".csv", Path.GetExtension(stmt.PdfPath));
    }

    [Fact]
    public async Task Handle_ARefusedCsv_ReportsWhy_WithoutCallingItADuplicate()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db, new StubExtractor(["never used"]));
        var csv = TradeRepublicCsv.File(
            TradeRepublicCsv.Cash("2026-08-12", "CARD_TRANSACTION", "-3.00", "SHOP", currency: "USD"));

        var results = await handler.HandleAsync([MakeFile("export.csv", csv)]);

        var item = Assert.Single(results);
        Assert.Equal("BankStatement", item.DocumentType);
        Assert.False(item.Success);
        Assert.False(item.WasDuplicate);
        Assert.Contains("only EUR", item.Error);
    }

    [Fact]
    public async Task Handle_ACsvThatIsNotUtf8_IsReportedUnread()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db, new StubExtractor(["never used"]));

        var results = await handler.HandleAsync([("export.csv", new MemoryStream([0x22, 0xE7, 0xE3, 0x22]))]);

        var item = Assert.Single(results);
        Assert.Equal("Unknown", item.DocumentType);
        Assert.Contains("not UTF-8", item.Error);
    }

    [Fact]
    public async Task Handle_ATradeRepublicPdf_IsNotRecognised_AndPointsToTheCsv()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db, new StubExtractor(
            ["TRADE REPUBLIC BANK GMBH, SUCURSAL EM PORTUGAL\nBIC TRBKPTP2XXX"]));

        var results = await handler.HandleAsync([MakeFile("statement.pdf", "tr-pdf-bytes")]);

        var item = Assert.Single(results);
        Assert.Equal("Unknown", item.DocumentType);
        Assert.Contains("Trade Republic transaction exports (CSV)", item.Error);
    }

    [Fact]
    public async Task Handle_Micro1InvoicePlusWithdrawal_PairsIntoOneEurSalarySlip()
    {
        await using var db = CreateDb();
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
        Assert.Equal(1402.47m, parsed.GrossAmount);
        Assert.Equal(1391.95m, parsed.NetAmount);
        Assert.Equal(1336.73m, parsed.LineItems.First(li => li.Description == "Base Pay").Amount);
        Assert.Equal(65.74m, parsed.LineItems.First(li => li.Description == "Other").Amount);
        Assert.Equal(10.52m, parsed.LineItems.First(li => li.Description == "Deel exchange fee").Amount);
        Assert.Null(parsed.Warnings);

        // The client sends this back when it saves the slip: a file name, not a server path.
        var pdfPath = item.SalaryResult.PdfPath;
        Assert.Equal(Path.GetFileName(pdfPath), pdfPath);
        Assert.True(File.Exists(_fileStorage.GetFullPath(pdfPath)));
    }

    [Fact]
    public async Task Handle_LoneInvoice_IsBlockedNotImported()
    {
        await using var db = CreateDb();
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
        await using var db = CreateDb();
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
        await using var db = CreateDb();
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
        await using var db = CreateDb();
        var handler = MakeHandler(db, new PerFileExtractor());

        var text = ActivoBankPage + "\nNote: incoming payment from Micro1 Inc. Total USD $1,600.00";
        var results = await handler.HandleAsync([MakeFile("jan.pdf", text)]);

        var item = Assert.Single(results);
        Assert.Equal("BankStatement", item.DocumentType);
        Assert.True(item.Success);

        await using var freshDb = CreateDb();
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
    }

    [Fact]
    public async Task Handle_MixedBatch_BankStatementPlusMicro1Pair_ImportsEachCorrectly()
    {
        await using var db = CreateDb();
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

        await using var freshDb = CreateDb();
        Assert.Equal(1, await freshDb.MonthlyStatements.CountAsync());
    }

    private const string ActivoBankAugustWithMercor = """
        DEPOSITO A ORDEM: 123456789
        EXTRATO DE 2026/08/01 A 2026/08/31
        MOEDA BASE: EURO
        SALDO INICIAL 1 000.00
        08.14 08.14 TRF MERCOR.IO CORPORATION PAYOUTS 88.20 1 088.20
        SALDO FINAL 1 088.20
        ACTVPTPL
        """;

    private static Transaction Credit(string date, decimal amount, string description, string type = "credit") => new()
    {
        DatePosting = DateOnly.Parse(date),
        DateValue = DateOnly.Parse(date),
        Description = description,
        Amount = amount,
        Type = type,
    };

    private async Task SeedStatementAsync(string bank, params Transaction[] transactions)
    {
        await using var db = CreateDb();
        db.MonthlyStatements.Add(new MonthlyStatement
        {
            Bank = bank,
            Account = "",
            PeriodFrom = new DateOnly(2026, 7, 1),
            PeriodTo = new DateOnly(2026, 9, 30),
            SourceFile = $"{bank}.pdf",
            Transactions = transactions,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_MercorStatement_AsksForTheEur_AndSavesNoSlip()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db, new PerFileExtractor());

        var results = await handler.HandleAsync([MakeFile("mercor.pdf", MercorStatementText.Page())]);

        var item = Assert.Single(results);
        Assert.Equal("MercorNeedsEur", item.DocumentType);
        Assert.Null(item.Error);
        Assert.Null(item.SalaryResult);

        var mercor = item.MercorResult!;
        Assert.Equal(new DateOnly(2026, 8, 1), mercor.Period);
        Assert.Equal(145.17m, mercor.TotalPayUsd);
        Assert.Equal(3.63m, mercor.HoursWorked);
        Assert.Equal(40.00m, mercor.PayRateUsd);
        Assert.Null(mercor.SuggestedEur);
        Assert.Empty(mercor.Payouts);

        // Stored for the conversion endpoint, under a file name the client sends back.
        Assert.Equal(Path.GetFileName(mercor.PdfPath), mercor.PdfPath);
        Assert.True(File.Exists(_fileStorage.GetFullPath(mercor.PdfPath)));

        await using var freshDb = CreateDb();
        Assert.Equal(0, await freshDb.SalarySlips.CountAsync());
    }

    [Fact]
    public async Task Handle_MercorStatement_SuggestsOnlyThatMonthsMercorCredits_FromAnyBank()
    {
        await SeedStatementAsync("REVOLUT",
            Credit("2026-08-25", 40.00m, "Carregamento de Mercor.io Corporation"),
            Credit("2026-08-10", 50.00m, "Carregamento de MERCOR.IO CORPORATION"),
            Credit("2026-07-31", 20.00m, "Carregamento de MERCOR.IO CORPORATION"),
            Credit("2026-09-01", 30.00m, "Carregamento de MERCOR.IO CORPORATION"),
            Credit("2026-08-12", 5.00m, "MERCOR.IO CORPORATION", type: "debit"),
            Credit("2026-08-15", 300.00m, "Transfer from savings"));
        await SeedStatementAsync("BPI", Credit("2026-08-31", 10.00m, "TRF MERCORIO CORPORATION PAYOUTS"));

        await using var db = CreateDb();
        var handler = MakeHandler(db, new PerFileExtractor());

        var results = await handler.HandleAsync([MakeFile("mercor.pdf", MercorStatementText.Page())]);

        var mercor = Assert.Single(results).MercorResult!;
        Assert.Equal(100.00m, mercor.SuggestedEur);
        Assert.Equal(
            new[]
            {
                new MercorPayout(new DateOnly(2026, 8, 10), "REVOLUT", 50.00m),
                new MercorPayout(new DateOnly(2026, 8, 25), "REVOLUT", 40.00m),
                new MercorPayout(new DateOnly(2026, 8, 31), "BPI", 10.00m),
            },
            mercor.Payouts);
    }

    [Fact]
    public async Task Handle_MercorStatement_CountsABankStatementLaterInTheSameUpload()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db, new PerFileExtractor());

        var results = await handler.HandleAsync(
        [
            MakeFile("mercor.pdf", MercorStatementText.Page()),
            MakeFile("activo-august.pdf", ActivoBankAugustWithMercor),
        ]);

        Assert.Equal(2, results.Count);
        Assert.Equal("MercorNeedsEur", results[0].DocumentType);
        Assert.Equal("BankStatement", results[1].DocumentType);
        Assert.True(results[1].Success, results[1].Error);
        Assert.Equal(88.20m, results[0].MercorResult!.SuggestedEur);
    }

    [Fact]
    public async Task Handle_MercorStatementThatDoesNotAddUp_IsReportedWithTheReason_AndNotStored()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db, new PerFileExtractor());

        var results = await handler.HandleAsync(
            [MakeFile("mercor.pdf", MercorStatementText.Page(shiftPay: "150.00", totalPay: "150.00"))]);

        var item = Assert.Single(results);
        Assert.Equal("SalarySlip", item.DocumentType);
        Assert.False(item.Success);
        Assert.Contains("Mercor statement", item.Error);
        Assert.Contains("$150.00", item.Error);
        Assert.Null(item.MercorResult);
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }

    // XTB exports (ADR-034): a May and a June file, built like the real ones with invented numbers.
    private static readonly DateTime May27 = new(2026, 5, 27, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime June4 = new(2026, 6, 4, 7, 0, 0, DateTimeKind.Utc);

    private static (string, MemoryStream) XtbMay(params object?[][] positions) =>
        ("EUR_10000001_2026-04-30_2026-05-31.xlsx", XtbWorkbook.Export(
            [XtbWorkbook.Purchase(May27, "VWCE.DE", -200.00m, "OPEN BUY 2 @ 100.00", "5550001")],
            positions,
            from: new DateTime(2026, 4, 30, 23, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 5, 31, 22, 59, 59, DateTimeKind.Utc)));

    private static (string, MemoryStream) XtbJune(params object?[][] positions) =>
        (XtbWorkbook.FileName, XtbWorkbook.Export(
            [
                XtbWorkbook.Purchase(June4.AddHours(1), "SXR8.DE", -300.00m, "OPEN BUY 0.5 @ 600.00", "5550003"),
                XtbWorkbook.Sell(June4, "VWCE.DE", 220.00m, "CLOSE BUY 2 @ 110.00", "5550002"),
            ],
            positions));

    [Fact]
    public async Task Handle_XtbExportsOutOfOrder_AreAppliedOldestFirst()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db, new StubExtractor(["never used"]));

        // June sells what May bought: applied in the order given, the sell would be refused.
        var results = await handler.HandleAsync([XtbJune(XtbWorkbook.Position("SXR8.DE", 0.5m)), XtbMay()]);

        Assert.All(results, r => Assert.Equal("BrokerExport", r.DocumentType));
        Assert.All(results, r => Assert.True(r.Success, r.Error));
        Assert.Equal([XtbWorkbook.FileName, "EUR_10000001_2026-04-30_2026-05-31.xlsx"], results.Select(r => r.FileName));
        Assert.Equal((new DateOnly(2026, 6, 1), 2, 2), (results[0].TradesResult!.PeriodFrom,
            results[0].TradesResult!.TradeCount, results[0].TradesResult!.Added));
        Assert.All(results, r => Assert.Empty(r.TradesResult!.Warnings));

        await using var fresh = CreateDb();
        var held = await fresh.InvestmentLots.Include(l => l.Asset).ToListAsync();
        Assert.Equal(3, held.Count);
        Assert.Equal(0m, held.Where(l => l.Asset.Ticker == "VWCE.DE").Sum(l => l.Quantity));
        Assert.Equal(0.5m, held.Where(l => l.Asset.Ticker == "SXR8.DE").Sum(l => l.Quantity));
        Assert.Empty(Directory.GetFiles(_tempStorageRoot));
    }

    [Fact]
    public async Task Handle_AnXtbMonthAgain_AddsNothing()
    {
        await using var db = CreateDb();

        await MakeHandler(db, new StubExtractor(["never used"])).HandleAsync([XtbMay()]);
        var again = await MakeHandler(db, new StubExtractor(["never used"])).HandleAsync([XtbMay()]);

        var item = Assert.Single(again);
        Assert.Equal("BrokerExport", item.DocumentType);
        Assert.False(item.Success);
        Assert.True(item.WasDuplicate);
        Assert.Equal(0, item.TradesResult!.Added);
        Assert.Equal(1, await db.InvestmentLots.CountAsync());
    }

    [Fact]
    public async Task Handle_XtbTradesTypedByHand_AreNotDoubled()
    {
        await using var db = CreateDb();
        db.InvestmentAssets.Add(new InvestmentAsset
        {
            AssetType = "ETF",
            Ticker = "SXR8.DE",
            Name = "S&P 500, typed",
            Lots = [new InvestmentLot { Date = new DateOnly(2026, 6, 4), Quantity = 0.5m, PricePerUnit = 600.00m }],
        });
        db.InvestmentAssets.Add(new InvestmentAsset
        {
            AssetType = "ETF",
            Ticker = "VWCE.DE",
            Name = "All-World, typed",
            Lots = [new InvestmentLot { Date = new DateOnly(2026, 5, 27), Quantity = 2m, PricePerUnit = 100.00m }],
        });
        await db.SaveChangesAsync();

        var results = await MakeHandler(db, new StubExtractor(["never used"]))
            .HandleAsync([XtbMay(), XtbJune(XtbWorkbook.Position("SXR8.DE", 0.5m))]);

        // Only June's sell is new; the two buys were already there.
        Assert.Equal([0, 1], results.Select(r => r.TradesResult!.Added));
        Assert.All(results, r => Assert.Empty(r.TradesResult!.Warnings));
        await using var fresh = CreateDb();
        var lots = await fresh.InvestmentLots.OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(["XTB:5550003", "XTB:5550001", "XTB:5550002"], lots.Select(l => l.ExternalId));
    }

    [Fact]
    public async Task Handle_TheXtbHoldingsCheck_WarnsOnTheNewestExportOnly()
    {
        await using var db = CreateDb();
        var handler = MakeHandler(db, new StubExtractor(["never used"]));

        // Made after a later buy that this upload does not hold, XTB lists more than Beacon has.
        var position = XtbWorkbook.Position("SXR8.DE", 1.25m);
        var results = await handler.HandleAsync([XtbMay(position), XtbJune(position)]);

        Assert.Empty(results[0].TradesResult!.Warnings);
        var warning = Assert.Single(results[1].TradesResult!.Warnings);
        Assert.Contains("SXR8.DE: XTB lists 1.25 held on 6 Oct 2026, Beacon has 0.5.", warning);
        Assert.True(results[1].Success);
    }

    [Fact]
    public async Task Handle_AnXtbSellOfMoreThanHeld_RefusesThatFile_SavingNoneOfIt()
    {
        await using var db = CreateDb();

        var results = await MakeHandler(db, new StubExtractor(["never used"])).HandleAsync([XtbJune()]);

        var item = Assert.Single(results);
        Assert.Equal("BrokerExport", item.DocumentType);
        Assert.False(item.Success);
        Assert.False(item.WasDuplicate);
        Assert.Contains("Cannot sell 2 VWCE.DE on 2026-06-04", item.Error);
        Assert.Equal(0, await db.InvestmentLots.CountAsync());
        Assert.Equal(0, await db.InvestmentAssets.CountAsync());
    }

    [Fact]
    public async Task Handle_ARefusedXtbExport_ReportsWhy()
    {
        await using var db = CreateDb();
        var file = XtbWorkbook.Export([XtbWorkbook.Operation("Dividend", June4, 1.20m, "5550009")]);

        var results = await MakeHandler(db, new StubExtractor(["never used"]))
            .HandleAsync([(XtbWorkbook.FileName, file)]);

        var item = Assert.Single(results);
        Assert.Equal("BrokerExport", item.DocumentType);
        Assert.False(item.Success);
        Assert.Contains("\"Dividend\"", item.Error);
    }

    [Fact]
    public async Task Handle_AnXlsxThatIsNotAnXtbExport_IsNotRecognised()
    {
        await using var db = CreateDb();
        var file = XtbWorkbook.Write(("Sheet1", [["Date", "Amount"], ["2026-06-04", 10m]]));

        var results = await MakeHandler(db, new StubExtractor(["never used"])).HandleAsync([("budget.xlsx", file)]);

        var item = Assert.Single(results);
        Assert.Equal("Unknown", item.DocumentType);
        Assert.Contains("XTB account exports (XLSX)", item.Error);
    }
}
