using System.Text;
using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beacon.Tests.Services;

public class GroceryReceiptUploadServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    private readonly string _tempStorageRoot;
    private readonly FileStorageService _fileStorage;

    public GroceryReceiptUploadServiceTests()
    {
        _tempStorageRoot = Path.Combine(Path.GetTempPath(), $"fh_grocery_upload_tests_{Guid.NewGuid()}");
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

    private sealed class StubExtractor : IPdfExtractor
    {
        public Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["STUB RECEIPT"]);
    }

    private sealed class StubParser(ParsedGroceryReceipt receipt) : IGroceryReceiptParser
    {
        public string ParserName => "Stub";
        public bool CanParse(string fullText) => true;
        public ParsedGroceryReceipt Parse(string fileName, IReadOnlyList<string> pages) => receipt;
    }

    private AppDbContext CreateDb() => _database.CreateContext();

    private GroceryReceiptUploadService MakeService(AppDbContext db, params ParsedGroceryItem[] items) =>
        new(db, new StubExtractor(),
            new GroceryReceiptParserFactory([new StubParser(new ParsedGroceryReceipt(
                "Continente", new DateOnly(2026, 3, 14), items.Sum(i => i.Amount), items))]),
            _fileStorage, NullLogger<GroceryReceiptUploadService>.Instance);

    private static FormFile MakeFormFile(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "receipt.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Import_RuleWithTextAndAmount_CategorisesOnlyItemsWithBoth(bool matchWholeDescription, bool longerNameMatches)
    {
        await using var db = CreateDb();
        var drinks = new GroceryCategory { Name = "Drinks", Color = "#0000ff" };
        db.GroceryCategories.Add(drinks);
        await db.SaveChangesAsync();
        var rule = new GroceryCategoryRule { CategoryId = drinks.Id, Pattern = "COCA COLA LATA", Value = 0.90m, MatchWholeDescription = matchWholeDescription };
        db.GroceryCategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = MakeService(db,
            new ParsedGroceryItem("COCA COLA LATA", 0.90m, 1),
            new ParsedGroceryItem("COCA COLA LATA", 1.20m, 1),
            new ParsedGroceryItem("AGUA LUSO", 0.90m, 1),
            new ParsedGroceryItem("COCA COLA LATA ZERO", 0.90m, 1));

        await service.ImportAsync(MakeFormFile("receipt-text-and-amount"));

        await using var freshDb = CreateDb();
        var items = await freshDb.GroceryItems.OrderBy(i => i.Id).ToListAsync();
        Assert.Equal(4, items.Count);
        Assert.Equal((drinks.Id, rule.Id), (items[0].CategoryId!.Value, items[0].CategoryRuleId!.Value));
        Assert.All(items.Skip(1).Take(2), i =>
        {
            Assert.Null(i.CategoryId);
            Assert.Null(i.CategoryRuleId);
        });
        Assert.Equal(longerNameMatches ? rule.Id : null, items[3].CategoryRuleId);
    }

    [Fact]
    public async Task Import_AmountOnlyRule_CategorisesOnlyItemsOfThatAmount()
    {
        await using var db = CreateDb();
        var bags = new GroceryCategory { Name = "Bags", Color = "#00ff00" };
        db.GroceryCategories.Add(bags);
        await db.SaveChangesAsync();
        db.GroceryCategoryRules.Add(new GroceryCategoryRule { CategoryId = bags.Id, Value = 0.12m });
        await db.SaveChangesAsync();

        var service = MakeService(db,
            new ParsedGroceryItem("SACO REUTILIZAVEL", 0.12m, 1),
            new ParsedGroceryItem("AGUA LUSO", 0.90m, 1));

        await service.ImportAsync(MakeFormFile("receipt-amount-only"));

        await using var freshDb = CreateDb();
        var items = await freshDb.GroceryItems.OrderBy(i => i.Id).ToListAsync();
        Assert.Equal(bags.Id, items[0].CategoryId);
        Assert.Null(items[1].CategoryId);
    }
}
