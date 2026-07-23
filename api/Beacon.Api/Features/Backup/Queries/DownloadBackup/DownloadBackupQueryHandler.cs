namespace Beacon.Api.Features.Backup.Queries.DownloadBackup;

public record DownloadBackupResult(FileStream Stream, string FileName);

public class DownloadBackupQueryHandler(IConfiguration config)
{
    public DownloadBackupResult? Handle()
    {
        var backupDir = config["Backup:Path"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Beacon", "Backups");
        var backupFile = Path.Combine(backupDir, "Beacon_backup.json");

        if (!File.Exists(backupFile))
            return null;

        var stream = new FileStream(backupFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new DownloadBackupResult(stream, $"Beacon_backup_{DateTime.UtcNow:yyyy-MM-dd}.json");
    }
}
