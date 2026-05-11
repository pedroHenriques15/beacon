using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Categories.Shared;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceHub.Tests.Services;

public class ApplyRuleServiceTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        var cat = new Category { Name = "Groceries", Color = "#00ff00" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var statement = new MonthlyStatement
        {
            Bank       = "TESTBANK",
            Account    = "123",
            PeriodFrom = new DateOnly(2024, 1, 1),
            PeriodTo   = new DateOnly(2024, 1, 31),
            Transactions =
            [
                new Transaction { Description = "LIDL Lisboa",    Amount = 30, Type = "debit",  DatePosting = new DateOnly(2024,1,1), DateValue = new DateOnly(2024,1,1), Balance = 970 },
                new Transaction { Description = "CONTINENTE ABC", Amount = 50, Type = "debit",  DatePosting = new DateOnly(2024,1,2), DateValue = new DateOnly(2024,1,2), Balance = 920 },
                new Transaction { Description = "SALARY",         Amount = 1000, Type = "credit",DatePosting = new DateOnly(2024,1,5), DateValue = new DateOnly(2024,1,5), Balance = 1920 }
            ]
        };
        db.MonthlyStatements.Add(statement);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ApplyAsync_MatchingTransactions_AreAssignedCategory()
    {
        await using var db = CreateDb(nameof(ApplyAsync_MatchingTransactions_AreAssignedCategory));
        await SeedAsync(db);

        var cat  = await db.Categories.FirstAsync();
        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "LIDL" };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        await service.ApplyAsync(rule);

