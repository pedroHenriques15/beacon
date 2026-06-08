using Beacon.Api.Data;
using Beacon.Api.Features.Categories.Commands.CreateCategory;
using Beacon.Api.Features.Categories.Commands.CreateCategoryRule;
using Beacon.Api.Features.Categories.Commands.UpdateCategoryRule;
using Beacon.Api.Features.Categories.Shared;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Handlers;

public class CategoryHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static CreateCategoryRuleCommandHandler CreateHandler(AppDbContext db)
    {
        var applyRule = new ApplyRuleService(db);
        var logger    = NullLogger<CreateCategoryRuleCommandHandler>.Instance;
        return new CreateCategoryRuleCommandHandler(db, applyRule, logger);
    }

    private static UpdateCategoryRuleCommandHandler CreateUpdateHandler(AppDbContext db)
    {
        var logger = NullLogger<UpdateCategoryRuleCommandHandler>.Instance;
        return new UpdateCategoryRuleCommandHandler(db, logger);
    }

    private static CreateCategoryCommandHandler CreateCategoryHandler(AppDbContext db)
    {
        var applyRule = new ApplyRuleService(db);
        var logger    = NullLogger<CreateCategoryCommandHandler>.Instance;
        return new CreateCategoryCommandHandler(db, applyRule, logger);
    }

    private static async Task<(Category cat, MonthlyStatement stmt)> SeedCategoryAndStatementAsync(
        AppDbContext db,
        string catName,
        IEnumerable<Transaction> transactions)
    {
        var cat = new Category { Name = catName, Color = "#aaaaaa" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var stmt = new MonthlyStatement
        {
            Bank       = "TESTBANK",
            Account    = "123",
            PeriodFrom = new DateOnly(2024, 1, 1),
            PeriodTo   = new DateOnly(2024, 1, 31),
            Transactions = transactions.ToList()
        };
        db.MonthlyStatements.Add(stmt);
        await db.SaveChangesAsync();

        return (cat, stmt);
    }

    [Fact]
    public async Task CreateCategoryRule_WithPatternOnly_PersistsAndApplies()
    {
        await using var db = CreateDb(nameof(CreateCategoryRule_WithPatternOnly_PersistsAndApplies));

        var (cat, _) = await SeedCategoryAndStatementAsync(db, "Groceries",
        [
            new Transaction { Description = "LIDL Lisboa", Amount = 30, Type = "debit", DatePosting = new DateOnly(2024,1,1), DateValue = new DateOnly(2024,1,1), Balance = 970 },
            new Transaction { Description = "SALARY",      Amount = 1000, Type = "credit", DatePosting = new DateOnly(2024,1,5), DateValue = new DateOnly(2024,1,5), Balance = 1970 }
        ]);

        var handler = CreateHandler(db);
        var result  = await handler.HandleAsync(new CreateCategoryRuleCommand(cat.Id, "LIDL", null));

        Assert.NotNull(result);
        Assert.Equal("LIDL", result.Pattern);
        Assert.Null(result.Value);

        var matched   = await db.Transactions.FirstAsync(t => t.Description == "LIDL Lisboa");
        var unmatched = await db.Transactions.FirstAsync(t => t.Description == "SALARY");

        Assert.Equal(cat.Id,    matched.CategoryId);
        Assert.Equal(result.Id, matched.CategoryRuleId);
        Assert.Null(unmatched.CategoryId);
    }

    [Fact]
    public async Task CreateCategoryRule_WithValueOnly_PersistsAndApplies()
    {
        await using var db = CreateDb(nameof(CreateCategoryRule_WithValueOnly_PersistsAndApplies));

        var (cat, _) = await SeedCategoryAndStatementAsync(db, "Salary",
        [
            new Transaction { Description = "EMPLOYER CREDIT", Amount = 1500, Type = "credit", DatePosting = new DateOnly(2024,1,5), DateValue = new DateOnly(2024,1,5), Balance = 1500 },
            new Transaction { Description = "OTHER CREDIT",    Amount = 200,  Type = "credit", DatePosting = new DateOnly(2024,1,6), DateValue = new DateOnly(2024,1,6), Balance = 1700 }
        ]);

        var handler = CreateHandler(db);
        var result  = await handler.HandleAsync(new CreateCategoryRuleCommand(cat.Id, null, 1500m));

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Pattern);
        Assert.Equal(1500m, result.Value);

        var savedRule = await db.CategoryRules.FirstAsync(r => r.Id == result.Id);
        Assert.Equal(1500m, savedRule.Value);
        Assert.Equal(cat.Id, savedRule.CategoryId);

        var matched   = await db.Transactions.FirstAsync(t => t.Description == "EMPLOYER CREDIT");
        var unmatched = await db.Transactions.FirstAsync(t => t.Description == "OTHER CREDIT");

        Assert.Equal(cat.Id,    matched.CategoryId);
        Assert.Equal(result.Id, matched.CategoryRuleId);
        Assert.False(matched.CategorySetManually);
        Assert.Null(unmatched.CategoryId);
    }

    [Fact]
    public async Task CreateCategoryRule_WithBothPatternAndValue_RequiresBothConditions()
    {
        await using var db = CreateDb(nameof(CreateCategoryRule_WithBothPatternAndValue_RequiresBothConditions));

        var (cat, _) = await SeedCategoryAndStatementAsync(db, "Groceries",
        [
            new Transaction { Description = "LIDL Lisboa", Amount = 30, Type = "debit", DatePosting = new DateOnly(2024,1,1), DateValue = new DateOnly(2024,1,1), Balance = 970 },
            new Transaction { Description = "LIDL Lisboa", Amount = 99, Type = "debit", DatePosting = new DateOnly(2024,1,2), DateValue = new DateOnly(2024,1,2), Balance = 871 },
            new Transaction { Description = "OTHER STORE", Amount = 30, Type = "debit", DatePosting = new DateOnly(2024,1,3), DateValue = new DateOnly(2024,1,3), Balance = 841 }
        ]);

        var handler = CreateHandler(db);
        var result  = await handler.HandleAsync(new CreateCategoryRuleCommand(cat.Id, "LIDL", 30m));

        Assert.NotNull(result);
        Assert.Equal("LIDL", result.Pattern);
        Assert.Equal(30m, result.Value);

        var txs     = await db.Transactions.ToListAsync();
        var lidl30  = txs.First(t => t.Description == "LIDL Lisboa" && t.Amount == 30);
        var lidl99  = txs.First(t => t.Description == "LIDL Lisboa" && t.Amount == 99);
        var other30 = txs.First(t => t.Description == "OTHER STORE");

        Assert.Equal(cat.Id,    lidl30.CategoryId);
        Assert.Equal(result.Id, lidl30.CategoryRuleId);
        Assert.Null(lidl99.CategoryId);
        Assert.Null(other30.CategoryId);
    }

    [Fact]
    public async Task CreateCategoryRule_WithNeitherPatternNorValue_ThrowsArgumentException()
    {
        await using var db = CreateDb(nameof(CreateCategoryRule_WithNeitherPatternNorValue_ThrowsArgumentException));

        var cat = new Category { Name = "Test", Color = "#aaaaaa" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(new CreateCategoryRuleCommand(cat.Id, null, null)));
    }

    [Fact]
    public async Task CreateCategoryRule_WithUnknownCategoryId_ReturnsNull()
    {
        await using var db = CreateDb(nameof(CreateCategoryRule_WithUnknownCategoryId_ReturnsNull));

        var handler = CreateHandler(db);
        var result  = await handler.HandleAsync(new CreateCategoryRuleCommand(9999, "LIDL", null));

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateCategoryRule_WithPatternOnly_UpdatesRuleSuccessfully()
    {
        await using var db = CreateDb(nameof(UpdateCategoryRule_WithPatternOnly_UpdatesRuleSuccessfully));

        var cat = new Category { Name = "Test", Color = "#aaaaaa" };
        db.Categories.Add(cat);
        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "OLD", Value = null };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var handler = CreateUpdateHandler(db);
        var result  = await handler.HandleAsync(new UpdateCategoryRuleCommand(rule.Id, "NEW", null));

        Assert.True(result);

        var updated = await db.CategoryRules.FindAsync(rule.Id);
        Assert.Equal("NEW", updated!.Pattern);
        Assert.Null(updated.Value);
    }

    [Fact]
    public async Task UpdateCategoryRule_WithValueOnly_UpdatesRuleSuccessfully()
    {
        await using var db = CreateDb(nameof(UpdateCategoryRule_WithValueOnly_UpdatesRuleSuccessfully));

        var cat = new Category { Name = "Test", Color = "#aaaaaa" };
        db.Categories.Add(cat);
        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "OLD", Value = null };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var handler = CreateUpdateHandler(db);
        var result  = await handler.HandleAsync(new UpdateCategoryRuleCommand(rule.Id, null, 500m));

        Assert.True(result);

        var updated = await db.CategoryRules.FindAsync(rule.Id);
        Assert.Equal(string.Empty, updated!.Pattern);
        Assert.Equal(500m, updated.Value);
    }

    [Fact]
    public async Task UpdateCategoryRule_WithBothPatternAndValue_UpdatesRuleSuccessfully()
    {
        await using var db = CreateDb(nameof(UpdateCategoryRule_WithBothPatternAndValue_UpdatesRuleSuccessfully));

        var cat = new Category { Name = "Test", Color = "#aaaaaa" };
        db.Categories.Add(cat);
        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "OLD", Value = 100m };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var handler = CreateUpdateHandler(db);
        var result  = await handler.HandleAsync(new UpdateCategoryRuleCommand(rule.Id, "NEW", 200m));

        Assert.True(result);

        var updated = await db.CategoryRules.FindAsync(rule.Id);
        Assert.Equal("NEW", updated!.Pattern);
        Assert.Equal(200m, updated.Value);
    }

    [Fact]
    public async Task UpdateCategoryRule_WithUnknownId_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(UpdateCategoryRule_WithUnknownId_ReturnsFalse));

        var handler = CreateUpdateHandler(db);
        var result  = await handler.HandleAsync(new UpdateCategoryRuleCommand(9999, "X", null));

        Assert.False(result);
    }

    [Fact]
    public async Task UpdateCategoryRule_WithNeitherPatternNorValue_ThrowsArgumentException()
    {
        await using var db = CreateDb(nameof(UpdateCategoryRule_WithNeitherPatternNorValue_ThrowsArgumentException));

        var cat = new Category { Name = "Test", Color = "#aaaaaa" };
        db.Categories.Add(cat);
        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "OLD" };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var handler = CreateUpdateHandler(db);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(new UpdateCategoryRuleCommand(rule.Id, null, null)));
    }

    [Fact]
    public async Task CreateCategory_WithPatternAndValue_CreatesRuleWithValue()
    {
        await using var db = CreateDb(nameof(CreateCategory_WithPatternAndValue_CreatesRuleWithValue));

        var handler  = CreateCategoryHandler(db);
        var response = await handler.HandleAsync(new CreateCategoryCommand("Test", "#aaa", "MB WAY", 50m));

        var rules = await db.CategoryRules.Where(r => r.CategoryId == response.Id).ToListAsync();
        Assert.Single(rules);
        Assert.Equal("MB WAY", rules[0].Pattern);
        Assert.Equal(50m, rules[0].Value);
    }

    [Fact]
    public async Task CreateCategory_WithPatternOnly_CreatesRuleWithNullValue()
    {
        await using var db = CreateDb(nameof(CreateCategory_WithPatternOnly_CreatesRuleWithNullValue));

        var handler  = CreateCategoryHandler(db);
        var response = await handler.HandleAsync(new CreateCategoryCommand("Test", "#aaa", "MB WAY", null));

        var rules = await db.CategoryRules.Where(r => r.CategoryId == response.Id).ToListAsync();
        Assert.Single(rules);
        Assert.Equal("MB WAY", rules[0].Pattern);
        Assert.Null(rules[0].Value);
    }

    [Fact]
    public async Task CreateCategory_WithoutPattern_NoRuleCreated()
    {
        await using var db = CreateDb(nameof(CreateCategory_WithoutPattern_NoRuleCreated));

        var handler  = CreateCategoryHandler(db);
        var response = await handler.HandleAsync(new CreateCategoryCommand("Test", "#aaa", null, null));

        var rules = await db.CategoryRules.Where(r => r.CategoryId == response.Id).ToListAsync();
        Assert.Empty(rules);
    }
}
