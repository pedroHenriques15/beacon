using System.IO.Compression;
using FinanceHub.Api.Features.Statements.Commands.DeleteStatement;
using FinanceHub.Api.Features.Statements.Commands.ImportMealCardText;
using FinanceHub.Api.Features.Statements.Commands.UploadStatement;
using FinanceHub.Api.Features.Statements.Queries.DownloadStatementFile;
using FinanceHub.Api.Features.Statements.Queries.GetStatementById;
using FinanceHub.Api.Features.Statements.Queries.GetStatements;
using FinanceHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FinanceHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatementsController(
    GetStatementsQueryHandler getStatements,
    GetStatementByIdQueryHandler getStatementById,
    DownloadStatementFileQueryHandler downloadFile,
    UploadStatementCommandHandler uploadStatement,
    ImportMealCardTextCommandHandler importMealCardText,
    DeleteStatementCommandHandler deleteStatement) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? bank, CancellationToken ct) =>
        Ok(await getStatements.HandleAsync(new GetStatementsQuery(bank), ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await getStatementById.HandleAsync(new GetStatementByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> DownloadFile(int id, CancellationToken ct)
    {
        try
        {
            var result = await downloadFile.HandleAsync(new DownloadStatementFileQuery(id), ct);
            if (result is null) return NotFound();
            return File(result.Stream, result.ContentType, result.FileName);
        }
        catch (FileNotFoundException) { return NotFound("File has been removed from storage."); }
    }

    [HttpPost("upload")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");
        try
        {
            var result = await uploadStatement.HandleAsync(new UploadStatementCommand(file), ct);
            return result.Imported ? Ok(result) : Conflict(result);
        }
        catch (NotSupportedException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("upload-batch")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> UploadBatch([FromForm] IFormFileCollection files, CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            return BadRequest("No files provided.");

        var toProcess = new List<(string FileName, MemoryStream Content)>();
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
                        var ms = new MemoryStream();
                        using var es = entry.Open();
                        await es.CopyToAsync(ms, ct);
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
            }

            if (toProcess.Count == 0)
                return BadRequest("No PDF files found in the provided input.");

            var results = new List<BatchUploadItemResult>();
            foreach (var (fileName, content) in toProcess)
            {
                try
                {
                    var formFile = new FormFile(content, 0, content.Length, "file", fileName)
                    {
                        Headers = new HeaderDictionary(),
                        ContentType = "application/pdf"
                    };
                    var result = await uploadStatement.HandleAsync(new UploadStatementCommand(formFile), ct);
                    results.Add(new BatchUploadItemResult(fileName, result.Imported, (UploadResult?)result, null));
                }
                catch (NotSupportedException ex)
                {
                    results.Add(new BatchUploadItemResult(fileName, false, null, ex.Message));
                }
                catch (Exception ex)
                {
                    results.Add(new BatchUploadItemResult(fileName, false, null, ex.Message));
                }
            }

            return Ok(results);
        }
        finally
        {
            foreach (var (_, ms) in toProcess) ms.Dispose();
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await deleteStatement.HandleAsync(new DeleteStatementCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("import-text")]
    public async Task<IActionResult> ImportText(
        [FromBody] ImportMealCardTextRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawText))
            return BadRequest("No text provided.");
        try
        {
            var result = await importMealCardText.HandleAsync(
                new ImportMealCardTextCommand(request.RawText, request.PeriodFrom, request.PeriodTo, request.ClosingBalance), ct);
            return result.Imported ? Ok(result) : Conflict(result);
        }
        catch (NotSupportedException ex) { return BadRequest(ex.Message); }
    }
}

public record ImportMealCardTextRequest(
    string RawText,
    DateOnly? PeriodFrom = null,
    DateOnly? PeriodTo = null,
    decimal? ClosingBalance = null);

public record BatchUploadItemResult(
    string FileName,
    bool Success,
    UploadResult? Result,
    string? Error);
