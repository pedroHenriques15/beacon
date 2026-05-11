using FinanceHub.Api.Data;
using FinanceHub.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Statements.Queries.DownloadStatementFile;

public record FileResult(Stream Stream, string ContentType, string FileName);

public class DownloadStatementFileQueryHandler(AppDbContext db, FileStorageService fileStorage, ILogger<DownloadStatementFileQueryHandler> logger)
{
    public async Task<FileResult?> HandleAsync(DownloadStatementFileQuery query, CancellationToken ct = default)
    {
        logger.LogInformation("DownloadStatementFile: id={Id}", query.Id);
        var statement = await db.MonthlyStatements
            .Select(s => new { s.Id, s.PdfPath, s.SourceFile })
            .FirstOrDefaultAsync(s => s.Id == query.Id, ct);

        if (statement is null || statement.PdfPath is null)
            return null;

        var (stream, contentType, fileName) = fileStorage.GetFile(statement.PdfPath, statement.SourceFile);
        return new FileResult(stream, contentType, fileName);
    }
}
