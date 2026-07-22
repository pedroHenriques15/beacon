using Beacon.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Services;

public class FileStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileStorageService _service;

    public FileStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"fh_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = _tempRoot })
            .Build();

        _service = new FileStorageService(config, NullLogger<FileStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_WritesFileToDisk()
    {
        var content  = "fake pdf bytes"u8.ToArray();
        var formFile = new FormFileStub(content, "statement.pdf");

        var relativePath = await _service.SaveAsync(formFile);

        var fullPath = Path.Combine(_tempRoot, relativePath);
        Assert.True(File.Exists(fullPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(fullPath));
    }

    [Fact]
    public async Task SaveAsync_ReturnsGuidBasedRelativePath()
    {
        var formFile = new FormFileStub("data"u8.ToArray(), "any.pdf");
        var path     = await _service.SaveAsync(formFile);

        var name = Path.GetFileNameWithoutExtension(path);
        Assert.True(Guid.TryParse(name, out _));
        Assert.Equal(".pdf", Path.GetExtension(path));
    }

    [Fact]
    public async Task SaveAsync_EachCallProducesUniqueFileName()
    {
        var formFile = new FormFileStub("data"u8.ToArray(), "any.pdf");
        var path1 = await _service.SaveAsync(formFile);
        var path2 = await _service.SaveAsync(formFile);

        Assert.NotEqual(path1, path2);
    }

    [Fact]
    public async Task GetFile_ReturnsStreamAndMetadata()
    {
        var content  = "pdf content"u8.ToArray();
        var formFile = new FormFileStub(content, "original.pdf");
        var relative = await _service.SaveAsync(formFile);

        var (stream, contentType, fileName) = _service.GetFile(relative, "original.pdf");

        using (stream)
        {
            var bytes = new byte[stream.Length];
            _ = await stream.ReadAsync(bytes);
            Assert.Equal(content, bytes);
        }

        Assert.Equal("application/pdf", contentType);
        Assert.Equal("original.pdf", fileName);
    }

    [Fact]
    public async Task GetFile_NonExistentFile_ThrowsFileNotFound()
    {
        await Task.CompletedTask;
        Assert.Throws<FileNotFoundException>(() =>
            _service.GetFile("nonexistent.pdf", "nonexistent.pdf"));
    }

    [Fact]
    public void GetFullPath_AbsolutePathOutsideStorage_ThrowsUnauthorized()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            _service.GetFullPath("/etc/passwd"));
    }

    [Fact]
    public void GetFullPath_RelativeTraversal_ThrowsUnauthorized()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            _service.GetFullPath(Path.Combine("..", "outside.pdf")));
    }

    [Fact]
    public void GetFullPath_PathInsideStorage_Resolves()
    {
        var result = _service.GetFullPath("some.pdf");
        Assert.StartsWith(Path.GetFullPath(_tempRoot), result);
    }

    [Fact]
    public async Task GetFile_PathTraversal_ThrowsUnauthorized()
    {
        var parentDir  = Path.GetDirectoryName(_tempRoot)!;
        var outsideFile = Path.Combine(parentDir, $"outside_{Guid.NewGuid()}.pdf");
        try
        {
            await File.WriteAllBytesAsync(outsideFile, "data"u8.ToArray());

            var traversal = Path.Combine("..", Path.GetFileName(outsideFile));

            Assert.Throws<UnauthorizedAccessException>(() =>
                _service.GetFile(traversal, "outside.pdf"));
        }
        finally
        {
            if (File.Exists(outsideFile)) File.Delete(outsideFile);
        }
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var formFile = new FormFileStub("data"u8.ToArray(), "del.pdf");
        var relative = await _service.SaveAsync(formFile);
        var fullPath = Path.Combine(_tempRoot, relative);

        Assert.True(File.Exists(fullPath));

        _service.Delete(relative);

        Assert.False(File.Exists(fullPath));
    }

    [Fact]
    public void Delete_NonExistentFile_DoesNotThrow()
    {
        var ex = Record.Exception(() => _service.Delete("ghost.pdf"));
        Assert.Null(ex);
    }

    private sealed class FormFileStub(byte[] content, string fileName) : IFormFile
    {
        public string ContentType        => "application/pdf";
        public string ContentDisposition => string.Empty;
        public IHeaderDictionary Headers => new HeaderDictionary();
        public long Length               => content.Length;
        public string Name               => "file";
        public string FileName           => fileName;

        public void CopyTo(Stream target)                    => new MemoryStream(content).CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken ct = default)
            => new MemoryStream(content).CopyToAsync(target, ct);
        public Stream OpenReadStream()                       => new MemoryStream(content);
    }
}
