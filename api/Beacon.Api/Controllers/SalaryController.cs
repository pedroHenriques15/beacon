using Beacon.Api.Features.Salary.Commands.CreateSalaryItemCategory;
using Beacon.Api.Features.Salary.Commands.CreateSalaryProfile;
using Beacon.Api.Features.Salary.Commands.CreateSalarySlip;
using Beacon.Api.Features.Salary.Commands.DeleteSalaryItemCategory;
using Beacon.Api.Features.Salary.Commands.DeleteSalaryProfile;
using Beacon.Api.Features.Salary.Commands.DeleteSalarySlip;
using Beacon.Api.Features.Salary.Commands.ParseSalarySlip;
using Beacon.Api.Features.Salary.Commands.UpdateSalaryItemCategory;
using Beacon.Api.Features.Salary.Commands.UpdateSalaryProfile;
using Beacon.Api.Features.Salary.Commands.UpdateSalarySlip;
using Beacon.Api.Features.Salary.Queries.GetSalaryItemCategories;
using Beacon.Api.Features.Salary.Queries.GetSalaryProfiles;
using Beacon.Api.Features.Salary.Queries.GetSalarySlips;
using Beacon.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SalaryController(
    GetSalaryProfilesQueryHandler getProfiles,
    CreateSalaryProfileCommandHandler createProfile,
    UpdateSalaryProfileCommandHandler updateProfile,
    DeleteSalaryProfileCommandHandler deleteProfile,
    GetSalarySlipsQueryHandler getSlips,
    CreateSalarySlipCommandHandler createSlip,
    UpdateSalarySlipCommandHandler updateSlip,
    DeleteSalarySlipCommandHandler deleteSlip,
    GetSalaryItemCategoriesQueryHandler getItemCategories,
    CreateSalaryItemCategoryCommandHandler createItemCategory,
    UpdateSalaryItemCategoryCommandHandler updateItemCategory,
    DeleteSalaryItemCategoryCommandHandler deleteItemCategory,
    FileStorageService fileStorage,
    ParseSalarySlipCommandHandler parseSalarySlip) : ControllerBase
{

    [HttpPost("upload-pdf")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> UploadPdf(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only PDF files are supported.");

        var pdfPath  = await fileStorage.SaveAsync(file);
        var fileName = file.FileName;
        return Ok(new { pdfPath, fileName });
    }

    [HttpPost("parse-pdf")]
    public async Task<IActionResult> ParsePdf([FromBody] ParsePdfRequest body, CancellationToken ct)
    {
        var fullPath = fileStorage.GetFullPath(body.PdfPath);
        var (result, error) = await parseSalarySlip.HandleAsync(
            new ParseSalarySlipCommand(fullPath), ct);
        if (error is not null) return BadRequest(error);
        return Ok(result);
    }

    [HttpGet("profiles")]
    public async Task<IActionResult> GetProfiles(CancellationToken ct) =>
        Ok(await getProfiles.HandleAsync(ct));

    [HttpPost("profiles")]
    public async Task<IActionResult> CreateProfile([FromBody] CreateSalaryProfileRequest body, CancellationToken ct)
    {
        var result = await createProfile.HandleAsync(new CreateSalaryProfileCommand(body.Name, body.Description), ct);
        return Created($"/api/salary/profiles/{result.Id}", result);
    }

    [HttpPut("profiles/{id:int}")]
    public async Task<IActionResult> UpdateProfile(int id, [FromBody] UpdateSalaryProfileRequest body, CancellationToken ct)
    {
        var result = await updateProfile.HandleAsync(new UpdateSalaryProfileCommand(id, body.Name, body.Description), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("profiles/{id:int}")]
    public async Task<IActionResult> DeleteProfile(int id, CancellationToken ct)
    {
        var deleted = await deleteProfile.HandleAsync(new DeleteSalaryProfileCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("slips")]
    public async Task<IActionResult> GetSlips([FromQuery] int? profileId, [FromQuery] int? year, CancellationToken ct) =>
        Ok(await getSlips.HandleAsync(new GetSalarySlipsQuery(profileId, year), ct));

    [HttpPost("slips")]
    public async Task<IActionResult> CreateSlip([FromBody] CreateSalarySlipRequest body, CancellationToken ct)
    {
        var (result, error) = await createSlip.HandleAsync(
            new CreateSalarySlipCommand(
                body.SalaryProfileId, body.Period, body.GrossAmount, body.NetAmount,
                body.Notes, body.PdfPath, body.SourceFile,
                body.LineItems.Select(li => new CreateLineItemRequest(
                    li.SalaryItemCategoryId, li.Amount, li.SortOrder,
                    li.Quantity, li.UnitValue, li.Percentage, li.IncidenciaBase)).ToList(),
                body.BaseAmount, body.HoursWorked, body.HourlyRate, body.TotalEspecie), ct);

        if (error is not null) return BadRequest(error);
        return Created($"/api/salary/slips/{result!.Id}", result);
    }

    [HttpPut("slips/{id:int}")]
    public async Task<IActionResult> UpdateSlip(int id, [FromBody] UpdateSalarySlipRequest body, CancellationToken ct)
    {
        var (result, error) = await updateSlip.HandleAsync(
            new UpdateSalarySlipCommand(
                id, body.Period, body.GrossAmount, body.NetAmount,
                body.Notes, body.LineItems.Select(li => new CreateLineItemRequest(
                    li.SalaryItemCategoryId, li.Amount, li.SortOrder,
                    li.Quantity, li.UnitValue, li.Percentage, li.IncidenciaBase)).ToList(),
                body.PdfPath, body.SourceFile,
                body.BaseAmount, body.HoursWorked, body.HourlyRate, body.TotalEspecie), ct);

        if (result is null && error is null) return NotFound();
        if (error is not null) return BadRequest(error);
        return Ok(result);
    }

    [HttpDelete("slips/{id:int}")]
    public async Task<IActionResult> DeleteSlip(int id, CancellationToken ct)
    {
        var deleted = await deleteSlip.HandleAsync(new DeleteSalarySlipCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("item-categories")]
    public async Task<IActionResult> GetItemCategories([FromQuery] int profileId, CancellationToken ct) =>
        Ok(await getItemCategories.HandleAsync(new GetSalaryItemCategoriesQuery(profileId), ct));

    [HttpPost("item-categories")]
    public async Task<IActionResult> CreateItemCategory([FromBody] CreateSalaryItemCategoryRequest body, CancellationToken ct)
    {
        var (result, error) = await createItemCategory.HandleAsync(
            new CreateSalaryItemCategoryCommand(body.SalaryProfileId, body.Name, body.Color, body.ItemType), ct);
        if (error is not null) return BadRequest(error);
        return Created($"/api/salary/item-categories/{result!.Id}", result);
    }

    [HttpPut("item-categories/{id:int}")]
    public async Task<IActionResult> UpdateItemCategory(int id, [FromBody] UpdateSalaryItemCategoryRequest body, CancellationToken ct)
    {
        var result = await updateItemCategory.HandleAsync(
            new UpdateSalaryItemCategoryCommand(id, body.Name, body.Color, body.ItemType), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("item-categories/{id:int}")]
    public async Task<IActionResult> DeleteItemCategory(int id, CancellationToken ct)
    {
        var (found, isProtected) = await deleteItemCategory.HandleAsync(new DeleteSalaryItemCategoryCommand(id), ct);
        if (!found) return NotFound();
        if (isProtected) return Conflict("This is a system category and cannot be deleted.");
        return NoContent();
    }
}

public record ParsePdfRequest(string PdfPath);
public record CreateSalaryProfileRequest(string Name, string? Description);
public record UpdateSalaryProfileRequest(string Name, string? Description);

public record LineItemRequest(
    int SalaryItemCategoryId,
    decimal Amount,
    int SortOrder,
    decimal? Quantity = null,
    decimal? UnitValue = null,
    decimal? Percentage = null,
    decimal? IncidenciaBase = null);

public record CreateSalarySlipRequest(
    int SalaryProfileId,
    DateOnly Period,
    decimal GrossAmount,
    decimal NetAmount,
    string? Notes,
    string? PdfPath,
    string? SourceFile,
    List<LineItemRequest> LineItems,
    decimal? BaseAmount = null,
    decimal? HoursWorked = null,
    decimal? HourlyRate = null,
    decimal? TotalEspecie = null);

public record UpdateSalarySlipRequest(
    DateOnly Period,
    decimal GrossAmount,
    decimal NetAmount,
    string? Notes,
    List<LineItemRequest> LineItems,
    string? PdfPath = null,
    string? SourceFile = null,
    decimal? BaseAmount = null,
    decimal? HoursWorked = null,
    decimal? HourlyRate = null,
    decimal? TotalEspecie = null);

public record CreateSalaryItemCategoryRequest(int SalaryProfileId, string Name, string Color, string ItemType);
public record UpdateSalaryItemCategoryRequest(string Name, string Color, string ItemType);
