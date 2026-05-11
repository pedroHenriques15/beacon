namespace FinanceHub.Api.Services;

public class FileStorageService(IConfiguration config, ILogger<FileStorageService> logger)
{
    private string StorageRoot => string.IsNullOrEmpty(config["Storage:Path"])
        ? Path.Combine(AppContext.BaseDirectory, "statements")
        : config["Storage:Path"]!;

    public async Task<string> SaveAsync(IFormFile file)
    {
        Directory.CreateDirectory(StorageRoot);
        var relativePath = $"{Guid.NewGuid()}.pdf";
        var fullPath = Path.Combine(StorageRoot, relativePath);
        await using var fs = File.Create(fullPath);
        await file.CopyToAsync(fs);
        logger.LogInformation("Saved PDF to {Path}", fullPath);
        return relativePath;
    }

    public string GetFullPath(string relativePath) => Path.Combine(StorageRoot, relativePath);

    public (Stream Stream, string ContentType, string FileName) GetFile(string relativePath, string originalFileName)
    {
        var fullPath = Path.Combine(StorageRoot, relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Statement file not found.", fullPath);

        var resolvedPath = Path.GetFullPath(fullPath);
        var resolvedRoot = Path.GetFullPath(StorageRoot);
        if (!resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal attempt detected.");

        return (File.OpenRead(fullPath), "application/pdf", originalFileName);
    }

    public void Delete(string relativePath)
    {
        var fullPath = Path.Combine(StorageRoot, relativePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
    }
}
