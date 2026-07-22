using Beacon.Api.Data;
using Beacon.Api.Features.Salary.Commands.CreateSalaryItemCategory;
using Beacon.Api.Features.Salary.Commands.CreateSalarySlip;
using Beacon.Api.Features.Salary.Commands.DeleteSalaryItemCategory;
using Beacon.Api.Features.Salary.Commands.DeleteSalarySlip;
using Beacon.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Beacon.Api.Features.Salary.Commands.UpdateSalarySlip;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests.Handlers;

public class SalaryHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CreateItemCategory_ReturnsError_WhenProfileDoesNotExist()
    {
        await using var db = CreateDb(nameof(CreateItemCategory_ReturnsError_WhenProfileDoesNotExist));
        var handler = new CreateSalaryItemCategoryCommandHandler(db);

        var (result, error) = await handler.HandleAsync(
            new CreateSalaryItemCategoryCommand(999, "Test", "#ff0000", "deduction"));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateItemCategory_ReturnsCategory_WhenProfileExists()
    {
        await using var db = CreateDb(nameof(CreateItemCategory_ReturnsCategory_WhenProfileExists));
        var profile = new SalaryProfile { Name = "Test Profile" };
        db.SalaryProfiles.Add(profile);
        await db.SaveChangesAsync();

        var handler = new CreateSalaryItemCategoryCommandHandler(db);

        var (result, error) = await handler.HandleAsync(
            new CreateSalaryItemCategoryCommand(profile.Id, "Bonus", "#00ff00", "earning"));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal("Bonus", result.Name);
        Assert.Equal(profile.Id, result.ProfileId);
    }

    private static async Task<(SalaryProfile profile, SalaryItemCategory cat, SalarySlip slip)>
        SeedSlipAsync(AppDbContext db, string profileName = "Acme")
    {
        var profile = new SalaryProfile { Name = profileName };
        db.SalaryProfiles.Add(profile);
        await db.SaveChangesAsync();

        var cat = new SalaryItemCategory
        {
            SalaryProfileId = profile.Id, Name = "Base", Color = "#22c55e", ItemType = "income"
        };
        db.SalaryItemCategories.Add(cat);

        var slip = new SalarySlip
        {
            SalaryProfileId = profile.Id,
            Period = new DateOnly(2026, 1, 1),
            GrossAmount = 1000m,
            NetAmount = 800m,
        };
        db.SalarySlips.Add(slip);
        await db.SaveChangesAsync();
        return (profile, cat, slip);
    }

    [Fact]
    public async Task CreateSlip_PersistsSlipAndLineItems()
    {
        await using var db = CreateDb(nameof(CreateSlip_PersistsSlipAndLineItems));
        var (profile, cat, _) = await SeedSlipAsync(db);

        var handler = new CreateSalarySlipCommandHandler(db);
        var (result, error) = await handler.HandleAsync(new CreateSalarySlipCommand(
            profile.Id, new DateOnly(2026, 2, 1), 1200m, 950m, "feb", "feb.pdf", "feb-src.pdf",
            [new CreateLineItemRequest(cat.Id, 1200m, 0, 160m, 7.5m, null, null)],
            BaseAmount: 1000m, HoursWorked: 160m, HourlyRate: 7.5m));

        Assert.Null(error);
        Assert.NotNull(result);

        var persisted = await db.SalarySlips.AsNoTracking()
            .Include(sl => sl.LineItems)
            .SingleAsync(sl => sl.Period == new DateOnly(2026, 2, 1));
        Assert.Equal(1200m, persisted.GrossAmount);
        Assert.Equal("feb.pdf", persisted.PdfPath);
        Assert.Equal(1000m, persisted.BaseAmount);
        var line = Assert.Single(persisted.LineItems);
        Assert.Equal(cat.Id, line.SalaryItemCategoryId);
        Assert.Equal(1200m, line.Amount);
    }

    [Fact]
    public async Task CreateSlip_DuplicateProfileAndPeriod_ReturnsError()
    {
        await using var db = CreateDb(nameof(CreateSlip_DuplicateProfileAndPeriod_ReturnsError));
        var (profile, _, slip) = await SeedSlipAsync(db);

        var handler = new CreateSalarySlipCommandHandler(db);
        var (result, error) = await handler.HandleAsync(new CreateSalarySlipCommand(
            profile.Id, slip.Period, 500m, 400m, null, null, null, []));

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("already exists", error);
    }

    [Fact]
    public async Task CreateSlip_CategoryFromAnotherProfile_ReturnsError()
    {
        await using var db = CreateDb(nameof(CreateSlip_CategoryFromAnotherProfile_ReturnsError));
        var (profile, _, _) = await SeedSlipAsync(db);
        var other = new SalaryProfile { Name = "Other" };
        db.SalaryProfiles.Add(other);
        await db.SaveChangesAsync();
        var foreignCat = new SalaryItemCategory
        {
            SalaryProfileId = other.Id, Name = "Foreign", Color = "#fff", ItemType = "income"
        };
        db.SalaryItemCategories.Add(foreignCat);
        await db.SaveChangesAsync();

        var handler = new CreateSalarySlipCommandHandler(db);
        var (result, error) = await handler.HandleAsync(new CreateSalarySlipCommand(
            profile.Id, new DateOnly(2026, 3, 1), 500m, 400m, null, null, null,
            [new CreateLineItemRequest(foreignCat.Id, 500m, 0, null, null, null, null)]));

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("does not belong", error);
    }

    [Fact]
    public async Task DeleteSlip_RemovesSlipLineItemsAndPdf()
    {
        await using var db = CreateDb(nameof(DeleteSlip_RemovesSlipLineItemsAndPdf));
        var (_, cat, slip) = await SeedSlipAsync(db);

        var storageRoot = Path.Combine(Path.GetTempPath(), $"fh_salary_del_{Guid.NewGuid()}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            var pdfPath = Path.Combine(storageRoot, "slip.pdf");
            await File.WriteAllBytesAsync(pdfPath, "pdf"u8.ToArray());
            slip.PdfPath = pdfPath;
            db.SalaryLineItems.Add(new SalaryLineItem
            {
                SalarySlipId = slip.Id, SalaryItemCategoryId = cat.Id, Amount = 100m, SortOrder = 0
            });
            await db.SaveChangesAsync();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = storageRoot })
                .Build();
            var fileStorage = new FileStorageService(config, NullLogger<FileStorageService>.Instance);
            var handler = new DeleteSalarySlipCommandHandler(db, fileStorage, NullLogger<DeleteSalarySlipCommandHandler>.Instance);

            var deleted = await handler.HandleAsync(new DeleteSalarySlipCommand(slip.Id));

            Assert.True(deleted);
            Assert.False(await db.SalarySlips.AnyAsync());
            Assert.False(await db.SalaryLineItems.AnyAsync());
            Assert.False(File.Exists(pdfPath));
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteItemCategory_InUse_ReturnsConflictSignal()
    {
        await using var db = CreateDb(nameof(DeleteItemCategory_InUse_ReturnsConflictSignal));
        var (_, cat, slip) = await SeedSlipAsync(db);
        db.SalaryLineItems.Add(new SalaryLineItem
        {
            SalarySlipId = slip.Id, SalaryItemCategoryId = cat.Id, Amount = 100m, SortOrder = 0
        });
        await db.SaveChangesAsync();

        var handler = new DeleteSalaryItemCategoryCommandHandler(db);
        var (found, isProtected, inUse) = await handler.HandleAsync(new DeleteSalaryItemCategoryCommand(cat.Id));

        Assert.True(found);
        Assert.False(isProtected);
        Assert.True(inUse);
        Assert.True(await db.SalaryItemCategories.AnyAsync(c => c.Id == cat.Id));
    }

    [Fact]
    public async Task DeleteItemCategory_Unused_Deletes()
    {
        await using var db = CreateDb(nameof(DeleteItemCategory_Unused_Deletes));
        var (_, cat, _) = await SeedSlipAsync(db);

        var handler = new DeleteSalaryItemCategoryCommandHandler(db);
        var (found, isProtected, inUse) = await handler.HandleAsync(new DeleteSalaryItemCategoryCommand(cat.Id));

        Assert.True(found);
        Assert.False(isProtected);
        Assert.False(inUse);
        Assert.False(await db.SalaryItemCategories.AnyAsync(c => c.Id == cat.Id));
    }

    [Fact]
    public async Task UpdateSlip_PersistsPdfAndCompensationFields()
    {
        await using var db = CreateDb(nameof(UpdateSlip_PersistsPdfAndCompensationFields));
        var (_, cat, slip) = await SeedSlipAsync(db);

        var handler = new UpdateSalarySlipCommandHandler(db);
        var (result, error) = await handler.HandleAsync(new UpdateSalarySlipCommand(
            slip.Id, new DateOnly(2026, 1, 1), 1100m, 880m, "edited",
            [new CreateLineItemRequest(cat.Id, 1100m, 0, null, null, null, null)],
            PdfPath: "abc.pdf", SourceFile: "jan.pdf",
            BaseAmount: 900m, HoursWorked: 160m, HourlyRate: 5.63m, TotalEspecie: 120m));

        Assert.Null(error);
        Assert.NotNull(result);

        var persisted = await db.SalarySlips.AsNoTracking().SingleAsync(s => s.Id == slip.Id);
        Assert.Equal("abc.pdf", persisted.PdfPath);
        Assert.Equal("jan.pdf", persisted.SourceFile);
        Assert.Equal(900m, persisted.BaseAmount);
        Assert.Equal(160m, persisted.HoursWorked);
        Assert.Equal(5.63m, persisted.HourlyRate);
        Assert.Equal(120m, persisted.TotalEspecie);
    }

    [Fact]
    public async Task UpdateSlip_RejectsCategoryFromAnotherProfile()
    {
        await using var db = CreateDb(nameof(UpdateSlip_RejectsCategoryFromAnotherProfile));
        var (_, _, slip) = await SeedSlipAsync(db);

        var otherProfile = new SalaryProfile { Name = "Other Corp" };
        db.SalaryProfiles.Add(otherProfile);
        await db.SaveChangesAsync();
        var foreignCat = new SalaryItemCategory
        {
            SalaryProfileId = otherProfile.Id, Name = "Foreign", Color = "#ef4444", ItemType = "deduction"
        };
        db.SalaryItemCategories.Add(foreignCat);
        await db.SaveChangesAsync();

        var handler = new UpdateSalarySlipCommandHandler(db);
        var (result, error) = await handler.HandleAsync(new UpdateSalarySlipCommand(
            slip.Id, new DateOnly(2026, 1, 1), 1000m, 800m, null,
            [new CreateLineItemRequest(foreignCat.Id, 100m, 0, null, null, null, null)]));

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("does not belong", error);
    }
}
