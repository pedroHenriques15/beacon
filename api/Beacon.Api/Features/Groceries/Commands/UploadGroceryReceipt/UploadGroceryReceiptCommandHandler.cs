using Beacon.Api.Services;

namespace Beacon.Api.Features.Groceries.Commands.UploadGroceryReceipt;

public class UploadGroceryReceiptCommandHandler(GroceryReceiptUploadService uploadService, ILogger<UploadGroceryReceiptCommandHandler> logger)
{
    public Task<GroceryReceiptUploadResult> HandleAsync(UploadGroceryReceiptCommand command, CancellationToken ct = default)
    {
        logger.LogInformation("UploadGroceryReceipt: file={FileName}", command.File.FileName);
        return uploadService.ImportAsync(command.File, ct: ct);
    }
}
