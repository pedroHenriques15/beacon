using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Api.Data;
using Beacon.Api.Features.Backup.Commands.CreateBackup;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Backup.Commands.RestoreBackup;

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
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Beacon", "Backups");
        var backupFile = Path.Combine(backupDir, "Beacon_backup.json");

        if (!File.Exists(backupFile))
            return null;

        var json = await File.ReadAllTextAsync(backupFile, ct);
        var payload = JsonSerializer.Deserialize<BackupPayload>(json, _jsonOptions)
                      ?? throw new InvalidOperationException("Backup file is empty or corrupt.");
        StorePdfPathsAsFileNames(payload);

        db.ChangeTracker.Clear();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.InvestmentPriceSnapshots.ExecuteDeleteAsync(ct);
        await db.InvestmentLots.ExecuteDeleteAsync(ct);
        await db.InvestmentAssets.ExecuteDeleteAsync(ct);
        await db.GroceryItems.ExecuteDeleteAsync(ct);
        await db.GroceryCategoryRules.ExecuteDeleteAsync(ct);
        await db.GroceryReceiptCategoryMappings.ExecuteDeleteAsync(ct);
        await db.GroceryReceipts.ExecuteDeleteAsync(ct);
        await db.GroceryCategories.ExecuteDeleteAsync(ct);
        await db.SalaryLineItems.ExecuteDeleteAsync(ct);
        await db.SalarySlips.ExecuteDeleteAsync(ct);
        await db.SalaryItemCategories.ExecuteDeleteAsync(ct);
        await db.Transactions.ExecuteDeleteAsync(ct);
        await db.CategoryRules.ExecuteDeleteAsync(ct);
        await db.MonthlyStatements.ExecuteDeleteAsync(ct);
        await db.SalaryProfiles.ExecuteDeleteAsync(ct);
        await db.Categories.ExecuteDeleteAsync(ct);

        await InsertKeepingIds(db, payload.Categories, ct);
        await InsertKeepingIds(db, payload.CategoryRules, ct);
        await InsertKeepingIds(db, payload.MonthlyStatements, ct);
        await InsertKeepingIds(db, payload.Transactions, ct);
        await InsertKeepingIds(db, payload.SalaryProfiles, ct);
        await InsertKeepingIds(db, payload.SalaryItemCategories, ct);
        await InsertKeepingIds(db, payload.SalarySlips, ct);
        await InsertKeepingIds(db, payload.SalaryLineItems, ct);
        await InsertKeepingIds(db, payload.GroceryCategories, ct);
        await InsertKeepingIds(db, payload.GroceryReceiptCategoryMappings, ct);
        await InsertKeepingIds(db, payload.GroceryReceipts, ct);
        await InsertKeepingIds(db, payload.GroceryItems, ct);
        await InsertKeepingIds(db, payload.GroceryCategoryRules, ct);
        await InsertKeepingIds(db, payload.InvestmentAssets, ct);
        await InsertKeepingIds(db, payload.InvestmentLots, ct);
        await InsertKeepingIds(db, payload.InvestmentPriceSnapshots, ct);

        await tx.CommitAsync(ct);

        logger.LogInformation("Database restored from {Path}", backupFile);
        return backupFile;
    }

    /// <summary>
    /// A backup taken before PDF paths became file names holds absolute paths from the machine that
    /// wrote it; keep only the file name, which <see cref="FileStorageService"/> resolves against this
    /// machine's storage root.
    /// </summary>
    internal static void StorePdfPathsAsFileNames(BackupPayload payload)
    {
        foreach (var statement in payload.MonthlyStatements)
            statement.PdfPath = FileStorageService.FileNameOf(statement.PdfPath);
        foreach (var slip in payload.SalarySlips)
            slip.PdfPath = FileStorageService.FileNameOf(slip.PdfPath);
        foreach (var receipt in payload.GroceryReceipts)
            receipt.PdfPath = FileStorageService.FileNameOf(receipt.PdfPath);
    }

    /// <summary>Rows keep their backed-up ids: SQLite inserts an explicit key as given.</summary>
    private static async Task InsertKeepingIds<T>(AppDbContext db, IEnumerable<T> entities, CancellationToken ct)
        where T : class
    {
        var list = entities.ToList();
        if (list.Count == 0) return;

        db.Set<T>().AddRange(list);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
