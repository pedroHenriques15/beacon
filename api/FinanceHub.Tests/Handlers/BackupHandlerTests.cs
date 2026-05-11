using System.Text.Json;
using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Backup.Commands.CreateBackup;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceHub.Tests.Handlers;

public class BackupHandlerTests : IDisposable
{
    private readonly string _tempBackupDir;

    public BackupHandlerTests()
    {
        _tempBackupDir = Path.Combine(Path.GetTempPath(), $"fh_backup_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempBackupDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBackupDir))
            Directory.Delete(_tempBackupDir, recursive: true);
    }

    private AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private CreateBackupCommandHandler MakeHandler(AppDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Backup:Path"] = _tempBackupDir })
            .Build();
        return new CreateBackupCommandHandler(db, config, NullLogger<CreateBackupCommandHandler>.Instance);
    }

    [Fact]
    public async Task CreateBackup_ReturnsSuccessResponse()
    {
        await using var db = CreateDb(nameof(CreateBackup_ReturnsSuccessResponse));

        var response = await MakeHandler(db).HandleAsync();

        Assert.Contains("successfully", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_tempBackupDir, response.Path);
    }

    [Fact]
    public async Task CreateBackup_WritesJsonFileToConfiguredDirectory()
    {
        await using var db = CreateDb(nameof(CreateBackup_WritesJsonFileToConfiguredDirectory));

        var response = await MakeHandler(db).HandleAsync();

        Assert.True(File.Exists(response.Path));
        var content = await File.ReadAllTextAsync(response.Path);
        Assert.False(string.IsNullOrWhiteSpace(content));

        using var doc = JsonDocument.Parse(content);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task CreateBackup_JsonContainsAllExpectedTopLevelKeys()
    {
        await using var db = CreateDb(nameof(CreateBackup_JsonContainsAllExpectedTopLevelKeys));

        var response = await MakeHandler(db).HandleAsync();
        var json = await File.ReadAllTextAsync(response.Path);
        using var doc = JsonDocument.Parse(json);

        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("categories",           keys);
        Assert.Contains("categoryRules",        keys);
        Assert.Contains("monthlyStatements",    keys);
        Assert.Contains("transactions",         keys);
        Assert.Contains("salaryProfiles",       keys);
        Assert.Contains("salaryItemCategories", keys);
        Assert.Contains("salarySlips",          keys);
        Assert.Contains("salaryLineItems",      keys);
    }

    [Fact]
    public async Task CreateBackup_IncludesSeedDataInJson()
    {
        await using var db = CreateDb(nameof(CreateBackup_IncludesSeedDataInJson));

        var cat = new Category { Name = "Groceries", Color = "#00ff00" };
        db.Categories.Add(cat);
        var stmt = new MonthlyStatement
        {
            Bank = "BPI", Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions =
            [
                new Transaction { Description = "LIDL", Amount = 25, Type = "debit",
                    DatePosting = new DateOnly(2026, 1, 5), DateValue = new DateOnly(2026, 1, 5), Balance = 975 }
            ]
        };
        db.MonthlyStatements.Add(stmt);
        await db.SaveChangesAsync();

        var response = await MakeHandler(db).HandleAsync();
        var json = await File.ReadAllTextAsync(response.Path);
        using var doc = JsonDocument.Parse(json);

        var categories = doc.RootElement.GetProperty("Categories");
        Assert.Equal(1, categories.GetArrayLength());
        Assert.Equal("Groceries", categories[0].GetProperty("Name").GetString());

        var statements = doc.RootElement.GetProperty("MonthlyStatements");
        Assert.Equal(1, statements.GetArrayLength());
        Assert.Equal("BPI", statements[0].GetProperty("Bank").GetString());

        var transactions = doc.RootElement.GetProperty("Transactions");
        Assert.Equal(1, transactions.GetArrayLength());
        Assert.Equal("LIDL", transactions[0].GetProperty("Description").GetString());
    }

    [Fact]
    public async Task CreateBackup_EmptyDb_WritesEmptyArrays()
    {
        await using var db = CreateDb(nameof(CreateBackup_EmptyDb_WritesEmptyArrays));

        var response = await MakeHandler(db).HandleAsync();
        var json = await File.ReadAllTextAsync(response.Path);
        using var doc = JsonDocument.Parse(json);

        foreach (var prop in doc.RootElement.EnumerateObject())
            Assert.Equal(0, prop.Value.GetArrayLength());
    }
}
