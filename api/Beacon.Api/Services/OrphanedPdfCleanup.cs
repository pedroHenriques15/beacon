using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Services;

/// <summary>
/// Runs at startup: deletes every PDF in the storage root that no statement, salary slip or grocery
/// receipt references and that is older than <see cref="MinimumAge"/> (a younger one may belong to
/// an upload whose row is not saved yet). A row protects the file its <c>PdfPath</c> ends in, so a
/// relative path, an absolute path under the root and an absolute path written on another machine
/// (a restored backup) all keep their file.
/// </summary>
public class OrphanedPdfCleanup(AppDbContext db, IConfiguration config, ILogger<OrphanedPdfCleanup> logger)
{
    public static readonly TimeSpan MinimumAge = TimeSpan.FromHours(24);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var storagePath = config["Storage:Path"];
        if (string.IsNullOrEmpty(storagePath) || !Directory.Exists(storagePath)) return 0;

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        referenced.UnionWith((await db.MonthlyStatements
            .Where(s => s.PdfPath != null).Select(s => s.PdfPath!).ToListAsync(ct)).Select(FileStorageService.FileNameOf));
        referenced.UnionWith((await db.SalarySlips
            .Where(s => s.PdfPath != null).Select(s => s.PdfPath!).ToListAsync(ct)).Select(FileStorageService.FileNameOf));
        referenced.UnionWith((await db.GroceryReceipts
            .Where(r => r.PdfPath != null).Select(r => r.PdfPath!).ToListAsync(ct)).Select(FileStorageService.FileNameOf));

        var cutoff = DateTime.UtcNow - MinimumAge;
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(storagePath, "*.pdf"))
        {
            if (referenced.Contains(Path.GetFileName(file))) continue;
            if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete orphaned PDF {Path}", file);
            }
        }

        if (deleted > 0)
            logger.LogInformation("Deleted {Count} orphaned PDFs from storage", deleted);
        return deleted;
    }
}
