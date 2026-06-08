namespace Beacon.Api.Services;

public class FileStorageService(IConfiguration config, ILogger<FileStorageService> logger)
{
    private string StorageRoot => string.IsNullOrEmpty(config["Storage:Path"])
        ? Path.Combine(AppContext.BaseDirectory, "statements")
        : config["Storage:Path"]!;

    public async Task<string> SaveAsync(IFormFile file)
    {
        Directory.CreateDirectory(StorageRoot);
        var fullPath = Path.Combine(StorageRoot, $"{Guid.NewGuid()}.pdf");
        await using var fs = File.Create(fullPath);
        await file.CopyToAsync(fs);
        logger.LogInformation("Saved PDF to {Path}", fullPath);
        return fullPath;
    }

    public string GetFullPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(StorageRoot, path);

    public (Stream Stream, string ContentType, string FileName) GetFile(string path, string originalFileName)
    {
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(StorageRoot, path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Statement file not found.", fullPath);

        var resolvedPath = Path.GetFullPath(fullPath);
        var resolvedRoot = Path.GetFullPath(StorageRoot);
        if (!resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal attempt detected.");

        return (File.OpenRead(fullPath), "application/pdf", originalFileName);
    }

    public void Delete(string path)
    {
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(StorageRoot, path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            logger.LogInformation("Deleted PDF at {Path}", fullPath);
        }
        else
        {
            logger.LogWarning("PDF not found for deletion at {Path}", fullPath);
        }
    }
}
