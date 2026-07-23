using Beacon.Api.Features.Investments.Commands.CreateInvestmentAsset;
using Beacon.Api.Features.Investments.Commands.CreateInvestmentLot;
using Beacon.Api.Features.Investments.Commands.DeleteInvestmentAsset;
using Beacon.Api.Features.Investments.Commands.DeleteInvestmentLot;
using Beacon.Api.Features.Investments.Commands.DeleteInvestmentPriceSnapshot;
using Beacon.Api.Features.Investments.Commands.FetchInvestmentPrice;
using Beacon.Api.Features.Investments.Commands.UpdateInvestmentAsset;
using Beacon.Api.Features.Investments.Commands.UpdateInvestmentLot;
using Beacon.Api.Features.Investments.Commands.UpsertInvestmentPrice;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvestmentsController(
    GetInvestmentAssetsQueryHandler getAssets,
    CreateInvestmentAssetCommandHandler createAsset,
    UpdateInvestmentAssetCommandHandler updateAsset,
    DeleteInvestmentAssetCommandHandler deleteAsset,
    CreateInvestmentLotCommandHandler createLot,
    UpdateInvestmentLotCommandHandler updateLot,
    DeleteInvestmentLotCommandHandler deleteLot,
    UpsertInvestmentPriceCommandHandler upsertPrice,
    DeleteInvestmentPriceSnapshotCommandHandler deletePrice,
    FetchInvestmentPriceCommandHandler fetchPrice) : ControllerBase
{
    [HttpGet("assets")]
    public async Task<IActionResult> GetAssets(CancellationToken ct) =>
        Ok(await getAssets.HandleAsync(ct));

    [HttpPost("assets")]
    public async Task<IActionResult> CreateAsset([FromBody] CreateAssetRequest body, CancellationToken ct)
    {
        var (result, error) = await createAsset.HandleAsync(
            new CreateInvestmentAssetCommand(body.AssetType, body.Ticker, body.Name, body.Notes), ct);
        return error is not null ? BadRequest(error) : Created($"/api/investments/assets/{result!.Id}", result);
    }

    [HttpPut("assets/{id:int}")]
    public async Task<IActionResult> UpdateAsset(int id, [FromBody] UpdateAssetRequest body, CancellationToken ct)
    {
        var (result, error) = await updateAsset.HandleAsync(
            new UpdateInvestmentAssetCommand(id, body.Ticker, body.Name, body.Notes), ct);
        if (result is null && error is null) return NotFound();
        return error is not null ? BadRequest(error) : Ok(result);
    }

    [HttpDelete("assets/{id:int}")]
    public async Task<IActionResult> DeleteAsset(int id, CancellationToken ct)
    {
        var found = await deleteAsset.HandleAsync(new DeleteInvestmentAssetCommand(id), ct);
        return found ? NoContent() : NotFound();
    }

    [HttpPost("assets/{id:int}/fetch-price")]
    public async Task<IActionResult> FetchPrice(int id, CancellationToken ct)
    {
        var (result, error) = await fetchPrice.HandleAsync(new FetchInvestmentPriceCommand(id), ct);
        if (result is null && error is null) return NotFound();
        return error is not null ? BadRequest(error) : Ok(result);
    }

    [HttpPost("lots")]
    public async Task<IActionResult> CreateLot([FromBody] CreateLotRequest body, CancellationToken ct)
    {
        var (result, error) = await createLot.HandleAsync(
            new CreateInvestmentLotCommand(body.AssetId, body.Date, body.Quantity, body.PricePerUnit, body.Fees, body.Notes), ct);
        return error is not null ? BadRequest(error) : Created($"/api/investments/lots/{result!.Id}", result);
    }

    [HttpPut("lots/{id:int}")]
    public async Task<IActionResult> UpdateLot(int id, [FromBody] UpdateLotRequest body, CancellationToken ct)
    {
        var (result, error) = await updateLot.HandleAsync(
            new UpdateInvestmentLotCommand(id, body.Date, body.Quantity, body.PricePerUnit, body.Fees, body.Notes), ct);
        if (result is null && error is null) return NotFound();
        return error is not null ? BadRequest(error) : Ok(result);
    }

    [HttpDelete("lots/{id:int}")]
    public async Task<IActionResult> DeleteLot(int id, CancellationToken ct)
    {
        var found = await deleteLot.HandleAsync(new DeleteInvestmentLotCommand(id), ct);
        return found ? NoContent() : NotFound();
    }

    [HttpPut("prices")]
    public async Task<IActionResult> UpsertPrice([FromBody] UpsertPriceRequest body, CancellationToken ct)
    {
        var (result, error) = await upsertPrice.HandleAsync(
            new UpsertInvestmentPriceCommand(body.AssetId, body.Date, body.PricePerUnit), ct);
        return error is not null ? BadRequest(error) : Ok(result);
    }

    [HttpDelete("prices/{id:int}")]
    public async Task<IActionResult> DeletePrice(int id, CancellationToken ct)
    {
        var found = await deletePrice.HandleAsync(new DeleteInvestmentPriceSnapshotCommand(id), ct);
        return found ? NoContent() : NotFound();
    }
}

public record CreateAssetRequest(string AssetType, string? Ticker, string Name, string? Notes);
public record UpdateAssetRequest(string? Ticker, string Name, string? Notes);
public record CreateLotRequest(int AssetId, DateOnly Date, decimal Quantity, decimal PricePerUnit, decimal? Fees, string? Notes);
public record UpdateLotRequest(DateOnly Date, decimal Quantity, decimal PricePerUnit, decimal? Fees, string? Notes);
public record UpsertPriceRequest(int AssetId, DateOnly Date, decimal PricePerUnit);
