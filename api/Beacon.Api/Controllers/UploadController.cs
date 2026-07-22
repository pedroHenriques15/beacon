using System.IO.Compression;
using Beacon.Api.Features.Upload.Commands.UnifiedUploadBatch;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/upload")]
public class UploadController(UnifiedUploadBatchCommandHandler handler) : ControllerBase
{
    private const long MaxEntryDecompressedBytes = 50L * 1024 * 1024;
    private const long MaxTotalDecompressedBytes = 500L * 1024 * 1024;

    [HttpPost("batch")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> UploadBatch([FromForm] IFormFileCollection files, CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            return BadRequest("No files provided.");

        var toProcess = new List<(string FileName, MemoryStream Content)>();
        var errors    = new List<UnifiedUploadItemResult>();
        long totalDecompressed = 0;
        try
        {
            foreach (var file in files)
            {
                if (file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = new ZipArchive(file.OpenReadStream(), ZipArchiveMode.Read);
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;

                        if (totalDecompressed >= MaxTotalDecompressedBytes)
                            return BadRequest("The archive expands beyond the allowed total size.");

                        var ms = new MemoryStream();
                        using var es = entry.Open();
                        var ok = await CopyBoundedAsync(es, ms, MaxEntryDecompressedBytes, ct);
                        totalDecompressed += ms.Length;
                        if (!ok)
                        {
                            errors.Add(new UnifiedUploadItemResult(
                                entry.Name, "Unknown", false, false,
                                "This file expands beyond the allowed size and was skipped.", null, null, null));
                            await ms.DisposeAsync();
                            continue;
                        }
                        ms.Seek(0, SeekOrigin.Begin);
                        toProcess.Add((entry.Name, ms));
                    }
                }
                else if (file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var ms = new MemoryStream();
                    await file.CopyToAsync(ms, ct);
                    ms.Seek(0, SeekOrigin.Begin);
                    toProcess.Add((file.FileName, ms));
                }
                else
                {
                    errors.Add(new UnifiedUploadItemResult(
                        file.FileName, "Unknown", false, false,
                        "Only PDF files and ZIP archives are supported.", null, null, null));
                }
            }

            if (toProcess.Count == 0 && errors.Count == 0)
                return BadRequest("No PDF files found in the provided input.");

            var results = toProcess.Count > 0
                ? await handler.HandleAsync(toProcess, ct)
                : [];

            results.AddRange(errors);
            return Ok(results);
        }
        finally
        {
            foreach (var (_, ms) in toProcess) ms.Dispose();
        }
    }

    private static async Task<bool> CopyBoundedAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes) return false;
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return true;
    }
}

