using Beacon.Api.Data;
using Beacon.Api.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Groceries.Commands.UpdateGroceryItem;

public class UpdateGroceryItemCommandHandler(AppDbContext db, ILogger<UpdateGroceryItemCommandHandler> logger)
{
    public async Task<UpdateGroceryItemResponse?> HandleAsync(UpdateGroceryItemCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("UpdateGroceryItem: id={Id}", cmd.Id);

        new UpdateGroceryItemCommandValidator().Validate(cmd).ThrowIfInvalid();

        var item = await db.GroceryItems.FirstOrDefaultAsync(i => i.Id == cmd.Id, ct);
        if (item is null) return null;

        if (cmd.Description is not null && cmd.Description.Trim().Length > 0)
            item.Description = cmd.Description.Trim();
        if (cmd.Amount is not null && cmd.Amount > 0)
            item.Amount = cmd.Amount.Value;
        if (cmd.Quantity is not null && cmd.Quantity > 0)
            item.Quantity = cmd.Quantity.Value;

        await db.SaveChangesAsync(ct);
        return new UpdateGroceryItemResponse(item.Id, item.ReceiptId, item.Description, item.Amount, item.Quantity, item.CategoryId);
    }
}