        var tx = await db.Transactions.FirstAsync(t => t.Description == "LIDL Lisboa");
        Assert.Equal(cat.Id,  tx.CategoryId);
        Assert.Equal(rule.Id, tx.CategoryRuleId);
        Assert.False(tx.CategorySetManually);
    }

    [Fact]
    public async Task ApplyAsync_NonMatchingTransactions_AreNotChanged()
    {
        await using var db = CreateDb(nameof(ApplyAsync_NonMatchingTransactions_AreNotChanged));
        await SeedAsync(db);

        var cat  = await db.Categories.FirstAsync();
        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "LIDL" };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        await service.ApplyAsync(rule);

        var salary = await db.Transactions.FirstAsync(t => t.Description == "SALARY");
        Assert.Null(salary.CategoryId);
        Assert.Null(salary.CategoryRuleId);
    }

    [Fact]
    public async Task ApplyAsync_AlreadyCategorizedTransactions_AreSkipped()
    {
        await using var db = CreateDb(nameof(ApplyAsync_AlreadyCategorizedTransactions_AreSkipped));

        var cat1 = new Category { Name = "Food",    Color = "#ff0000" };
        var cat2 = new Category { Name = "Savings", Color = "#0000ff" };
        db.Categories.AddRange(cat1, cat2);
        await db.SaveChangesAsync();

        var statement = new MonthlyStatement
        {
            Bank = "TESTBANK", Account = "123",
            PeriodFrom = new DateOnly(2024,1,1), PeriodTo = new DateOnly(2024,1,31),
            Transactions =
            [
                new Transaction
                {
                    Description = "LIDL",
                    Amount = 10, Type = "debit",
                    DatePosting = new DateOnly(2024,1,1), DateValue = new DateOnly(2024,1,1), Balance = 990,
                    CategoryId = cat1.Id,
                    CategorySetManually = true
                }
            ]
        };
        db.MonthlyStatements.Add(statement);
        await db.SaveChangesAsync();

        var rule = new CategoryRule { CategoryId = cat2.Id, Pattern = "LIDL" };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        await service.ApplyAsync(rule);

        var tx = await db.Transactions.FirstAsync(t => t.Description == "LIDL");
        Assert.Equal(cat1.Id, tx.CategoryId);
        Assert.True(tx.CategorySetManually);
    }

    [Fact]
    public async Task ApplyAsync_NoMatches_DoesNotThrow()
    {
        await using var db = CreateDb(nameof(ApplyAsync_NoMatches_DoesNotThrow));
        await SeedAsync(db);

        var cat  = await db.Categories.FirstAsync();
        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "NONEXISTENT_PATTERN_XYZ" };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        var ex = await Record.ExceptionAsync(() => service.ApplyAsync(rule));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ApplyAsync_MultipleTxMatchPattern_AllAreAssigned()
    {
        await using var db = CreateDb(nameof(ApplyAsync_MultipleTxMatchPattern_AllAreAssigned));
        await SeedAsync(db);

        var cat  = await db.Categories.FirstAsync();
        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "IDENTI" };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var allTxs = await db.Transactions.Where(t => t.CategoryId == null).ToListAsync();
        Assert.True(allTxs.Count >= 2);

        var rule2 = new CategoryRule { CategoryId = cat.Id, Pattern = "L" };
        db.CategoryRules.Add(rule2);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        await service.ApplyAsync(rule2);

        var matched = await db.Transactions
            .Where(t => t.CategoryRuleId == rule2.Id)
            .ToListAsync();

        Assert.All(matched, t => Assert.Contains("L", t.Description, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAsync_PatternMatchIsCaseSensitive()
    {
        await using var db = CreateDb(nameof(ApplyAsync_PatternMatchIsCaseSensitive));

        var cat = new Category { Name = "Test", Color = "#aaaaaa" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var statement = new MonthlyStatement
        {
            Bank = "B", Account = "A",
            PeriodFrom = new DateOnly(2024,1,1), PeriodTo = new DateOnly(2024,1,31),
            Transactions =
            [
                new Transaction { Description = "lowercase match", Amount = 1, Type = "debit", DatePosting = new DateOnly(2024,1,1), DateValue = new DateOnly(2024,1,1), Balance = 0 },
                new Transaction { Description = "UPPERCASE MATCH",  Amount = 1, Type = "debit", DatePosting = new DateOnly(2024,1,1), DateValue = new DateOnly(2024,1,1), Balance = 0 }
            ]
        };
        db.MonthlyStatements.Add(statement);
        await db.SaveChangesAsync();

        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "lowercase" };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        await service.ApplyAsync(rule);

        var lower = await db.Transactions.FirstAsync(t => t.Description == "lowercase match");
        var upper = await db.Transactions.FirstAsync(t => t.Description == "UPPERCASE MATCH");

        Assert.Equal(cat.Id, lower.CategoryId);
        Assert.Null(upper.CategoryId);
    }

    [Fact]
    public async Task ApplyAsync_ValueOnlyRule_MatchesTransactionsByAmount()
    {
        await using var db = CreateDb(nameof(ApplyAsync_ValueOnlyRule_MatchesTransactionsByAmount));

        var cat = new Category { Name = "Salary", Color = "#00ff00" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var statement = new MonthlyStatement
        {
            Bank = "TESTBANK", Account = "123",
            PeriodFrom = new DateOnly(2024,1,1), PeriodTo = new DateOnly(2024,1,31),
            Transactions =
            [
                new Transaction { Description = "EMPLOYER CREDIT", Amount = 1500, Type = "credit", DatePosting = new DateOnly(2024,1,5), DateValue = new DateOnly(2024,1,5), Balance = 1500 },
                new Transaction { Description = "OTHER CREDIT",    Amount = 200,  Type = "credit", DatePosting = new DateOnly(2024,1,6), DateValue = new DateOnly(2024,1,6), Balance = 1700 }
            ]
        };
        db.MonthlyStatements.Add(statement);
        await db.SaveChangesAsync();

        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = string.Empty, Value = 1500m };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        await service.ApplyAsync(rule);

        var matched   = await db.Transactions.FirstAsync(t => t.Description == "EMPLOYER CREDIT");
        var unmatched = await db.Transactions.FirstAsync(t => t.Description == "OTHER CREDIT");

        Assert.Equal(cat.Id,  matched.CategoryId);
        Assert.Equal(rule.Id, matched.CategoryRuleId);
        Assert.False(matched.CategorySetManually);
        Assert.Null(unmatched.CategoryId);
    }

    [Fact]
    public async Task ApplyAsync_PatternAndValueRule_RequiresBothConditions()
    {
        await using var db = CreateDb(nameof(ApplyAsync_PatternAndValueRule_RequiresBothConditions));

        var cat = new Category { Name = "Groceries", Color = "#ff9900" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var statement = new MonthlyStatement
        {
            Bank = "TESTBANK", Account = "123",
            PeriodFrom = new DateOnly(2024,1,1), PeriodTo = new DateOnly(2024,1,31),
            Transactions =
            [
                new Transaction { Description = "LIDL Lisboa", Amount = 30, Type = "debit", DatePosting = new DateOnly(2024,1,1), DateValue = new DateOnly(2024,1,1), Balance = 970 },
                new Transaction { Description = "LIDL Lisboa", Amount = 99, Type = "debit", DatePosting = new DateOnly(2024,1,2), DateValue = new DateOnly(2024,1,2), Balance = 871 },
                new Transaction { Description = "OTHER STORE", Amount = 30, Type = "debit", DatePosting = new DateOnly(2024,1,3), DateValue = new DateOnly(2024,1,3), Balance = 841 }
            ]
        };
        db.MonthlyStatements.Add(statement);
        await db.SaveChangesAsync();

        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "LIDL", Value = 30m };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        await service.ApplyAsync(rule);

        var txs = await db.Transactions.ToListAsync();
        var lidl30    = txs.First(t => t.Description == "LIDL Lisboa" && t.Amount == 30);
        var lidl99    = txs.First(t => t.Description == "LIDL Lisboa" && t.Amount == 99);
        var other30   = txs.First(t => t.Description == "OTHER STORE");

        Assert.Equal(cat.Id,  lidl30.CategoryId);
        Assert.Equal(rule.Id, lidl30.CategoryRuleId);
        Assert.Null(lidl99.CategoryId);
        Assert.Null(other30.CategoryId);
    }

    [Fact]
    public async Task ApplyAsync_PatternAndValueRule_DoesNotMatchWhenOnlyPatternMatches()
    {
        await using var db = CreateDb(nameof(ApplyAsync_PatternAndValueRule_DoesNotMatchWhenOnlyPatternMatches));

        var cat = new Category { Name = "Test", Color = "#aaaaaa" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var statement = new MonthlyStatement
        {
            Bank = "TESTBANK", Account = "123",
            PeriodFrom = new DateOnly(2024,1,1), PeriodTo = new DateOnly(2024,1,31),
            Transactions =
            [
                new Transaction { Description = "LIDL Lisboa", Amount = 99, Type = "debit", DatePosting = new DateOnly(2024,1,1), DateValue = new DateOnly(2024,1,1), Balance = 901 }
            ]
        };
        db.MonthlyStatements.Add(statement);
        await db.SaveChangesAsync();

        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "LIDL", Value = 30m };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        await service.ApplyAsync(rule);

        var tx = await db.Transactions.FirstAsync();
        Assert.Null(tx.CategoryId);
    }

    [Fact]
    public async Task ApplyAsync_PatternAndValueRule_DoesNotMatchWhenOnlyValueMatches()
    {
        await using var db = CreateDb(nameof(ApplyAsync_PatternAndValueRule_DoesNotMatchWhenOnlyValueMatches));

        var cat = new Category { Name = "Test", Color = "#aaaaaa" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var statement = new MonthlyStatement
        {
            Bank = "TESTBANK", Account = "123",
            PeriodFrom = new DateOnly(2024,1,1), PeriodTo = new DateOnly(2024,1,31),
            Transactions =
            [
                new Transaction { Description = "OTHER STORE", Amount = 30, Type = "debit", DatePosting = new DateOnly(2024,1,1), DateValue = new DateOnly(2024,1,1), Balance = 970 }
            ]
        };
        db.MonthlyStatements.Add(statement);
        await db.SaveChangesAsync();

        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "LIDL", Value = 30m };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        var service = new ApplyRuleService(db);
        await service.ApplyAsync(rule);

        var tx = await db.Transactions.FirstAsync();
        Assert.Null(tx.CategoryId);
    }
}
