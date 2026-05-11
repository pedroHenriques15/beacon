using FinanceHub.Api.Data;
using FinanceHub.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Groceries.Commands.DeleteGroceryReceipt;

public class DeleteGroceryReceiptCommandHandler(AppDbContext db, FileStorageService fileStorage, ILogger<DeleteGroceryReceiptCommandHandler> logger)
{
    public async Task<bool> HandleAsync(DeleteGroceryReceiptCommand cmd, CancellationToken ct = default)
    {
        var receipt = await db.GroceryReceipts
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == cmd.Id, ct);

        if (receipt is null) return false;

        if (receipt.PdfPath is not null)
        {
            try { fileStorage.Delete(receipt.PdfPath); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete PDF for grocery receipt {Id}", receipt.Id); }
        }

        db.GroceryReceipts.Remove(receipt);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
