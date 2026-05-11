using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceHub.Api.Data;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Backup.Commands.CreateBackup;

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
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FinanceHub", "Backups");
        Directory.CreateDirectory(backupDir);
        var backupFile = Path.Combine(backupDir, "FinanceHub_backup.json");

        var payload = new BackupPayload
        {
            Categories           = await db.Categories.AsNoTracking().ToListAsync(ct),
            CategoryRules        = await db.CategoryRules.AsNoTracking().ToListAsync(ct),
            MonthlyStatements    = await db.MonthlyStatements.AsNoTracking().ToListAsync(ct),
            Transactions         = await db.Transactions.AsNoTracking().ToListAsync(ct),
            SalaryProfiles       = await db.SalaryProfiles.AsNoTracking().ToListAsync(ct),
            SalaryItemCategories = await db.SalaryItemCategories.AsNoTracking().ToListAsync(ct),
            SalarySlips          = await db.SalarySlips.AsNoTracking().ToListAsync(ct),
            SalaryLineItems      = await db.SalaryLineItems.AsNoTracking().ToListAsync(ct),
        };

        await File.WriteAllTextAsync(backupFile, JsonSerializer.Serialize(payload, _jsonOptions), ct);

        logger.LogInformation("Backup created at {Path}", backupFile);
        return new CreateBackupResponse("Backup created successfully.", backupFile);
    }
}

internal sealed class BackupPayload
{
    public List<Category>           Categories           { get; set; } = [];
    public List<CategoryRule>       CategoryRules        { get; set; } = [];
    public List<MonthlyStatement>   MonthlyStatements    { get; set; } = [];
    public List<Transaction>        Transactions         { get; set; } = [];
    public List<SalaryProfile>      SalaryProfiles       { get; set; } = [];
    public List<SalaryItemCategory> SalaryItemCategories { get; set; } = [];
    public List<SalarySlip>         SalarySlips          { get; set; } = [];
    public List<SalaryLineItem>     SalaryLineItems      { get; set; } = [];
}
