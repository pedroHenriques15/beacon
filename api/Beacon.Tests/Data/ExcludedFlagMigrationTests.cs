using Beacon.Api.Features.Shared;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Beacon.Tests.Data;

/// <summary>The migration that flags rows already in the Excluded category, run over data from before it.</summary>
public class ExcludedFlagMigrationTests : IDisposable
{
    private const string Before = "20261001200740_PriceHistorySync";
    private const string Migration = "20261006173257_FlagRowsInExcludedCategory";

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static Transaction Tx(string description, Category? category, bool isExcluded) => new()
    {
        DatePosting = new DateOnly(2026, 8, 3),
        DateValue = new DateOnly(2026, 8, 3),
        Description = description,
        Amount = 50m,
        Type = "debit",
        Category = category,
        IsExcluded = isExcluded,
    };

    [Fact]
    public async Task RowsInExcludedBecomeExcluded_AndNoFlagIsCleared()
    {
        await using var db = _database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);

        var excluded = new Category { Name = ExcludedCategory.Name, Color = "#64748b", IsProtected = true };
        var groceries = new Category { Name = "Groceries", Color = "#00ff00" };
        db.MonthlyStatements.Add(new MonthlyStatement
        {
            Bank = "TRADE REPUBLIC",
            Account = "PT50",
            PeriodFrom = new DateOnly(2026, 8, 1),
            PeriodTo = new DateOnly(2026, 8, 31),
            Transactions =
            [
                Tx("TOP-UP BY RULE", excluded, isExcluded: false),
                Tx("Savings plan execution", null, isExcluded: true),
                Tx("Savings plan execution, categorised later", groceries, isExcluded: true),
                Tx("MINI MERCADO", groceries, isExcluded: false),
            ],
        });
        var groceryExcluded = new GroceryCategory { Name = ExcludedCategory.Name, Color = "#64748b", IsProtected = true };
        var bakery = new GroceryCategory { Name = "Bakery", Color = "#f00" };
        db.GroceryReceipts.Add(new GroceryReceipt
        {
            StoreName = "Continente",
            ReceiptDate = new DateOnly(2026, 8, 3),
            Total = 1.30m,
            SourceFile = "receipt.pdf",
            Items =
            [
                new GroceryItem { Description = "SACO PAPEL", Amount = 0.10m, Quantity = 1, Category = groceryExcluded },
                new GroceryItem { Description = "BREAD", Amount = 1.20m, Quantity = 1, Category = bakery },
            ],
        });
        await db.SaveChangesAsync();

        await migrator.MigrateAsync(Migration);

        db.ChangeTracker.Clear();
        var txs = await db.Transactions.ToDictionaryAsync(t => t.Description, t => t.IsExcluded);
        Assert.True(txs["TOP-UP BY RULE"]);
        Assert.True(txs["Savings plan execution"]);
        Assert.True(txs["Savings plan execution, categorised later"]);
        Assert.False(txs["MINI MERCADO"]);
        var items = await db.GroceryItems.ToDictionaryAsync(i => i.Description, i => i.IsExcluded);
        Assert.True(items["SACO PAPEL"]);
        Assert.False(items["BREAD"]);
    }
}
