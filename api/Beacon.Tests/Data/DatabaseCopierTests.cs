using Beacon.Api.Data;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests.Data;

public class DatabaseCopierTests
{
    /// <summary>One row in every table, with ids that are not 1, 2, 3 so a renumbering shows.</summary>
    private static async Task SeedEveryTableAsync(AppDbContext db)
    {
        var category = new Category { Id = 7, Name = "Food", Color = "#ff0000" };
        category.Rules.Add(new CategoryRule { Id = 11, Pattern = "LIDL", Value = 12.5m });
        db.Categories.Add(category);
        db.MonthlyStatements.Add(new MonthlyStatement
        {
            Id = 40,
            Bank = "ACTIVOBANK",
            Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1),
            PeriodTo = new DateOnly(2026, 1, 31),
            OpeningBalance = 1000m,
            ClosingBalance = 970m,
            SourceFile = "statement.pdf",
            ImportedAt = new DateTime(2026, 2, 1, 9, 30, 15, 123).AddTicks(4567),
            Transactions =
            [
                new Transaction
                {
                    Id = 42,
                    Description = "LIDL LISBOA",
                    Amount = 30m,
                    Type = "debit",
                    Balance = 970m,
                    DatePosting = new DateOnly(2026, 1, 5),
                    DateValue = new DateOnly(2026, 1, 5),
                    CategoryId = 7,
                    CategoryRuleId = 11,
                },
            ],
        });

        var profile = new SalaryProfile { Id = 3, Name = "Acme" };
        var itemCategory = new SalaryItemCategory { Id = 9, Name = "Base", ItemType = "income", SalaryProfile = profile };
        db.SalarySlips.Add(new SalarySlip
        {
            Id = 5,
            SalaryProfile = profile,
            Period = new DateOnly(2026, 1, 1),
            GrossAmount = 1000m,
            NetAmount = 800m,
            HourlyRate = 6.25m,
            LineItems = [new SalaryLineItem { Id = 21, SalaryItemCategory = itemCategory, Amount = 1000m, Percentage = 11m }],
        });

        var groceryCategory = new GroceryCategory { Id = 4, Name = "Fruit" };
        groceryCategory.Rules.Add(new GroceryCategoryRule { Id = 6, Pattern = "BANANA" });
        db.GroceryReceiptCategoryMappings.Add(new GroceryReceiptCategoryMapping
        {
            Id = 2,
            ReceiptCategoryName = "Frutas",
            Category = groceryCategory,
        });
        db.GroceryReceipts.Add(new GroceryReceipt
        {
            Id = 13,
            StoreName = "Continente",
            ReceiptDate = new DateOnly(2026, 1, 10),
            Total = 1.5m,
            Items = [new GroceryItem { Id = 31, Description = "BANANA", Amount = 1.5m, Quantity = 1.234m, Category = groceryCategory }],
        });

        var asset = new InvestmentAsset { Id = 8, AssetType = "ETF", Ticker = "VWCE", Isin = "IE00BK5BQT80", Name = "Vanguard FTSE All-World" };
        asset.Lots.Add(new InvestmentLot { Id = 17, Date = new DateOnly(2026, 1, 5), Quantity = 0.031295m, PricePerUnit = 97.8123m, Fees = 1m });
        asset.PriceSnapshots.Add(new InvestmentPriceSnapshot { Id = 99, Date = new DateOnly(2026, 1, 20), PricePerUnit = 105.5m });
        db.InvestmentAssets.Add(asset);

        db.GoogleOAuthTokens.Add(new GoogleOAuthToken
        {
            Id = 1,
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAt = new DateTime(2026, 3, 1, 12, 0, 0),
            Scopes = "calendar tasks",
            ConnectedAt = new DateTime(2026, 2, 1, 12, 0, 0),
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CopyAsync_CopiesEveryTableKeepingIdsAndValues()
    {
        using var sourceDatabase = new SqliteTestDatabase();
        using var targetDatabase = new SqliteTestDatabase();
        await using (var seed = sourceDatabase.CreateContext())
            await SeedEveryTableAsync(seed);

        await using var source = sourceDatabase.CreateContext();
        await using var target = targetDatabase.CreateContext();
        await DatabaseCopier.CopyAsync(source, target);
        var tables = await DatabaseCopier.CompareAsync(source, target);

        Assert.Equal(17, tables.Count);
        Assert.All(tables, t =>
        {
            Assert.Empty(t.Differences);
            Assert.Equal(1, t.SourceRows);
            Assert.Equal(1, t.TargetRows);
        });
        var transaction = await target.Transactions.SingleAsync();
        Assert.Equal((42, 40, 7), (transaction.Id, transaction.StatementId, transaction.CategoryId!.Value));
        Assert.Equal(0.031295m, (await target.InvestmentLots.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task CompareAsync_ReportsAChangedValue()
    {
        using var sourceDatabase = new SqliteTestDatabase();
        using var targetDatabase = new SqliteTestDatabase();
        await using (var seed = sourceDatabase.CreateContext())
            await SeedEveryTableAsync(seed);
        await using (var source = sourceDatabase.CreateContext())
        await using (var target = targetDatabase.CreateContext())
            await DatabaseCopier.CopyAsync(source, target);

        await using (var change = targetDatabase.CreateContext())
        {
            (await change.Transactions.SingleAsync()).Amount = 31m;
            await change.SaveChangesAsync();
        }

        await using var sourceAfter = sourceDatabase.CreateContext();
        await using var targetAfter = targetDatabase.CreateContext();
        var tables = await DatabaseCopier.CompareAsync(sourceAfter, targetAfter);

        var transactions = Assert.Single(tables, t => t.Differences.Count > 0);
        Assert.Equal("Transactions", transactions.Table);
        Assert.Equal("id 42, Amount: '30.0' became '31.0'", Assert.Single(transactions.Differences));
    }
}
