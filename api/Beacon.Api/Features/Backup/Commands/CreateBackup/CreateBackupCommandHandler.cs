using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Api.Data;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Backup.Commands.CreateBackup;

public record CreateBackupResponse(string Message, string Path);

public class CreateBackupCommandHandler(AppDbContext db, IConfiguration config, ILogger<CreateBackupCommandHandler> logger)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    public async Task<CreateBackupResponse> HandleAsync(CancellationToken ct = default)
    {
        var backupDir = config["Backup:Path"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Beacon", "Backups");
        Directory.CreateDirectory(backupDir);
        var backupFile = Path.Combine(backupDir, "Beacon_backup.json");

        var payload = new BackupPayload
        {
            Categories                      = await db.Categories.AsNoTracking().ToListAsync(ct),
            CategoryRules                   = await db.CategoryRules.AsNoTracking().ToListAsync(ct),
            MonthlyStatements               = await db.MonthlyStatements.AsNoTracking().ToListAsync(ct),
            Transactions                    = await db.Transactions.AsNoTracking().ToListAsync(ct),
            SalaryProfiles                  = await db.SalaryProfiles.AsNoTracking().ToListAsync(ct),
            SalaryItemCategories            = await db.SalaryItemCategories.AsNoTracking().ToListAsync(ct),
            SalarySlips                     = await db.SalarySlips.AsNoTracking().ToListAsync(ct),
            SalaryLineItems                 = await db.SalaryLineItems.AsNoTracking().ToListAsync(ct),
            GroceryCategories               = await db.GroceryCategories.AsNoTracking().ToListAsync(ct),
            GroceryReceiptCategoryMappings  = await db.GroceryReceiptCategoryMappings.AsNoTracking().ToListAsync(ct),
            GroceryReceipts                 = await db.GroceryReceipts.AsNoTracking().ToListAsync(ct),
            GroceryItems                    = await db.GroceryItems.AsNoTracking().ToListAsync(ct),
            GroceryCategoryRules            = await db.GroceryCategoryRules.AsNoTracking().ToListAsync(ct),
            InvestmentAssets                = await db.InvestmentAssets.AsNoTracking().ToListAsync(ct),
            InvestmentLots                  = await db.InvestmentLots.AsNoTracking().ToListAsync(ct),
            InvestmentPriceSnapshots        = await db.InvestmentPriceSnapshots.AsNoTracking().ToListAsync(ct),
        };

        await File.WriteAllTextAsync(backupFile, JsonSerializer.Serialize(payload, _jsonOptions), ct);

        logger.LogInformation("Backup created at {Path}", backupFile);
        return new CreateBackupResponse("Backup created successfully.", backupFile);
    }
}

internal sealed class BackupPayload
{
    public List<Category>                      Categories                     { get; set; } = [];
    public List<CategoryRule>                  CategoryRules                  { get; set; } = [];
    public List<MonthlyStatement>              MonthlyStatements              { get; set; } = [];
    public List<Transaction>                   Transactions                   { get; set; } = [];
    public List<SalaryProfile>                 SalaryProfiles                 { get; set; } = [];
    public List<SalaryItemCategory>            SalaryItemCategories           { get; set; } = [];
    public List<SalarySlip>                    SalarySlips                    { get; set; } = [];
    public List<SalaryLineItem>                SalaryLineItems                { get; set; } = [];
    public List<GroceryCategory>               GroceryCategories              { get; set; } = [];
    public List<GroceryReceiptCategoryMapping> GroceryReceiptCategoryMappings { get; set; } = [];
    public List<GroceryReceipt>                GroceryReceipts                { get; set; } = [];
    public List<GroceryItem>                   GroceryItems                   { get; set; } = [];
    public List<GroceryCategoryRule>           GroceryCategoryRules           { get; set; } = [];
    public List<InvestmentAsset>               InvestmentAssets               { get; set; } = [];
    public List<InvestmentLot>                 InvestmentLots                 { get; set; } = [];
    public List<InvestmentPriceSnapshot>       InvestmentPriceSnapshots       { get; set; } = [];
}
