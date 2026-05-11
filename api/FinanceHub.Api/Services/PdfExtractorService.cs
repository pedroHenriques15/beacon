using System.Diagnostics;
using System.Text.Json;

namespace FinanceHub.Api.Services;

public class PdfExtractorService(IConfiguration config, ILogger<PdfExtractorService> logger)
{
    public async Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath)
    {
        var python = config["Python:Executable"] ?? "python";
        var script = config["Python:ExtractorScript"]
            ?? Path.Combine(AppContext.BaseDirectory, "../../../../Scripts/pdfExtractor.py");

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
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"PDF extraction failed: {stderr}");

        return JsonSerializer.Deserialize<List<string>>(stdout)
            ?? throw new InvalidOperationException("Extractor returned null.");
    }
}
