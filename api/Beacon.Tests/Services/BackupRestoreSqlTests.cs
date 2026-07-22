using Beacon.Api.Data;
using Beacon.Api.Features.Backup.Commands.CreateBackup;
using Beacon.Api.Features.Backup.Commands.RestoreBackup;
using Beacon.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Services;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BEACON_TEST_SQLSERVER")))
            Skip = "Set BEACON_TEST_SQLSERVER to a SQL Server connection string to run SQL-backed tests.";
    }
}

public class BackupRestoreSqlTests
{
    private static string TestConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("BEACON_TEST_SQLSERVER"))
        {
            InitialCatalog = "BeaconBackupRoundTripTest",
        };
        return builder.ConnectionString;
    }

    private static AppDbContext CreateSqlContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestConnectionString(),
                sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null))
            .Options);

    private static IConfiguration BackupConfig(string backupDir) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Backup:Path"] = backupDir })
            .Build();

    [SqlServerFact]
    public async Task CreateBackup_ThenRestore_RoundTripsAllTablesUnderRetryStrategy()
    {
        var backupDir = Path.Combine(Path.GetTempPath(), $"fh_backup_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(backupDir);
        var config = BackupConfig(backupDir);

        await using var db = CreateSqlContext();
        try
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            var category = new Category { Name = "Food", Color = "#ff0000" };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            db.CategoryRules.Add(new CategoryRule { CategoryId = category.Id, Pattern = "LIDL" });

            var statement = new MonthlyStatement
            {
                Bank = "ACTIVOBANK", Account = "PT50",
                PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
                Currency = "EUR", OpeningBalance = 1000m, ClosingBalance = 970m,
                Transactions =
                [
                    new Transaction { Description = "LIDL LISBOA", Amount = 30m, Type = "debit",
                        DatePosting = new DateOnly(2026, 1, 5), DateValue = new DateOnly(2026, 1, 5), Balance = 970m }
                ]
            };
            db.MonthlyStatements.Add(statement);

            var profile = new SalaryProfile { Name = "Acme" };
            db.SalaryProfiles.Add(profile);
            await db.SaveChangesAsync();

            var itemCategory = new SalaryItemCategory
            {
                SalaryProfileId = profile.Id, Name = "Base", Color = "#22c55e", ItemType = "income"
            };
            db.SalaryItemCategories.Add(itemCategory);
            var slip = new SalarySlip
            {
                SalaryProfileId = profile.Id, Period = new DateOnly(2026, 1, 1),
                GrossAmount = 1000m, NetAmount = 800m,
            };
            db.SalarySlips.Add(slip);
            await db.SaveChangesAsync();
            db.SalaryLineItems.Add(new SalaryLineItem
            {
                SalarySlipId = slip.Id, SalaryItemCategoryId = itemCategory.Id, Amount = 1000m, SortOrder = 0
            });

            var groceryCategory = new GroceryCategory { Name = "Fruit", Color = "#a855f7" };
            db.GroceryCategories.Add(groceryCategory);
            await db.SaveChangesAsync();
            db.GroceryCategoryRules.Add(new GroceryCategoryRule { CategoryId = groceryCategory.Id, Pattern = "BANANA" });
            db.GroceryReceiptCategoryMappings.Add(new GroceryReceiptCategoryMapping
            {
                ReceiptCategoryName = "Frutas", GroceryCategoryId = groceryCategory.Id
            });
            var receipt = new GroceryReceipt
            {
                StoreName = "Continente", ReceiptDate = new DateOnly(2026, 1, 10), Total = 1.5m,
                Items = [new GroceryItem { Description = "BANANA", Amount = 1.5m, Quantity = 1 }]
            };
            db.GroceryReceipts.Add(receipt);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var createHandler = new CreateBackupCommandHandler(db, config, NullLogger<CreateBackupCommandHandler>.Instance);
            await createHandler.HandleAsync();
            Assert.True(File.Exists(Path.Combine(backupDir, "Beacon_backup.json")));

            db.Categories.Add(new Category { Name = "Intruder", Color = "#000000" });
            var tx = await db.Transactions.SingleAsync();
            var originalTxId = tx.Id;
            db.Transactions.Remove(tx);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var restoreHandler = new RestoreBackupCommandHandler(db, config, NullLogger<RestoreBackupCommandHandler>.Instance);
            var restoredFrom = await restoreHandler.HandleAsync();
            Assert.NotNull(restoredFrom);

            await using var verifyDb = CreateSqlContext();
            Assert.Equal(1, await verifyDb.Categories.CountAsync());
            Assert.False(await verifyDb.Categories.AnyAsync(c => c.Name == "Intruder"));
            Assert.Equal(1, await verifyDb.CategoryRules.CountAsync());
            Assert.Equal(1, await verifyDb.MonthlyStatements.CountAsync());

            var restoredTx = await verifyDb.Transactions.SingleAsync();
            Assert.Equal(originalTxId, restoredTx.Id);
            Assert.Equal("LIDL LISBOA", restoredTx.Description);
            Assert.Equal(30m, restoredTx.Amount);

            Assert.Equal(1, await verifyDb.SalaryProfiles.CountAsync());
            Assert.Equal(1, await verifyDb.SalaryItemCategories.CountAsync());
            Assert.Equal(1, await verifyDb.SalarySlips.CountAsync());
            Assert.Equal(1, await verifyDb.SalaryLineItems.CountAsync());
            Assert.Equal(1, await verifyDb.GroceryCategories.CountAsync());
            Assert.Equal(1, await verifyDb.GroceryCategoryRules.CountAsync());
            Assert.Equal(1, await verifyDb.GroceryReceiptCategoryMappings.CountAsync());
            Assert.Equal(1, await verifyDb.GroceryReceipts.CountAsync());
            Assert.Equal(1, await verifyDb.GroceryItems.CountAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            Directory.Delete(backupDir, recursive: true);
        }
    }
}
