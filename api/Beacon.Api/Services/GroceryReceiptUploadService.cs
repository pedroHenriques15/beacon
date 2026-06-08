using System.Security.Cryptography;
using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services.Parsing;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Services;

public record GroceryReceiptUploadResult(
    int ReceiptId,
    string StoreName,
    DateOnly ReceiptDate,
    decimal Total,
    int ItemCount,
    bool WasDuplicate,
    IReadOnlyList<string> NewReceiptCategories);

public class GroceryReceiptUploadService(
    AppDbContext db,
    PdfExtractorService extractor,
    GroceryReceiptParserFactory parserFactory,
    FileStorageService fileStorage,
    ILogger<GroceryReceiptUploadService> logger)
{
    public async Task<GroceryReceiptUploadResult> ImportAsync(IFormFile file)
    {
        var tempPath = Path.ChangeExtension(Path.GetTempFileName(), ".pdf");
        string? savedPath = null;
        try
        {
            await using (var fs = File.Create(tempPath))
                await file.CopyToAsync(fs);

            var fileBytes = await File.ReadAllBytesAsync(tempPath);
            var hashBytes = SHA256.HashData(fileBytes);
            var fileHash  = Convert.ToHexString(hashBytes);

            var existing = await db.GroceryReceipts
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.FileHash == fileHash);

            if (existing is not null)
            {
                logger.LogInformation("GroceryReceiptUpload: duplicate file hash={Hash}, returning existing receiptId={Id}", fileHash, existing.Id);
                return new GroceryReceiptUploadResult(
                    existing.Id,
                    existing.StoreName,
                    existing.ReceiptDate,
                    existing.Total,
                    existing.Items.Count,
                    WasDuplicate: true,
                    NewReceiptCategories: []);
            }

            var pages    = await extractor.ExtractPagesAsync(tempPath);
            var fullText = string.Join("\n", pages);
            var parser   = parserFactory.DetectParser(fullText);
            var parsed   = parser.Parse(file.FileName, pages);

            var rules            = await db.GroceryCategoryRules.ToListAsync();
            var categoryMappings = await db.GroceryReceiptCategoryMappings.ToListAsync();

            savedPath = await fileStorage.SaveAsync(file);

            var items = parsed.Items.Select(pi =>
            {
                var matchedRule = rules
                    .OrderBy(r => r.Id)
                    .FirstOrDefault(r =>
                        (!string.IsNullOrEmpty(r.Pattern) && pi.Description.Contains(r.Pattern, StringComparison.Ordinal)) ||
                        (r.Value.HasValue && pi.Amount == r.Value.Value));

                var mapping = categoryMappings.FirstOrDefault(m => m.ReceiptCategoryName == pi.ReceiptCategory);

                return new GroceryItem
                {
                    Description          = pi.Description,
                    Amount               = pi.Amount,
                    Quantity             = pi.Quantity,
                    ReceiptCategory      = pi.ReceiptCategory,
                    CategoryId           = matchedRule?.CategoryId ?? mapping?.GroceryCategoryId,
                    CategoryRuleId       = matchedRule?.Id,
                    CategorySetManually  = false
                };
            }).ToList();

            var newCategories = parsed.Items
                .Where(pi => !string.IsNullOrEmpty(pi.ReceiptCategory))
                .Select(pi => pi.ReceiptCategory!)
                .Distinct()
                .Where(cat => !categoryMappings.Any(m => m.ReceiptCategoryName == cat))
                .OrderBy(c => c)
                .ToList();

            var receipt = new GroceryReceipt
            {
                StoreName  = parsed.StoreName,
                ReceiptDate = parsed.ReceiptDate,
                Total      = parsed.Total,
                SourceFile = file.FileName,
                PdfPath    = savedPath,
                FileHash   = fileHash,
                ImportedAt = DateTime.UtcNow,
                Items      = items
            };

            db.GroceryReceipts.Add(receipt);
            await db.SaveChangesAsync();

            logger.LogInformation("Imported grocery receipt {Store} {Date} with {Count} items",
                parsed.StoreName, parsed.ReceiptDate, items.Count);

            return new GroceryReceiptUploadResult(
                receipt.Id,
                receipt.StoreName,
                receipt.ReceiptDate,
                receipt.Total,
                items.Count,
                WasDuplicate: false,
                NewReceiptCategories: newCategories);
        }
        catch
        {
            if (savedPath is not null) fileStorage.Delete(savedPath);
            throw;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
