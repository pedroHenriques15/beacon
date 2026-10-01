using Beacon.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Services;

/// <summary>
/// Runs at startup: deletes every PDF in the storage root that no statement, salary slip or grocery
/// receipt references and that is older than <see cref="MinimumAge"/> (a younger one may belong to
/// an upload whose row is not saved yet). A row protects the file its <c>PdfPath</c> ends in, so a
/// relative path, an absolute path under the root and an absolute path written on another machine
/// (a restored backup) all keep their file. When the database references none of the PDFs in the
/// folder, the two do not belong together (the demo database pointed at real uploads, say), so
/// nothing is deleted.
/// </summary>
public class OrphanedPdfCleanup(AppDbContext db, IConfiguration config, ILogger<OrphanedPdfCleanup> logger)
{
    public static readonly TimeSpan MinimumAge = TimeSpan.FromHours(24);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var storagePath = config["Storage:Path"];
        if (string.IsNullOrEmpty(storagePath) || !Directory.Exists(storagePath)) return 0;

        var storedPaths = new List<string>();
        storedPaths.AddRange(await db.MonthlyStatements
            .Where(s => s.PdfPath != null).Select(s => s.PdfPath!).ToListAsync(ct));
        storedPaths.AddRange(await db.SalarySlips
            .Where(s => s.PdfPath != null).Select(s => s.PdfPath!).ToListAsync(ct));
        storedPaths.AddRange(await db.GroceryReceipts
            .Where(r => r.PdfPath != null).Select(r => r.PdfPath!).ToListAsync(ct));
        var referenced = storedPaths
            .Select(p => FileStorageService.FileNameOf(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(storagePath, "*.pdf").ToList();
        if (files.Count > 0 && !files.Any(f => referenced.Contains(Path.GetFileName(f))))
        {
            logger.LogWarning(
                "Skipped the orphaned PDF cleanup: the database references none of the {Count} PDFs in {Path}, so they do not belong together",
                files.Count, storagePath);
            return 0;
        }

        var cutoff = DateTime.UtcNow - MinimumAge;
        var deleted = 0;
        foreach (var file in files)
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
