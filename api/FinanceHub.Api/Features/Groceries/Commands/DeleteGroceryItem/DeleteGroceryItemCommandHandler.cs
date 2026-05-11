using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Groceries.Commands.DeleteGroceryItem;

public class DeleteGroceryItemCommandHandler(AppDbContext db, ILogger<DeleteGroceryItemCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteGroceryItemCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("DeleteGroceryItem: id={Id}", cmd.Id);
        var item = await db.GroceryItems.FirstOrDefaultAsync(i => i.Id == cmd.Id, ct);
        if (item is null) return false;

        db.GroceryItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
