namespace FinanceHub.Api.Features.Backup.Queries.DownloadBackup;

public record DownloadBackupResult(FileStream Stream, string FileName);

public class DownloadBackupQueryHandler
{
    private static readonly string BackupFilePath =
        Path.Combine(AppContext.BaseDirectory, "Backups", "FinanceHub_backup.bak");

    public DownloadBackupResult? Handle()
    {
        if (!File.Exists(BackupFilePath))
            return null;

        var stream = new FileStream(BackupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new DownloadBackupResult(stream, "FinanceHub_backup.bak");
    }
}
