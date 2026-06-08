namespace Beacon.Api.Features.Backup.Queries.DownloadBackup;

public record DownloadBackupResult(FileStream Stream, string FileName);

public class DownloadBackupQueryHandler
{
    private static readonly string BackupFilePath =
        Path.Combine(AppContext.BaseDirectory, "Backups", "Beacon_backup.bak");

    public DownloadBackupResult? Handle()
    {
        if (!File.Exists(BackupFilePath))
            return null;

        var stream = new FileStream(BackupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new DownloadBackupResult(stream, "Beacon_backup.bak");
    }
}
