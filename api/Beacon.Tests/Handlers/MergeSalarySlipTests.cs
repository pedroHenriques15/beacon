using Beacon.Api.Data;
using Beacon.Api.Features.Salary.Commands.CreateSalarySlip;
using Beacon.Api.Features.Salary.Commands.MergeSalarySlip;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Handlers;

/// <summary>
/// Merging a second pay run into an existing month — the micro1/Deel case, where a paycheck arrives
/// twice a calendar month but the schema allows one slip per (profile, period).
/// </summary>
public class MergeSalarySlipTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static MergeSalarySlipCommandHandler CreateHandler(AppDbContext db, string? storageRoot = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = storageRoot })
            .Build();
        return new MergeSalarySlipCommandHandler(
            db,
            new FileStorageService(config, NullLogger<FileStorageService>.Instance),
            NullLogger<MergeSalarySlipCommandHandler>.Instance);
    }

    private sealed record Seed(SalaryProfile Profile, SalaryItemCategory BasePay, SalaryItemCategory Fee, SalarySlip Slip);

    /// <summary>First half of July: gross 1337.85 / net 1328.49, base pay + Deel exchange fee.</summary>
    private static async Task<Seed> SeedFirstHalfAsync(AppDbContext db)
    {
        var profile = new SalaryProfile { Name = "Micro1 Inc." };
        db.SalaryProfiles.Add(profile);
        await db.SaveChangesAsync();

        var basePay = new SalaryItemCategory
        { SalaryProfileId = profile.Id, Name = "Base Pay", Color = "#22c55e", ItemType = "income" };
        var fee = new SalaryItemCategory
        { SalaryProfileId = profile.Id, Name = "Deel exchange fee", Color = "#ef4444", ItemType = "deduction" };
        db.SalaryItemCategories.AddRange(basePay, fee);
        await db.SaveChangesAsync();

        var slip = new SalarySlip
        {
            SalaryProfileId = profile.Id,
            Period          = new DateOnly(2026, 7, 1),
            GrossAmount     = 1337.85m,
            NetAmount       = 1328.49m,
            BaseAmount      = 1246.72m,
            HoursWorked     = 28.73m,
            HourlyRate      = 43.39m,
            SourceFile      = "invoice-jul1-15.pdf",
            LineItems =
            [
                new SalaryLineItem { SalaryItemCategoryId = basePay.Id, Amount = 1246.72m, SortOrder = 0 },
                new SalaryLineItem { SalaryItemCategoryId = fee.Id,     Amount = 9.36m,    SortOrder = 1 },
            ],
        };
        db.SalarySlips.Add(slip);
        await db.SaveChangesAsync();
        return new Seed(profile, basePay, fee, slip);
    }

    /// <summary>Second half of July: gross 361.47 / net 359.40.</summary>
    private static MergeSalarySlipCommand SecondHalf(Seed seed, int slipId) => new(
        slipId,
        GrossAmount: 361.47m,
        NetAmount: 359.40m,
        Notes: null,
        PdfPath: null,
        SourceFile: "invoice-jul16-31.pdf",
        LineItems:
        [
            new CreateLineItemRequest(seed.BasePay.Id, 361.47m, 0),
            new CreateLineItemRequest(seed.Fee.Id, 2.07m, 1),
        ],
        BaseAmount: 361.47m,
        HoursWorked: 8.33m,
        HourlyRate: 43.39m);

    [Fact]
    public async Task Merge_SumsGrossAndNet()
    {
        await using var db = CreateDb(nameof(Merge_SumsGrossAndNet));
        var seed = await SeedFirstHalfAsync(db);

        var (result, error) = await CreateHandler(db).HandleAsync(SecondHalf(seed, seed.Slip.Id));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(1699.32m, result.GrossAmount);
        Assert.Equal(1687.89m, result.NetAmount);
    }

    [Fact]
    public async Task Merge_DoesNotCreateASecondSlip()
    {
        await using var db = CreateDb(nameof(Merge_DoesNotCreateASecondSlip));
        var seed = await SeedFirstHalfAsync(db);

        await CreateHandler(db).HandleAsync(SecondHalf(seed, seed.Slip.Id));

        Assert.Equal(1, await db.SalarySlips.CountAsync());
    }

    [Fact]
    public async Task Merge_CombinesLineItemsSharingACategory()
    {
        await using var db = CreateDb(nameof(Merge_CombinesLineItemsSharingACategory));
        var seed = await SeedFirstHalfAsync(db);

        var (result, _) = await CreateHandler(db).HandleAsync(SecondHalf(seed, seed.Slip.Id));

        Assert.Equal(2, result!.LineItems.Count);
        Assert.Equal(1608.19m, result.LineItems.Single(li => li.SalaryItemCategoryId == seed.BasePay.Id).Amount);
        Assert.Equal(11.43m, result.LineItems.Single(li => li.SalaryItemCategoryId == seed.Fee.Id).Amount);
    }

    [Fact]
    public async Task Merge_AppendsLineItemForCategoryNotOnTheExistingSlip()
    {
        await using var db = CreateDb(nameof(Merge_AppendsLineItemForCategoryNotOnTheExistingSlip));
        var seed = await SeedFirstHalfAsync(db);

        var bonus = new SalaryItemCategory
        { SalaryProfileId = seed.Profile.Id, Name = "Other", Color = "#3b82f6", ItemType = "income" };
        db.SalaryItemCategories.Add(bonus);
        await db.SaveChangesAsync();

        var command = SecondHalf(seed, seed.Slip.Id) with
        {
            LineItems = [new CreateLineItemRequest(bonus.Id, 50m, 0)],
        };

        var (result, error) = await CreateHandler(db).HandleAsync(command);

        Assert.Null(error);
        Assert.Equal(3, result!.LineItems.Count);
        Assert.Equal(50m, result.LineItems.Single(li => li.SalaryItemCategoryId == bonus.Id).Amount);
        Assert.Equal(2, result.LineItems.Single(li => li.SalaryItemCategoryId == bonus.Id).SortOrder);
    }

    [Fact]
    public async Task Merge_SumsBaseAmountAndHours()
    {
        await using var db = CreateDb(nameof(Merge_SumsBaseAmountAndHours));
        var seed = await SeedFirstHalfAsync(db);

        var (result, _) = await CreateHandler(db).HandleAsync(SecondHalf(seed, seed.Slip.Id));

        Assert.Equal(1608.19m, result!.BaseAmount);
        Assert.Equal(37.06m, result.HoursWorked);
    }

    [Fact]
    public async Task Merge_WeightsHourlyRateByHours()
    {
        await using var db = CreateDb(nameof(Merge_WeightsHourlyRateByHours));
        var seed = await SeedFirstHalfAsync(db);

        // 28.73 h at 43.39 + 8.33 h at 40.00 -> (1246.60 + 333.20) / 37.06 = 42.63
        var command = SecondHalf(seed, seed.Slip.Id) with { HourlyRate = 40.00m };
        var (result, _) = await CreateHandler(db).HandleAsync(command);

        Assert.Equal(42.63m, result!.HourlyRate);
    }

    [Fact]
    public async Task Merge_KeepsExistingRate_WhenIncomingHasNone()
    {
        await using var db = CreateDb(nameof(Merge_KeepsExistingRate_WhenIncomingHasNone));
        var seed = await SeedFirstHalfAsync(db);

        var command = SecondHalf(seed, seed.Slip.Id) with { HourlyRate = null };
        var (result, _) = await CreateHandler(db).HandleAsync(command);

        Assert.Equal(43.39m, result!.HourlyRate);
    }

    [Fact]
    public async Task Merge_TakesIncomingValue_WhenExistingFieldIsNull()
    {
        await using var db = CreateDb(nameof(Merge_TakesIncomingValue_WhenExistingFieldIsNull));
        var seed = await SeedFirstHalfAsync(db);

        var slip = await db.SalarySlips.FirstAsync();
        slip.HoursWorked = null;
        slip.HourlyRate  = null;
        await db.SaveChangesAsync();

        var (result, _) = await CreateHandler(db).HandleAsync(SecondHalf(seed, seed.Slip.Id));

        Assert.Equal(8.33m, result!.HoursWorked);
        Assert.Equal(43.39m, result.HourlyRate);
    }

    [Fact]
    public async Task Merge_RecordsBothSourceFileNames()
    {
        await using var db = CreateDb(nameof(Merge_RecordsBothSourceFileNames));
        var seed = await SeedFirstHalfAsync(db);

        var (result, _) = await CreateHandler(db).HandleAsync(SecondHalf(seed, seed.Slip.Id));

        Assert.Equal("invoice-jul1-15.pdf; invoice-jul16-31.pdf", result!.SourceFile);
    }

    [Fact]
    public async Task Merge_DoesNotRepeatAnAlreadyRecordedSourceFile()
    {
        await using var db = CreateDb(nameof(Merge_DoesNotRepeatAnAlreadyRecordedSourceFile));
        var seed = await SeedFirstHalfAsync(db);

        var command = SecondHalf(seed, seed.Slip.Id) with { SourceFile = "invoice-jul1-15.pdf" };
        var (result, _) = await CreateHandler(db).HandleAsync(command);

        Assert.Equal("invoice-jul1-15.pdf", result!.SourceFile);
    }

    [Fact]
    public async Task Merge_KeepsFirstPdfAndDeletesTheSupersededOne()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"beacon_merge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            await using var db = CreateDb(nameof(Merge_KeepsFirstPdfAndDeletesTheSupersededOne));
            var seed = await SeedFirstHalfAsync(db);

            var firstPdf  = Path.Combine(storageRoot, "first.pdf");
            var secondPdf = Path.Combine(storageRoot, "second.pdf");
            await File.WriteAllTextAsync(firstPdf, "first");
            await File.WriteAllTextAsync(secondPdf, "second");

            var slip = await db.SalarySlips.FirstAsync();
            slip.PdfPath = firstPdf;
            await db.SaveChangesAsync();

            var command = SecondHalf(seed, seed.Slip.Id) with { PdfPath = secondPdf };
            var (result, _) = await CreateHandler(db, storageRoot).HandleAsync(command);

            Assert.Equal(firstPdf, result!.PdfPath);
            Assert.True(File.Exists(firstPdf));
            Assert.False(File.Exists(secondPdf));
        }
        finally
        {
            if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, true);
        }
    }

    [Fact]
    public async Task Merge_AdoptsIncomingPdf_WhenExistingSlipHasNone()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"beacon_merge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            await using var db = CreateDb(nameof(Merge_AdoptsIncomingPdf_WhenExistingSlipHasNone));
            var seed = await SeedFirstHalfAsync(db);

            var incomingPdf = Path.Combine(storageRoot, "incoming.pdf");
            await File.WriteAllTextAsync(incomingPdf, "incoming");

            var command = SecondHalf(seed, seed.Slip.Id) with { PdfPath = incomingPdf };
            var (result, _) = await CreateHandler(db, storageRoot).HandleAsync(command);

            Assert.Equal(incomingPdf, result!.PdfPath);
            Assert.True(File.Exists(incomingPdf));
        }
        finally
        {
            if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, true);
        }
    }

    [Fact]
    public async Task Merge_AppendsNotes()
    {
        await using var db = CreateDb(nameof(Merge_AppendsNotes));
        var seed = await SeedFirstHalfAsync(db);

        var slip = await db.SalarySlips.FirstAsync();
        slip.Notes = "first half";
        await db.SaveChangesAsync();

        var command = SecondHalf(seed, seed.Slip.Id) with { Notes = "second half" };
        var (result, _) = await CreateHandler(db).HandleAsync(command);

        Assert.Equal("first half\nsecond half", result!.Notes);
    }

    [Fact]
    public async Task Merge_ReturnsNotFoundResult_WhenSlipDoesNotExist()
    {
        await using var db = CreateDb(nameof(Merge_ReturnsNotFoundResult_WhenSlipDoesNotExist));
        var seed = await SeedFirstHalfAsync(db);

        var (result, error) = await CreateHandler(db).HandleAsync(SecondHalf(seed, 9999));

        Assert.Null(result);
        Assert.Null(error);
    }

    [Fact]
    public async Task Merge_RejectsCategoryFromAnotherProfile()
    {
        await using var db = CreateDb(nameof(Merge_RejectsCategoryFromAnotherProfile));
        var seed = await SeedFirstHalfAsync(db);

        var otherProfile = new SalaryProfile { Name = "Other Employer" };
        db.SalaryProfiles.Add(otherProfile);
        await db.SaveChangesAsync();
        var foreignCat = new SalaryItemCategory
        { SalaryProfileId = otherProfile.Id, Name = "Base Pay", Color = "#22c55e", ItemType = "income" };
        db.SalaryItemCategories.Add(foreignCat);
        await db.SaveChangesAsync();

        var command = SecondHalf(seed, seed.Slip.Id) with
        {
            LineItems = [new CreateLineItemRequest(foreignCat.Id, 100m, 0)],
        };

        var (result, error) = await CreateHandler(db).HandleAsync(command);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Equal(1337.85m, (await db.SalarySlips.FirstAsync()).GrossAmount);
    }

    [Fact]
    public async Task Merge_LeavesPeriodAndProfileUntouched()
    {
        await using var db = CreateDb(nameof(Merge_LeavesPeriodAndProfileUntouched));
        var seed = await SeedFirstHalfAsync(db);

        var (result, _) = await CreateHandler(db).HandleAsync(SecondHalf(seed, seed.Slip.Id));

        Assert.Equal(new DateOnly(2026, 7, 1), result!.Period);
        Assert.Equal(seed.Profile.Id, result.SalaryProfileId);
    }
}
