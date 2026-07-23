using Beacon.Api.Data;
using Beacon.Api.Features.Groceries.Commands.CreateGroceryItem;
using Beacon.Api.Services;
using Beacon.Api.Features.Groceries.Commands.DeleteGroceryItem;
using Beacon.Api.Features.Groceries.Commands.DeleteGroceryReceipt;
using Beacon.Api.Features.Groceries.Commands.MarkGroceryItemsExcluded;
using Beacon.Api.Features.Groceries.Commands.SetGroceryItemCategory;
using Beacon.Api.Features.Groceries.Commands.UpdateGroceryItem;
using Beacon.Api.Features.Groceries.Queries.GetGroceryReceipts;
using Beacon.Api.Features.Groceries.Shared;
using Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryCategory;
using Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryReceiptCategoryMapping;
using Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryCategory;
using Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryReceiptCategoryMapping;
using Beacon.Api.Features.GroceryCategories.Commands.UpdateGroceryCategory;
using Beacon.Api.Features.GroceryCategories.Queries.GetGroceryReceiptCategoryMappings;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Handlers;

public class GroceriesHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<GroceryReceipt> SeedReceiptAsync(
        AppDbContext db,
        string store,
        DateOnly? date = null,
        IEnumerable<GroceryItem>? items = null)
    {
        var receipt = new GroceryReceipt
        {
            StoreName   = store,
            ReceiptDate = date ?? new DateOnly(2026, 1, 10),
            Total       = 20m,
            SourceFile  = $"{store}.pdf",
            Items       = items?.ToList() ?? []
        };
        db.GroceryReceipts.Add(receipt);
        await db.SaveChangesAsync();
        return receipt;
    }

    [Fact]
    public async Task GetGroceryReceipts_ReturnsAll()
    {
        await using var db = CreateDb(nameof(GetGroceryReceipts_ReturnsAll));
        await SeedReceiptAsync(db, "Continente");
        await SeedReceiptAsync(db, "Pingo Doce");

        var handler = new GetGroceryReceiptsQueryHandler(db, NullLogger<GetGroceryReceiptsQueryHandler>.Instance);
        var result  = await handler.HandleAsync(null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetGroceryReceipts_FiltersByStore()
    {
        await using var db = CreateDb(nameof(GetGroceryReceipts_FiltersByStore));
        await SeedReceiptAsync(db, "Continente");
        await SeedReceiptAsync(db, "Pingo Doce");

        var handler = new GetGroceryReceiptsQueryHandler(db, NullLogger<GetGroceryReceiptsQueryHandler>.Instance);
        var result  = await handler.HandleAsync("Continente");

        Assert.Single(result);
        Assert.Equal("Continente", result[0].StoreName);
    }

    [Fact]
    public async Task DeleteGroceryReceipt_RemovesReceiptAndItems()
    {
        await using var db = CreateDb(nameof(DeleteGroceryReceipt_RemovesReceiptAndItems));
        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "Bread", Amount = 1.50m, Quantity = 1 }
        ]);

        var handler = new DeleteGroceryReceiptCommandHandler(db, new FileStorageService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), NullLogger<FileStorageService>.Instance), NullLogger<DeleteGroceryReceiptCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new DeleteGroceryReceiptCommand(receipt.Id));

        Assert.True(result);
        Assert.Null(await db.GroceryReceipts.FindAsync(receipt.Id));
        Assert.Empty(await db.GroceryItems.Where(i => i.ReceiptId == receipt.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteGroceryReceipt_ReturnsFalseForMissing()
    {
        await using var db = CreateDb(nameof(DeleteGroceryReceipt_ReturnsFalseForMissing));

        var handler = new DeleteGroceryReceiptCommandHandler(db, new FileStorageService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), NullLogger<FileStorageService>.Instance), NullLogger<DeleteGroceryReceiptCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new DeleteGroceryReceiptCommand(9999));

        Assert.False(result);
    }

    [Fact]
    public async Task CreateGroceryItem_PersistsCorrectly()
    {
        await using var db = CreateDb(nameof(CreateGroceryItem_PersistsCorrectly));
        var receipt = await SeedReceiptAsync(db, "Continente");

        var handler = new CreateGroceryItemCommandHandler(db, NullLogger<CreateGroceryItemCommandHandler>.Instance);
        var (result, error) = await handler.HandleAsync(
            new CreateGroceryItemCommand(receipt.Id, "Milk", 1.29m, 2));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("Milk", result!.Description);
        Assert.Equal(1.29m, result.Amount);
        Assert.Equal(2m, result.Quantity);

        var saved = await db.GroceryItems.FindAsync(result.Id);
        Assert.NotNull(saved);
        Assert.Equal(receipt.Id, saved!.ReceiptId);
    }

    [Fact]
    public async Task CreateGroceryItem_ReturnsErrorForUnknownReceipt()
    {
        await using var db = CreateDb(nameof(CreateGroceryItem_ReturnsErrorForUnknownReceipt));

        var handler = new CreateGroceryItemCommandHandler(db, NullLogger<CreateGroceryItemCommandHandler>.Instance);
        var (result, error) = await handler.HandleAsync(
            new CreateGroceryItemCommand(9999, "Milk", 1.29m, 1));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task UpdateGroceryItem_UpdatesFields()
    {
        await using var db = CreateDb(nameof(UpdateGroceryItem_UpdatesFields));
        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "OldName", Amount = 2m, Quantity = 1 }
        ]);
        var item = receipt.Items.First();

        var handler = new UpdateGroceryItemCommandHandler(db, NullLogger<UpdateGroceryItemCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new UpdateGroceryItemCommand(item.Id, "NewName", 3.99m, 2));

        Assert.NotNull(result);
        Assert.Equal("NewName", result!.Description);
        Assert.Equal(3.99m, result.Amount);
        Assert.Equal(2m, result.Quantity);
    }

    [Fact]
    public async Task UpdateGroceryItem_ReturnsNullForMissing()
    {
        await using var db = CreateDb(nameof(UpdateGroceryItem_ReturnsNullForMissing));

        var handler = new UpdateGroceryItemCommandHandler(db, NullLogger<UpdateGroceryItemCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new UpdateGroceryItemCommand(9999, "X", 1m, 1));

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteGroceryItem_RemovesItem()
    {
        await using var db = CreateDb(nameof(DeleteGroceryItem_RemovesItem));
        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "Bread", Amount = 1m, Quantity = 1 }
        ]);
        var item = receipt.Items.First();

        var handler = new DeleteGroceryItemCommandHandler(db, NullLogger<DeleteGroceryItemCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new DeleteGroceryItemCommand(item.Id));

        Assert.True(result);
        Assert.Null(await db.GroceryItems.FindAsync(item.Id));
    }

    [Fact]
    public async Task DeleteGroceryItem_ReturnsFalseForMissing()
    {
        await using var db = CreateDb(nameof(DeleteGroceryItem_ReturnsFalseForMissing));

        var handler = new DeleteGroceryItemCommandHandler(db, NullLogger<DeleteGroceryItemCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new DeleteGroceryItemCommand(9999));

        Assert.False(result);
    }

    [Fact]
    public async Task SetGroceryItemCategory_AssignsCategoryAndSetsManualFlag()
    {
        await using var db = CreateDb(nameof(SetGroceryItemCategory_AssignsCategoryAndSetsManualFlag));
        var cat = new GroceryCategory { Name = "Dairy", Color = "#fff" };
        db.GroceryCategories.Add(cat);
        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "Milk", Amount = 1m, Quantity = 1 }
        ]);
        var item = receipt.Items.First();

        var handler = new SetGroceryItemCategoryCommandHandler(db, NullLogger<SetGroceryItemCategoryCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new SetGroceryItemCategoryCommand(item.Id, cat.Id, null));

        Assert.NotNull(result);
        Assert.Equal(cat.Id, result!.CategoryId);

        var saved = await db.GroceryItems.FindAsync(item.Id);
        Assert.True(saved!.CategorySetManually);
        Assert.Equal(cat.Id, saved.CategoryId);
    }

    [Fact]
    public async Task SetGroceryItemCategory_ClearsWithNull()
    {
        await using var db = CreateDb(nameof(SetGroceryItemCategory_ClearsWithNull));
        var cat = new GroceryCategory { Name = "Dairy", Color = "#fff" };
        db.GroceryCategories.Add(cat);
        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "Milk", Amount = 1m, Quantity = 1, CategoryId = cat.Id, CategorySetManually = true }
        ]);
        var item = receipt.Items.First();

        var handler = new SetGroceryItemCategoryCommandHandler(db, NullLogger<SetGroceryItemCategoryCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new SetGroceryItemCategoryCommand(item.Id, null, null));

        Assert.NotNull(result);
        Assert.Null(result!.CategoryId);

        var saved = await db.GroceryItems.FindAsync(item.Id);
        Assert.Null(saved!.CategoryId);
    }

    [Fact]
    public async Task SetGroceryItemCategory_ReturnsNullForMissing()
    {
        await using var db = CreateDb(nameof(SetGroceryItemCategory_ReturnsNullForMissing));

        var handler = new SetGroceryItemCategoryCommandHandler(db, NullLogger<SetGroceryItemCategoryCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new SetGroceryItemCategoryCommand(9999, null, null));

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateGroceryCategory_CreatesWithoutRule()
    {
        await using var db = CreateDb(nameof(CreateGroceryCategory_CreatesWithoutRule));

        var applyRule = new GroceryApplyRuleService(db);
        var handler   = new CreateGroceryCategoryCommandHandler(db, applyRule, NullLogger<CreateGroceryCategoryCommandHandler>.Instance);
        var result    = await handler.HandleAsync(new CreateGroceryCategoryCommand("Bakery", "#ff0000", null));

        Assert.Equal("Bakery", result.Name);
        Assert.Equal("#ff0000", result.Color);

        var saved = await db.GroceryCategories.FindAsync(result.Id);
        Assert.NotNull(saved);
        Assert.Empty(await db.GroceryCategoryRules.Where(r => r.CategoryId == result.Id).ToListAsync());
    }

    [Fact]
    public async Task UpdateGroceryCategory_UpdatesNameAndColor()
    {
        await using var db = CreateDb(nameof(UpdateGroceryCategory_UpdatesNameAndColor));
        var cat = new GroceryCategory { Name = "OldName", Color = "#000" };
        db.GroceryCategories.Add(cat);
        await db.SaveChangesAsync();

        var handler = new UpdateGroceryCategoryCommandHandler(db, NullLogger<UpdateGroceryCategoryCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new UpdateGroceryCategoryCommand(cat.Id, "NewName", "#fff"));

        Assert.NotNull(result);
        Assert.Equal("NewName", result!.Name);
        Assert.Equal("#fff", result.Color);
    }

    [Fact]
    public async Task UpdateGroceryCategory_ReturnsFalseForMissing()
    {
        await using var db = CreateDb(nameof(UpdateGroceryCategory_ReturnsFalseForMissing));

        var handler = new UpdateGroceryCategoryCommandHandler(db, NullLogger<UpdateGroceryCategoryCommandHandler>.Instance);
        var result  = await handler.HandleAsync(new UpdateGroceryCategoryCommand(9999, "X", "#fff"));

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteGroceryCategory_Removes()
    {
        await using var db = CreateDb(nameof(DeleteGroceryCategory_Removes));
        var cat = new GroceryCategory { Name = "Bakery", Color = "#000" };
        db.GroceryCategories.Add(cat);
        await db.SaveChangesAsync();

        var handler = new DeleteGroceryCategoryCommandHandler(db, NullLogger<DeleteGroceryCategoryCommandHandler>.Instance);
        var (found, isProtected) = await handler.HandleAsync(new DeleteGroceryCategoryCommand(cat.Id));

        Assert.True(found);
        Assert.False(isProtected);
        Assert.Null(await db.GroceryCategories.FindAsync(cat.Id));
    }

    [Fact]
    public async Task DeleteGroceryCategory_Returns409ForProtected()
    {
        await using var db = CreateDb(nameof(DeleteGroceryCategory_Returns409ForProtected));
        var cat = new GroceryCategory { Name = "System", Color = "#000", IsProtected = true };
        db.GroceryCategories.Add(cat);
        await db.SaveChangesAsync();

        var handler = new DeleteGroceryCategoryCommandHandler(db, NullLogger<DeleteGroceryCategoryCommandHandler>.Instance);
        var (found, isProtected) = await handler.HandleAsync(new DeleteGroceryCategoryCommand(cat.Id));

        Assert.True(found);
        Assert.True(isProtected);
        Assert.NotNull(await db.GroceryCategories.FindAsync(cat.Id));
    }

    [Fact]
    public async Task CreateGroceryReceiptCategoryMapping_CreatesMappingAndAppliesRetroactively()
    {
        await using var db = CreateDb(nameof(CreateGroceryReceiptCategoryMapping_CreatesMappingAndAppliesRetroactively));
        var cat = new GroceryCategory { Name = "Sweets", Color = "#f00" };
        db.GroceryCategories.Add(cat);
        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "CREPES", Amount = 4.58m, Quantity = 2, ReceiptCategory = "Mercearia Doce" },
            new GroceryItem { Description = "BREAD", Amount = 1.20m, Quantity = 1, ReceiptCategory = "Padaria" }
        ]);

        var handler           = new CreateGroceryReceiptCategoryMappingCommandHandler(db, NullLogger<CreateGroceryReceiptCategoryMappingCommandHandler>.Instance);
        var (result, isConflict) = await handler.HandleAsync(
            new CreateGroceryReceiptCategoryMappingCommand("Mercearia Doce", cat.Id));

        Assert.False(isConflict);
        Assert.NotNull(result);
        Assert.Equal("Mercearia Doce", result!.ReceiptCategoryName);
        Assert.Equal(cat.Id, result.GroceryCategoryId);
        Assert.Equal("Sweets", result.CategoryName);

        var items = await db.GroceryItems.Where(i => i.ReceiptId == receipt.Id).ToListAsync();
        var crepes = items.First(i => i.Description == "CREPES");
        var bread  = items.First(i => i.Description == "BREAD");

        Assert.Equal(cat.Id, crepes.CategoryId);
        Assert.Null(bread.CategoryId);
    }

    [Fact]
    public async Task CreateGroceryReceiptCategoryMapping_ReturnsNullForUnknownCategory()
    {
        await using var db = CreateDb(nameof(CreateGroceryReceiptCategoryMapping_ReturnsNullForUnknownCategory));

        var handler              = new CreateGroceryReceiptCategoryMappingCommandHandler(db, NullLogger<CreateGroceryReceiptCategoryMappingCommandHandler>.Instance);
        var (result, isConflict) = await handler.HandleAsync(
            new CreateGroceryReceiptCategoryMappingCommand("Mercearia Doce", 9999));

        Assert.Null(result);
        Assert.False(isConflict);
    }

    [Fact]
    public async Task CreateGroceryReceiptCategoryMapping_ReturnsConflictForDuplicateName()
    {
        await using var db = CreateDb(nameof(CreateGroceryReceiptCategoryMapping_ReturnsConflictForDuplicateName));
        var cat = new GroceryCategory { Name = "Sweets", Color = "#f00" };
        db.GroceryCategories.Add(cat);
        await db.SaveChangesAsync();

        var handler = new CreateGroceryReceiptCategoryMappingCommandHandler(db, NullLogger<CreateGroceryReceiptCategoryMappingCommandHandler>.Instance);
        await handler.HandleAsync(new CreateGroceryReceiptCategoryMappingCommand("Mercearia Doce", cat.Id));

        var (result, isConflict) = await handler.HandleAsync(
            new CreateGroceryReceiptCategoryMappingCommand("Mercearia Doce", cat.Id));

        Assert.Null(result);
        Assert.True(isConflict);
    }

    [Fact]
    public async Task GetGroceryReceiptCategoryMappings_ReturnsAll()
    {
        await using var db = CreateDb(nameof(GetGroceryReceiptCategoryMappings_ReturnsAll));
        var cat = new GroceryCategory { Name = "Drinks", Color = "#00f" };
        db.GroceryCategories.Add(cat);
        await db.SaveChangesAsync();

        db.GroceryReceiptCategoryMappings.AddRange(
            new GroceryReceiptCategoryMapping { ReceiptCategoryName = "Soft Drinks",   GroceryCategoryId = cat.Id },
            new GroceryReceiptCategoryMapping { ReceiptCategoryName = "Mercearia Doce", GroceryCategoryId = cat.Id });
        await db.SaveChangesAsync();

        var handler = new GetGroceryReceiptCategoryMappingsQueryHandler(db);
        var result  = await handler.HandleAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.ReceiptCategoryName == "Soft Drinks");
        Assert.Contains(result, m => m.CategoryName == "Drinks");
    }

    [Fact]
    public async Task DeleteGroceryReceiptCategoryMapping_Removes()
    {
        await using var db = CreateDb(nameof(DeleteGroceryReceiptCategoryMapping_Removes));
        var cat = new GroceryCategory { Name = "Drinks", Color = "#00f" };
        db.GroceryCategories.Add(cat);
        var mapping = new GroceryReceiptCategoryMapping { ReceiptCategoryName = "Soft Drinks", GroceryCategoryId = cat.Id };
        db.GroceryReceiptCategoryMappings.Add(mapping);
        await db.SaveChangesAsync();

        var handler = new DeleteGroceryReceiptCategoryMappingCommandHandler(db);
        var result  = await handler.HandleAsync(new DeleteGroceryReceiptCategoryMappingCommand(mapping.Id));

        Assert.True(result);
        Assert.Null(await db.GroceryReceiptCategoryMappings.FindAsync(mapping.Id));
    }

    [Fact]
    public async Task DeleteGroceryReceiptCategoryMapping_ReturnsFalseForMissing()
    {
        await using var db = CreateDb(nameof(DeleteGroceryReceiptCategoryMapping_ReturnsFalseForMissing));

        var handler = new DeleteGroceryReceiptCategoryMappingCommandHandler(db);
        var result  = await handler.HandleAsync(new DeleteGroceryReceiptCategoryMappingCommand(9999));

        Assert.False(result);
    }

    [Fact]
    public async Task GroceryApplyRuleService_AppliesMatchingRulesOnUpload()
    {
        await using var db = CreateDb(nameof(GroceryApplyRuleService_AppliesMatchingRulesOnUpload));
        var cat = new GroceryCategory { Name = "Soft Drinks", Color = "#00f" };
        db.GroceryCategories.Add(cat);
        await db.SaveChangesAsync();

        var rule = new GroceryCategoryRule { CategoryId = cat.Id, Pattern = "COLA" };
        db.GroceryCategoryRules.Add(rule);

        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "REF.C/GAS C.COLA LATA", Amount = 0.90m, Quantity = 1 },
            new GroceryItem { Description = "WATER STILL",           Amount = 0.50m, Quantity = 1 }
        ]);

        var service = new GroceryApplyRuleService(db);
        await service.ApplyAsync(rule);

        var items = await db.GroceryItems.Where(i => i.ReceiptId == receipt.Id).ToListAsync();
        var cola  = items.First(i => i.Description.Contains("COLA"));
        var water = items.First(i => i.Description == "WATER STILL");

        Assert.Equal(cat.Id, cola.CategoryId);
        Assert.Equal(rule.Id, cola.CategoryRuleId);
        Assert.Null(water.CategoryId);
    }

    [Fact]
    public async Task GroceryApplyRuleService_SkipsManuallySetItems()
    {
        await using var db = CreateDb(nameof(GroceryApplyRuleService_SkipsManuallySetItems));
        var cat1 = new GroceryCategory { Name = "Soft Drinks", Color = "#00f" };
        var cat2 = new GroceryCategory { Name = "Manual", Color = "#0f0" };
        db.GroceryCategories.AddRange(cat1, cat2);
        await db.SaveChangesAsync();

        var rule = new GroceryCategoryRule { CategoryId = cat1.Id, Pattern = "COLA" };
        db.GroceryCategoryRules.Add(rule);

        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem
            {
                Description        = "REF.C/GAS C.COLA LATA",
                Amount             = 0.90m,
                Quantity           = 1,
                CategoryId         = cat2.Id,
                CategorySetManually = true
            }
        ]);

        var service = new GroceryApplyRuleService(db);
        await service.ApplyAsync(rule);

        var item = await db.GroceryItems.FirstAsync(i => i.ReceiptId == receipt.Id);
        Assert.Equal(cat2.Id, item.CategoryId);
        Assert.True(item.CategorySetManually);
    }

    [Fact]
    public async Task MarkGroceryItemsExcluded_SetsIsExcludedTrue()
    {
        await using var db = CreateDb(nameof(MarkGroceryItemsExcluded_SetsIsExcludedTrue));
        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "Leite", Amount = 1.20m },
            new GroceryItem { Description = "Pão",   Amount = 0.50m },
        ]);

        var handler = new MarkGroceryItemsExcludedCommandHandler(db, NullLogger<MarkGroceryItemsExcludedCommandHandler>.Instance);
        var ids = receipt.Items.Select(i => i.Id).ToArray();
        await handler.HandleAsync(new MarkGroceryItemsExcludedCommand(ids));

        var updated = await db.GroceryItems.Where(i => ids.Contains(i.Id)).ToListAsync();
        Assert.All(updated, i => Assert.True(i.IsExcluded));
    }

    [Fact]
    public async Task MarkGroceryItemsExcluded_WithUnmarkTrue_SetsIsExcludedFalse()
    {
        await using var db = CreateDb(nameof(MarkGroceryItemsExcluded_WithUnmarkTrue_SetsIsExcludedFalse));
        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "Leite", Amount = 1.20m },
        ]);

        var handler = new MarkGroceryItemsExcludedCommandHandler(db, NullLogger<MarkGroceryItemsExcludedCommandHandler>.Instance);
        var ids = receipt.Items.Select(i => i.Id).ToArray();
        await handler.HandleAsync(new MarkGroceryItemsExcludedCommand(ids));
        await handler.HandleAsync(new MarkGroceryItemsExcludedCommand(ids, Unmark: true));

        var updated = await db.GroceryItems.Where(i => ids.Contains(i.Id)).ToListAsync();
        Assert.All(updated, i => Assert.False(i.IsExcluded));
        Assert.All(updated, i => Assert.Null(i.CategoryId));
    }

    [Fact]
    public async Task MarkGroceryItemsExcluded_AssignsExcludedCategoryWhenExists()
    {
        await using var db = CreateDb(nameof(MarkGroceryItemsExcluded_AssignsExcludedCategoryWhenExists));
        var excludedCat = new GroceryCategory { Name = "Excluded", Color = "#aaa" };
        db.GroceryCategories.Add(excludedCat);
        await db.SaveChangesAsync();

        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "Saco Plástico", Amount = 0.10m },
        ]);

        var handler = new MarkGroceryItemsExcludedCommandHandler(db, NullLogger<MarkGroceryItemsExcludedCommandHandler>.Instance);
        var ids = receipt.Items.Select(i => i.Id).ToArray();
        await handler.HandleAsync(new MarkGroceryItemsExcludedCommand(ids));

        var item = await db.GroceryItems.FirstAsync(i => ids.Contains(i.Id));
        Assert.True(item.IsExcluded);
        Assert.Equal(excludedCat.Id, item.CategoryId);
    }

    [Fact]
    public async Task MarkGroceryItemsExcluded_WithoutExcludedCategory_StillSetsFlag()
    {
        await using var db = CreateDb(nameof(MarkGroceryItemsExcluded_WithoutExcludedCategory_StillSetsFlag));
        var receipt = await SeedReceiptAsync(db, "Continente", items:
        [
            new GroceryItem { Description = "Agua", Amount = 0.60m },
        ]);

        var handler = new MarkGroceryItemsExcludedCommandHandler(db, NullLogger<MarkGroceryItemsExcludedCommandHandler>.Instance);
        var ids = receipt.Items.Select(i => i.Id).ToArray();
        await handler.HandleAsync(new MarkGroceryItemsExcludedCommand(ids));

        var item = await db.GroceryItems.FirstAsync(i => ids.Contains(i.Id));
        Assert.True(item.IsExcluded);
        Assert.Null(item.CategoryId);
    }

    [Fact]
    public async Task MarkGroceryItemsExcluded_NonExistentIds_DoesNotThrow()
    {
        await using var db = CreateDb(nameof(MarkGroceryItemsExcluded_NonExistentIds_DoesNotThrow));
        var handler = new MarkGroceryItemsExcludedCommandHandler(db, NullLogger<MarkGroceryItemsExcludedCommandHandler>.Instance);
        var ex = await Record.ExceptionAsync(() => handler.HandleAsync(new MarkGroceryItemsExcludedCommand([9999, 8888])));
        Assert.Null(ex);
    }
}
