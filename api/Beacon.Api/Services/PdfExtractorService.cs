using System.Diagnostics;
using System.Text.Json;

namespace Beacon.Api.Services;

public class PdfExtractorService(IConfiguration config, ILogger<PdfExtractorService> logger) : IPdfExtractor
{
    public async Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken ct = default)
    {
        var python = config["Python:Executable"] ?? "python";
        var script = config["Python:ExtractorScript"]
            ?? throw new InvalidOperationException(
                "Python:ExtractorScript is not configured - set it to the absolute path of scripts/pdfExtractor.py.");

        var timeoutSeconds = config.GetValue("Python:TimeoutSeconds", 60);

        logger.LogInformation("Extracting text from {Path}", pdfPath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = python,
                Arguments              = $"\"{script}\" \"{pdfPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            }
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }

            try { await Task.WhenAll(stdoutTask, stderrTask); }
            catch { }

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException("PDF extraction was cancelled.", ct);

            logger.LogError("PDF extraction timed out after {Seconds}s for {Path}", timeoutSeconds, pdfPath);
            throw new InvalidOperationException(
                $"PDF extraction timed out after {timeoutSeconds} seconds - the file may be malformed.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode == 2)
            throw new NotSupportedException(
                "This PDF is password-protected - remove the password and upload it again.");
        if (process.ExitCode == 3)
            throw new NotSupportedException(
                "This PDF could not be read - it may be corrupt or not a valid PDF.");
        if (process.ExitCode != 0)
        {
            logger.LogError("PDF extraction failed (exit {Code}) for {Path}: {Stderr}",
                process.ExitCode, pdfPath, stderr);
            throw new InvalidOperationException($"PDF extraction failed: {LastLine(stderr)}");
        }

        var pages = JsonSerializer.Deserialize<List<string>>(stdout)
            ?? throw new InvalidOperationException("Extractor returned null.");

        if (pages.Count == 0)
            throw new NotSupportedException(
                "No text could be extracted from this PDF - it looks like a scanned image. " +
                "Only digitally-generated documents with a text layer are supported.");

        return pages;
    }

    private static string LastLine(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[^1] : "unknown error";
    }
}
