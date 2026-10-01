using Beacon.Api.Data;

namespace Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryReceiptCategoryMapping;

public record DeleteGroceryReceiptCategoryMappingCommand(int Id);

public class DeleteGroceryReceiptCategoryMappingCommandHandler(AppDbContext db)
{
    public async Task<bool> HandleAsync(DeleteGroceryReceiptCategoryMappingCommand cmd, CancellationToken ct = default)
    {
        var mapping = await db.GroceryReceiptCategoryMappings.FindAsync([cmd.Id], ct);
        if (mapping is null) return false;

        db.GroceryReceiptCategoryMappings.Remove(mapping);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
