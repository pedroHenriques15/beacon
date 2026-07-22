using Beacon.Api.Data;
using Beacon.Api.Features.Salary.Commands.CreateSalaryItemCategory;
using Beacon.Api.Features.Salary.Commands.CreateSalarySlip;
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
