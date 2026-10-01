namespace Beacon.Api.Services;

public interface IPdfExtractor
{
    Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken ct = default);
}
