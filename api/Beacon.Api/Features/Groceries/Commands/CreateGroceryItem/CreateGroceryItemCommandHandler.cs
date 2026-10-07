using Beacon.Api.Data;
using Beacon.Api.Features.Shared;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Groceries.Commands.CreateGroceryItem;

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
            .FirstOrDefault(r => RuleMatch.Matches(r.Pattern, r.MatchWholeDescription, r.Value, cmd.Description, cmd.Amount));

        var item = new GroceryItem
        {
            ReceiptId = cmd.ReceiptId,
            Description = cmd.Description.Trim(),
            Amount = cmd.Amount,
            Quantity = cmd.Quantity,
            CategoryRuleId = matchedRule?.Id,
            CategorySetManually = false
        };

        ExcludedCategory.ApplyCategory(
            item, matchedRule?.CategoryId, await ExcludedCategory.GetGroceryIdAsync(db, ct));

        db.GroceryItems.Add(item);
        await db.SaveChangesAsync(ct);

        return (new CreateGroceryItemResponse(
            item.Id, item.ReceiptId, item.Description,
            item.Amount, item.Quantity, item.CategoryId), null);
    }
}
