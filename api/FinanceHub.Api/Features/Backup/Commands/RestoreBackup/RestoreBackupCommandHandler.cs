using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Backup.Commands.RestoreBackup;

public class RestoreBackupCommandHandler(AppDbContext db, IConfiguration config, ILogger<RestoreBackupCommandHandler> logger)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    public async Task<string?> HandleAsync(CancellationToken ct = default)
    {
        var backupDir = config["Backup:Path"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FinanceHub", "Backups");
        var backupFile = Path.Combine(backupDir, "FinanceHub_backup.json");

        if (!File.Exists(backupFile))
            return null;

        var json    = await File.ReadAllTextAsync(backupFile, ct);
        var payload = JsonSerializer.Deserialize<CreateBackup.BackupPayload>(json, _jsonOptions)
                      ?? throw new InvalidOperationException("Backup file is empty or corrupt.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlRawAsync("DELETE FROM [GroceryItems]",                   ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [GroceryCategoryRules]",          ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [GroceryReceiptCategoryMappings]", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [GroceryReceipts]",               ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [GroceryCategories]",             ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [SalaryLineItems]",               ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [SalarySlips]",                   ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [SalaryItemCategories]",          ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [Transactions]",                  ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [CategoryRules]",                 ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [MonthlyStatements]",             ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [SalaryProfiles]",                ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [Categories]",                    ct);

        await InsertWithIdentity(db, "Categories",                     payload.Categories,                     ct);
        await InsertWithIdentity(db, "CategoryRules",                  payload.CategoryRules,                  ct);
        await InsertWithIdentity(db, "MonthlyStatements",              payload.MonthlyStatements,              ct);
        await InsertWithIdentity(db, "Transactions",                   payload.Transactions,                   ct);
        await InsertWithIdentity(db, "SalaryProfiles",                 payload.SalaryProfiles,                 ct);
        await InsertWithIdentity(db, "SalaryItemCategories",           payload.SalaryItemCategories,           ct);
        await InsertWithIdentity(db, "SalarySlips",                    payload.SalarySlips,                    ct);
        await InsertWithIdentity(db, "SalaryLineItems",                payload.SalaryLineItems,                ct);
        await InsertWithIdentity(db, "GroceryCategories",              payload.GroceryCategories,              ct);
        await InsertWithIdentity(db, "GroceryReceiptCategoryMappings", payload.GroceryReceiptCategoryMappings, ct);
        await InsertWithIdentity(db, "GroceryReceipts",                payload.GroceryReceipts,                ct);
        await InsertWithIdentity(db, "GroceryItems",                   payload.GroceryItems,                   ct);
        await InsertWithIdentity(db, "GroceryCategoryRules",           payload.GroceryCategoryRules,           ct);

        await tx.CommitAsync(ct);

        logger.LogInformation("Database restored from {Path}", backupFile);
        return backupFile;
    }

    private static async Task InsertWithIdentity<T>(AppDbContext db, string tableName, IEnumerable<T> entities, CancellationToken ct)
        where T : class
    {
        var list = entities.ToList();
        if (list.Count == 0) return;

#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [dbo].[{tableName}] ON", ct);
        db.Set<T>().AddRange(list);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [dbo].[{tableName}] OFF", ct);
#pragma warning restore EF1002
        db.ChangeTracker.Clear();
    }
}
