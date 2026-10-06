using System.Diagnostics.CodeAnalysis;

namespace Beacon.Api.Services;

public class FileStorageService(IConfiguration config, ILogger<FileStorageService> logger)
{
    private static readonly char[] PathSeparators = ['/', '\\'];

    // The kinds of file the uploads store, and the content type each is served with.
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".csv"] = "text/csv",
    };

    /// <summary>Whether a path names a kind of file the uploads store: a PDF, or a CSV export (ADR-031).</summary>
    public static bool IsStoredFile(string path) => ContentTypes.ContainsKey(Path.GetExtension(path));

    private string StorageRoot => string.IsNullOrEmpty(config["Storage:Path"])
        ? Path.Combine(AppContext.BaseDirectory, "statements")
        : config["Storage:Path"]!;

    /// <summary>
    /// The file name a stored <c>PdfPath</c> ends in, whichever machine wrote it. Every stored file is
    /// <c>&lt;guid&gt;.pdf</c> or <c>&lt;guid&gt;.csv</c> in the storage root, so the name alone identifies
    /// it. Splits on both separators: on Linux, <see cref="Path.GetFileName(string)"/> leaves a Windows
    /// path whole.
    /// </summary>
    [return: NotNullIfNotNull(nameof(storedPath))]
    public static string? FileNameOf(string? storedPath) =>
        storedPath?[(storedPath.LastIndexOfAny(PathSeparators) + 1)..];

    /// <summary>
    /// Writes the file to the storage root and returns its name, which is what <c>PdfPath</c>
    /// stores. Resolve it with <see cref="GetFullPath"/> before opening the file. The name keeps the
    /// upload's extension when <see cref="IsStoredFile"/> knows it, and is <c>.pdf</c> otherwise.
    /// </summary>
    public async Task<string> SaveAsync(IFormFile file)
    {
        Directory.CreateDirectory(StorageRoot);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{(ContentTypes.ContainsKey(extension) ? extension : ".pdf")}";
        var fullPath = Path.Combine(StorageRoot, fileName);
        await using var fs = File.Create(fullPath);
        await file.CopyToAsync(fs);
        logger.LogInformation("Saved the upload to {Path}", fullPath);
        return fileName;
    }

    public string GetFullPath(string path)
    {
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(StorageRoot, path);
        var resolvedPath = Path.GetFullPath(fullPath);
        var resolvedRoot = Path.GetFullPath(StorageRoot);
        if (!resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal attempt detected.");
        return resolvedPath;
    }

    public (Stream Stream, string ContentType, string FileName) GetFile(string path, string originalFileName)
    {
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(StorageRoot, path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Statement file not found.", fullPath);

        var resolvedPath = Path.GetFullPath(fullPath);
        var resolvedRoot = Path.GetFullPath(StorageRoot);
        if (!resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal attempt detected.");

        var contentType = ContentTypes.GetValueOrDefault(Path.GetExtension(fullPath), "application/pdf");
        return (File.OpenRead(fullPath), contentType, originalFileName);
    }

    public void Delete(string path)
    {
        string fullPath;
        try { fullPath = GetFullPath(path); }
        catch (UnauthorizedAccessException)
        {
            logger.LogWarning("Refused to delete path outside storage root: {Path}", path);
            return;
        }
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            logger.LogInformation("Deleted the stored file at {Path}", fullPath);
        }
        else
        {
            logger.LogWarning("Stored file not found for deletion at {Path}", fullPath);
        }
    }
}
