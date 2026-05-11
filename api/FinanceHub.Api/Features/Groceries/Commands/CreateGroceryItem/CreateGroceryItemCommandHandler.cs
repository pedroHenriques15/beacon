using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Shared;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Groceries.Commands.CreateGroceryItem;

public class CreateGroceryItemCommandHandler(AppDbContext db, ILogger<CreateGroceryItemCommandHandler> logger)
{
    public async Task<(CreateGroceryItemResponse? result, string? error)> HandleAsync(
        CreateGroceryItemCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("CreateGroceryItem: receiptId={ReceiptId} description={Description} amount={Amount}",
            cmd.ReceiptId, cmd.Description, cmd.Amount);

        new CreateGroceryItemCommandValidator().Validate(cmd).ThrowIfInvalid();

        if (!await db.GroceryReceipts.AnyAsync(r => r.Id == cmd.ReceiptId, ct))
            return (null, "Receipt not found.");

        var rules = await db.GroceryCategoryRules.ToListAsync(ct);
        var matchedRule = rules
            .OrderBy(r => r.Id)
            .FirstOrDefault(r =>
                (!string.IsNullOrEmpty(r.Pattern) && cmd.Description.Contains(r.Pattern, StringComparison.Ordinal)) ||
                (r.Value.HasValue && cmd.Amount == r.Value.Value));

        var item = new GroceryItem
        {
            ReceiptId            = cmd.ReceiptId,
            Description          = cmd.Description.Trim(),
            Amount               = cmd.Amount,
            Quantity             = cmd.Quantity,
            CategoryId           = matchedRule?.CategoryId,
            CategoryRuleId       = matchedRule?.Id,
            CategorySetManually  = false
        };

        db.GroceryItems.Add(item);
        await db.SaveChangesAsync(ct);

        return (new CreateGroceryItemResponse(
            item.Id, item.ReceiptId, item.Description,
            item.Amount, item.Quantity, item.CategoryId), null);
    }
}
