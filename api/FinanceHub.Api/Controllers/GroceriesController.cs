using FinanceHub.Api.Features.Groceries.Commands.CreateGroceryItem;
using FinanceHub.Api.Features.Groceries.Commands.DeleteGroceryItem;
using FinanceHub.Api.Features.Groceries.Commands.DeleteGroceryReceipt;
using FinanceHub.Api.Features.Groceries.Commands.SetGroceryItemCategory;
using FinanceHub.Api.Features.Groceries.Commands.UpdateGroceryItem;
using FinanceHub.Api.Features.Groceries.Commands.UploadGroceryReceipt;
using FinanceHub.Api.Features.Groceries.Queries.GetGroceryItems;
using FinanceHub.Api.Features.Groceries.Queries.GetGroceryReceipts;
using Microsoft.AspNetCore.Mvc;

namespace FinanceHub.Api.Controllers;

[ApiController]
[Route("api/groceries")]
public class GroceriesController(
    GetGroceryReceiptsQueryHandler getReceipts,
    UploadGroceryReceiptCommandHandler uploadReceipt,
    DeleteGroceryReceiptCommandHandler deleteReceipt,
    GetGroceryItemsQueryHandler getItems,
    CreateGroceryItemCommandHandler createItem,
    UpdateGroceryItemCommandHandler updateItem,
    DeleteGroceryItemCommandHandler deleteItem,
    SetGroceryItemCategoryCommandHandler setCategory) : ControllerBase
{
    [HttpGet("receipts")]
    public async Task<IActionResult> GetReceipts([FromQuery] string? store, CancellationToken ct) =>
        Ok(await getReceipts.HandleAsync(store, ct));

    [HttpPost("receipts/upload")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> UploadReceipt(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");
        try
        {
            var result = await uploadReceipt.HandleAsync(new UploadGroceryReceiptCommand(file), ct);
            return result.WasDuplicate ? Conflict(result) : Ok(result);
        }
        catch (NotSupportedException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("receipts/{id:int}")]
    public async Task<IActionResult> DeleteReceipt(int id, CancellationToken ct)
    {
        var deleted = await deleteReceipt.HandleAsync(new DeleteGroceryReceiptCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("items")]
    public async Task<IActionResult> GetItems(
        [FromQuery] int? receiptId, [FromQuery] string? store, [FromQuery] string? month,
        [FromQuery] int? categoryId, [FromQuery] string? search,
        [FromQuery] int skip = 0, [FromQuery] int take = 20,
        [FromQuery] string? sortCol = null, [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        var query = new GetGroceryItemsQuery(receiptId, store, month, categoryId, search, sortCol, sortDir, skip, take);
        return Ok(await getItems.HandleAsync(query, ct));
    }

    [HttpPost("items")]
    public async Task<IActionResult> CreateItem([FromBody] CreateGroceryItemRequest body, CancellationToken ct)
    {
        var cmd = new CreateGroceryItemCommand(body.ReceiptId, body.Description, body.Amount, body.Quantity);
        var (result, error) = await createItem.HandleAsync(cmd, ct);
        return error is not null ? BadRequest(error) : Created($"/api/groceries/items/{result!.Id}", result);
    }

    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> UpdateItem(int id, [FromBody] UpdateGroceryItemRequest body, CancellationToken ct)
    {
        var cmd = new UpdateGroceryItemCommand(id, body.Description, body.Amount, body.Quantity);
        var result = await updateItem.HandleAsync(cmd, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> DeleteItem(int id, CancellationToken ct)
    {
        var deleted = await deleteItem.HandleAsync(new DeleteGroceryItemCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPatch("items/{id:int}/category")]
    public async Task<IActionResult> SetItemCategory(int id, [FromBody] SetGroceryItemCategoryRequest body, CancellationToken ct)
    {
        var result = await setCategory.HandleAsync(
            new SetGroceryItemCategoryCommand(id, body.CategoryId, body.DeleteRuleId), ct);
        return result is null ? NotFound() : Ok(result);
    }
}

public record CreateGroceryItemRequest(int ReceiptId, string Description, decimal Amount, decimal Quantity);
public record UpdateGroceryItemRequest(string? Description, decimal? Amount, decimal? Quantity);
public record SetGroceryItemCategoryRequest(int? CategoryId, int? DeleteRuleId);
