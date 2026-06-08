using Beacon.Api.Data;
using Beacon.Api.Features.Statements.Commands.DeleteStatement;
using Beacon.Api.Features.Statements.Commands.ImportMealCardText;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Handlers;

public class StatementHandlerTests : IDisposable
{
    private readonly string _tempStorageRoot;
    private readonly FileStorageService _fileStorage;

    public StatementHandlerTests()
    {
        _tempStorageRoot = Path.Combine(Path.GetTempPath(), $"fh_stmt_tests_{Guid.NewGuid()}");
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

    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private DeleteStatementCommandHandler MakeDeleteHandler(AppDbContext db) =>
        new(db, _fileStorage, NullLogger<DeleteStatementCommandHandler>.Instance);

    private static ImportMealCardTextCommandHandler MakeImportHandler(AppDbContext db) =>
        new(db, NullLogger<ImportMealCardTextCommandHandler>.Instance);

    private const string ValidMealCardText =
        "01/01/2026 LIDL STORE PT-12,50 €\n" +
        "03/01/2026 PINGO DOCE PT-8,30 €\n" +
        "05/01/2026 EMPLOYER LOAD PT 100,00 €-";

    [Fact]
    public async Task DeleteStatement_ReturnsFalse_WhenNotFound()
    {
        await using var db = CreateDb(nameof(DeleteStatement_ReturnsFalse_WhenNotFound));

        var result = await MakeDeleteHandler(db).HandleAsync(new DeleteStatementCommand(9999));

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteStatement_ReturnsTrue_AndRemovesStatement()
    {
        await using var db = CreateDb(nameof(DeleteStatement_ReturnsTrue_AndRemovesStatement));
        var stmt = new MonthlyStatement
        {
            Bank = "ACTIVOBANK", Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31)
        };
        db.MonthlyStatements.Add(stmt);
        await db.SaveChangesAsync();

        var result = await MakeDeleteHandler(db).HandleAsync(new DeleteStatementCommand(stmt.Id));

        Assert.True(result);
        Assert.False(await db.MonthlyStatements.AnyAsync(s => s.Id == stmt.Id));
    }

    [Fact]
    public async Task DeleteStatement_RemovesCascadedTransactions()
    {
        await using var db = CreateDb(nameof(DeleteStatement_RemovesCascadedTransactions));
        var stmt = new MonthlyStatement
        {
            Bank = "BPI", Account = "PT51",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions =
            [
                new Transaction { Description = "LIDL",   Amount = 20, Type = "debit",
                    DatePosting = new DateOnly(2026, 1, 5), DateValue = new DateOnly(2026, 1, 5), Balance = 980 },
                new Transaction { Description = "SALARY", Amount = 1000, Type = "credit",
                    DatePosting = new DateOnly(2026, 1, 10), DateValue = new DateOnly(2026, 1, 10), Balance = 1980 }
            ]
        };
        db.MonthlyStatements.Add(stmt);
        await db.SaveChangesAsync();

        await MakeDeleteHandler(db).HandleAsync(new DeleteStatementCommand(stmt.Id));

        Assert.Equal(0, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task DeleteStatement_EmptyCounterpartStatement_IsAlsoDeleted()
    {
        await using var db = CreateDb(nameof(DeleteStatement_EmptyCounterpartStatement_IsAlsoDeleted));

        var date = new DateOnly(2026, 1, 10);

        var stmtA = new MonthlyStatement
        {
            Bank = "ACTIVOBANK", Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions =
            [
                new Transaction { Description = "TRANSFER OUT", Amount = 500, Type = "debit",
                    DatePosting = date, DateValue = date, Balance = 500, IsExcluded = true }
            ]
        };

        var stmtB = new MonthlyStatement
        {
            Bank = "BPI", Account = "PT51",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions =
            [
                new Transaction { Description = "TRANSFER IN", Amount = 500, Type = "credit",
                    DatePosting = date, DateValue = date, Balance = 1500, IsExcluded = true }
            ]
        };

        db.MonthlyStatements.AddRange(stmtA, stmtB);
        await db.SaveChangesAsync();

        await MakeDeleteHandler(db).HandleAsync(new DeleteStatementCommand(stmtA.Id));

        Assert.False(await db.MonthlyStatements.AnyAsync(s => s.Id == stmtB.Id));
        Assert.Equal(0, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task DeleteStatement_CounterpartStatementWithOtherTxs_IsRetained()
    {
        await using var db = CreateDb(nameof(DeleteStatement_CounterpartStatementWithOtherTxs_IsRetained));

        var date = new DateOnly(2026, 1, 10);

        var stmtA = new MonthlyStatement
        {
            Bank = "ACTIVOBANK", Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions =
            [
                new Transaction { Description = "TRANSFER", Amount = 200, Type = "debit",
                    DatePosting = date, DateValue = date, Balance = 800, IsExcluded = true }
            ]
        };

        var stmtB = new MonthlyStatement
        {
            Bank = "BPI", Account = "PT51",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions =
            [
                new Transaction { Description = "TRANSFER", Amount = 200, Type = "credit",
                    DatePosting = date, DateValue = date, Balance = 1200, IsExcluded = true },
                new Transaction { Description = "LIDL", Amount = 15, Type = "debit",
                    DatePosting = new DateOnly(2026, 1, 12), DateValue = new DateOnly(2026, 1, 12), Balance = 1185 }
            ]
        };

        db.MonthlyStatements.AddRange(stmtA, stmtB);
        await db.SaveChangesAsync();

        await MakeDeleteHandler(db).HandleAsync(new DeleteStatementCommand(stmtA.Id));

        Assert.True(await db.MonthlyStatements.AnyAsync(s => s.Id == stmtB.Id));
        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task ImportMealCardText_InvalidText_ThrowsNotSupportedException()
    {
        await using var db = CreateDb(nameof(ImportMealCardText_InvalidText_ThrowsNotSupportedException));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            MakeImportHandler(db).HandleAsync(
                new ImportMealCardTextCommand("this is not meal card format"), CancellationToken.None));
    }

    [Fact]
    public async Task ImportMealCardText_ValidText_CreatesStatementAndTransactions()
    {
        await using var db = CreateDb(nameof(ImportMealCardText_ValidText_CreatesStatementAndTransactions));

        var result = await MakeImportHandler(db).HandleAsync(
            new ImportMealCardTextCommand(ValidMealCardText), CancellationToken.None);

        Assert.True(result.Imported);
        Assert.Equal("MEAL CARD", result.Bank);
        Assert.Equal(3, result.TransactionCount);

        var stmt = await db.MonthlyStatements.Include(s => s.Transactions).SingleAsync();
        Assert.Equal("MEAL CARD", stmt.Bank);
        Assert.Equal(3, stmt.Transactions.Count);
    }

    [Fact]
    public async Task ImportMealCardText_ParsesDebitAndCreditTypes()
    {
        await using var db = CreateDb(nameof(ImportMealCardText_ParsesDebitAndCreditTypes));

        await MakeImportHandler(db).HandleAsync(
            new ImportMealCardTextCommand(ValidMealCardText), CancellationToken.None);

        var txs = await db.Transactions.ToListAsync();
        Assert.Equal(2, txs.Count(t => t.Type == "debit"));
        Assert.Equal(1, txs.Count(t => t.Type == "credit"));
    }

    [Fact]
    public async Task ImportMealCardText_DuplicatePeriod_ReturnsNotImported()
    {
        await using var db = CreateDb(nameof(ImportMealCardText_DuplicatePeriod_ReturnsNotImported));

        await MakeImportHandler(db).HandleAsync(
            new ImportMealCardTextCommand(ValidMealCardText), CancellationToken.None);

        var result = await MakeImportHandler(db).HandleAsync(
            new ImportMealCardTextCommand(ValidMealCardText), CancellationToken.None);

        Assert.False(result.Imported);
        Assert.NotNull(result.Message);
        Assert.Equal(1, await db.MonthlyStatements.CountAsync());
    }

    [Fact]
    public async Task ImportMealCardText_AppliesCategoryRules()
    {
        await using var db = CreateDb(nameof(ImportMealCardText_AppliesCategoryRules));

        var cat  = new Category { Name = "Groceries", Color = "#00ff00" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();
        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "LIDL" };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        await MakeImportHandler(db).HandleAsync(
            new ImportMealCardTextCommand(ValidMealCardText), CancellationToken.None);

        var txs = await db.Transactions.ToListAsync();
        var lidl = txs.Single(t => t.Description.Contains("LIDL"));
        Assert.Equal(cat.Id,  lidl.CategoryId);
        Assert.Equal(rule.Id, lidl.CategoryRuleId);
        Assert.Null(txs.First(t => t.Description.Contains("PINGO")).CategoryId);
    }

    [Fact]
    public async Task ImportMealCardText_DetectsTransferCandidates()
    {
        await using var db = CreateDb(nameof(ImportMealCardText_DetectsTransferCandidates));

        var existing = new MonthlyStatement
        {
            Bank = "ACTIVOBANK", Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions =
            [
                new Transaction { Description = "CARD RELOAD", Amount = 100, Type = "debit",
                    DatePosting = new DateOnly(2026, 1, 5), DateValue = new DateOnly(2026, 1, 5), Balance = 900 }
            ]
        };
        db.MonthlyStatements.Add(existing);
        await db.SaveChangesAsync();

        const string creditText = "05/01/2026 CARD RELOAD PT 100,00 €-";
        var result = await MakeImportHandler(db).HandleAsync(
            new ImportMealCardTextCommand(creditText), CancellationToken.None);

        Assert.True(result.Imported);
        Assert.NotNull(result.TransferCandidates);
        Assert.Single(result.TransferCandidates);
        Assert.Equal(100m, result.TransferCandidates[0].Amount);
    }
}
