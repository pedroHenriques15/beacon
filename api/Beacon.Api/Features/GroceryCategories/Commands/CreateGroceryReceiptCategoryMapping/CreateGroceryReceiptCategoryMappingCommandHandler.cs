using Beacon.Api.Data;
using Beacon.Api.Features.GroceryCategories.Queries.GetGroceryReceiptCategoryMappings;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryReceiptCategoryMapping;

public record CreateGroceryReceiptCategoryMappingCommand(string ReceiptCategoryName, int GroceryCategoryId);

public class CreateGroceryReceiptCategoryMappingCommandHandler(AppDbContext db, ILogger<CreateGroceryReceiptCategoryMappingCommandHandler> logger)
{
    public async Task<(GroceryReceiptCategoryMappingResponse? Response, bool IsConflict)> HandleAsync(
        CreateGroceryReceiptCategoryMappingCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("CreateGroceryReceiptCategoryMapping: receiptCategory={Name} groceryCategoryId={Id}",
            cmd.ReceiptCategoryName, cmd.GroceryCategoryId);

        var cat = await db.GroceryCategories.FindAsync([cmd.GroceryCategoryId], ct);
        if (cat is null) return (null, false);

        var alreadyExists = await db.GroceryReceiptCategoryMappings
            .AnyAsync(m => m.ReceiptCategoryName == cmd.ReceiptCategoryName, ct);
        if (alreadyExists) return (null, true);

        var mapping = new GroceryReceiptCategoryMapping
        {
            ReceiptCategoryName = cmd.ReceiptCategoryName,
            GroceryCategoryId   = cmd.GroceryCategoryId
        };
        db.GroceryReceiptCategoryMappings.Add(mapping);
        await db.SaveChangesAsync(ct);

        var unassigned = await db.GroceryItems
            .Where(i => i.ReceiptCategory == cmd.ReceiptCategoryName && !i.CategorySetManually && i.CategoryId == null)
            .ToListAsync(ct);

        foreach (var item in unassigned)
            item.CategoryId = cmd.GroceryCategoryId;

        if (unassigned.Count > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation("CreateGroceryReceiptCategoryMapping: created id={Id}, retroactively applied to {Count} items",
            mapping.Id, unassigned.Count);

        return (new GroceryReceiptCategoryMappingResponse(mapping.Id, mapping.ReceiptCategoryName, mapping.GroceryCategoryId, cat.Name), false);
    }
}
