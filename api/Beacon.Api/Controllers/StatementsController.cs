using Beacon.Api.Features.Statements.Commands.DeleteStatement;
using Beacon.Api.Features.Statements.Commands.ImportMealCardText;
using Beacon.Api.Features.Statements.Commands.UploadStatement;
using Beacon.Api.Features.Statements.Queries.DownloadStatementFile;
using Beacon.Api.Features.Statements.Queries.GetStatementById;
using Beacon.Api.Features.Statements.Queries.GetStatements;
using Beacon.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

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
