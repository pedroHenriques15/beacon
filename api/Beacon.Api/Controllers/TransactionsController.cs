using Beacon.Api.Features.Transactions.Commands.BulkDeleteTransactions;
using Beacon.Api.Features.Transactions.Commands.CreateTransaction;
using Beacon.Api.Features.Transactions.Commands.DeleteTransaction;
using Beacon.Api.Features.Transactions.Commands.MarkTransfers;
using Beacon.Api.Features.Transactions.Commands.SetTransactionCategory;
using Beacon.Api.Features.Transactions.Commands.UpdateTransaction;
using Beacon.Api.Features.Transactions.Queries.GetTransactions;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransactionsController(
    GetTransactionsQueryHandler getTransactions,
    SetTransactionCategoryCommandHandler setCategory,
    MarkTransfersCommandHandler markTransfers,
    CreateTransactionCommandHandler createTransaction,
    UpdateTransactionCommandHandler updateTransaction,
    DeleteTransactionCommandHandler deleteTransaction,
    BulkDeleteTransactionsCommandHandler bulkDeleteTransactions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? bank, [FromQuery] string? month, [FromQuery] string? type,
        [FromQuery] string? category, [FromQuery] string? search,
        [FromQuery] int skip = 0, [FromQuery] int take = 20,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        var query = new GetTransactionsQuery(bank, month, type, category, search, skip, take, sortBy, sortDir);
        var validation = new GetTransactionsQueryValidator().Validate(query);
        if (!validation.IsValid) return BadRequest(new { errors = validation.Errors });
        return Ok(await getTransactions.HandleAsync(query, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionRequest body, CancellationToken ct)
    {
        var cmd = new CreateTransactionCommand(
            body.StatementId, body.DatePosting, body.DateValue,
            body.Description, body.Amount, body.Type, body.Balance, body.CategoryId);
        var validation = new CreateTransactionCommandValidator().Validate(cmd);
        if (!validation.IsValid) return BadRequest(new { errors = validation.Errors });

        var (result, error) = await createTransaction.HandleAsync(cmd, ct);
        return error is not null ? BadRequest(error) : Created($"/api/transactions/{result!.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTransactionRequest body, CancellationToken ct)
    {
        var cmd = new UpdateTransactionCommand(
            id, body.DatePosting, body.DateValue, body.Description,
            body.Amount, body.Type, body.Balance, body.CategoryId,
            body.CategorySetManually, body.UnlinkTransfer, body.UnlinkCategory);
        var validation = new UpdateTransactionCommandValidator().Validate(cmd);
        if (!validation.IsValid) return BadRequest(new { errors = validation.Errors });

        var result = await updateTransaction.HandleAsync(cmd, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await deleteTransaction.HandleAsync(new DeleteTransactionCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteBulk([FromBody] BulkDeleteRequest body, CancellationToken ct)
    {
        if (body.Ids is null || body.Ids.Length == 0)
            return BadRequest(new { errors = new[] { "Ids must not be empty." } });
        await bulkDeleteTransactions.HandleAsync(new BulkDeleteTransactionsCommand(body.Ids), ct);
        return NoContent();
    }

    [HttpPatch("{id:int}/category")]
    public async Task<IActionResult> SetCategory(int id, [FromBody] SetCategoryRequest body, CancellationToken ct)
    {
        var result = await setCategory.HandleAsync(
            new SetTransactionCategoryCommand(id, body.CategoryId, body.DeleteRuleId), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("mark-transfers")]
    public async Task<IActionResult> MarkAsTransfer([FromBody] MarkTransfersRequest body, CancellationToken ct)
    {
        var cmd = new MarkTransfersCommand(body.TxIds, body.Unmark);
        var validation = new MarkTransfersCommandValidator().Validate(cmd);
        if (!validation.IsValid) return BadRequest(new { errors = validation.Errors });

        await markTransfers.HandleAsync(cmd, ct);
        return NoContent();
    }
}

public record CreateTransactionRequest(
    int StatementId, DateOnly DatePosting, DateOnly DateValue,
    string Description, decimal Amount, string Type, decimal Balance, int? CategoryId);

public record UpdateTransactionRequest(
    DateOnly? DatePosting, DateOnly? DateValue, string? Description,
    decimal? Amount, string? Type, decimal? Balance,
    int? CategoryId, bool? CategorySetManually,
    bool UnlinkTransfer = false, bool UnlinkCategory = false);

public record SetCategoryRequest(int? CategoryId, int? DeleteRuleId);
public record MarkTransfersRequest(int[] TxIds, bool Unmark = false);
public record BulkDeleteRequest(int[] Ids);
