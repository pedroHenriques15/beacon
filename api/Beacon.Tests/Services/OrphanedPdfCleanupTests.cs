using Beacon.Api.Data;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beacon.Tests.Services;

public class OrphanedPdfCleanupTests : IDisposable
{
    private static readonly TimeSpan Old = TimeSpan.FromDays(3);

    private readonly string _storageRoot;

    public OrphanedPdfCleanupTests()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), $"beacon_cleanup_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_storageRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

    private static AppDbContext CreateDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private OrphanedPdfCleanup CreateCleanup(AppDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = _storageRoot })
            .Build();
        return new OrphanedPdfCleanup(db, config, NullLogger<OrphanedPdfCleanup>.Instance);
    }

    // A PDF as the upload flows store it, <guid>.pdf in the storage root, last written `age` ago.
    private string StoredPdf(TimeSpan age)
    {
        var path = Path.Combine(_storageRoot, $"{Guid.NewGuid()}.pdf");
        File.WriteAllBytes(path, "pdf"u8.ToArray());
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    private static void AddStatement(AppDbContext db, string pdfPath) =>
        db.MonthlyStatements.Add(new MonthlyStatement { Bank = "ActivoBank", SourceFile = "statement.pdf", PdfPath = pdfPath });

    private static void AddSalarySlip(AppDbContext db, string pdfPath)
    {
        var profile = new SalaryProfile { Name = "Employer" };
        db.SalaryProfiles.Add(profile);
        db.SalarySlips.Add(new SalarySlip { SalaryProfile = profile, Period = new DateOnly(2026, 1, 1), PdfPath = pdfPath });
    }

    private static void AddGroceryReceipt(AppDbContext db, string pdfPath) =>
        db.GroceryReceipts.Add(new GroceryReceipt { StoreName = "Continente", ReceiptDate = new DateOnly(2026, 1, 10), PdfPath = pdfPath });

    [Fact]
    public async Task FileReferencedByRelativePath_Survives()
    {
        await using var db = CreateDb(nameof(FileReferencedByRelativePath_Survives));
        var file = StoredPdf(Old);
        AddStatement(db, Path.GetFileName(file));
        await db.SaveChangesAsync();

        await CreateCleanup(db).RunAsync();

        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task FileReferencedByAbsolutePathUnderRoot_Survives()
    {
        await using var db = CreateDb(nameof(FileReferencedByAbsolutePathUnderRoot_Survives));
        var file = StoredPdf(Old);
        AddSalarySlip(db, file);
        await db.SaveChangesAsync();

        await CreateCleanup(db).RunAsync();

        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task FileReferencedByAnotherMachinesLinuxPath_Survives()
    {
        await using var db = CreateDb(nameof(FileReferencedByAnotherMachinesLinuxPath_Survives));
        var file = StoredPdf(Old);
        AddGroceryReceipt(db, $"/workspaces/beacon/local/uploads/{Path.GetFileName(file)}");
        await db.SaveChangesAsync();

        await CreateCleanup(db).RunAsync();

        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task FileReferencedByAnotherMachinesWindowsPath_Survives()
    {
        await using var db = CreateDb(nameof(FileReferencedByAnotherMachinesWindowsPath_Survives));
        var file = StoredPdf(Old);
        AddStatement(db, $@"C:\beacon\local\uploads\{Path.GetFileName(file)}");
        await db.SaveChangesAsync();

        await CreateCleanup(db).RunAsync();

        Assert.True(File.Exists(file));
    }

    // A referenced file shows the cleanup that the folder belongs to this database.
    private async Task AddReferencedPdfAsync(AppDbContext db)
    {
        AddStatement(db, Path.GetFileName(StoredPdf(Old)));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UnreferencedFileOlderThanMinimumAge_IsDeleted()
    {
        await using var db = CreateDb(nameof(UnreferencedFileOlderThanMinimumAge_IsDeleted));
        await AddReferencedPdfAsync(db);
        var file = StoredPdf(OrphanedPdfCleanup.MinimumAge + TimeSpan.FromMinutes(5));

        var deleted = await CreateCleanup(db).RunAsync();

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task UnreferencedFileNewerThanMinimumAge_Survives()
    {
        await using var db = CreateDb(nameof(UnreferencedFileNewerThanMinimumAge_Survives));
        await AddReferencedPdfAsync(db);
        var file = StoredPdf(TimeSpan.FromHours(1));

        var deleted = await CreateCleanup(db).RunAsync();

        Assert.Equal(0, deleted);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task DatabaseReferencingNoFiles_DeletesNothing()
    {
        // The demo database, whose rows have no PdfPath, started against real uploads.
        await using var db = CreateDb(nameof(DatabaseReferencingNoFiles_DeletesNothing));
        var first = StoredPdf(Old);
        var second = StoredPdf(Old);

        var deleted = await CreateCleanup(db).RunAsync();

        Assert.Equal(0, deleted);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task DatabaseReferencingNoneOfTheFolder_DeletesNothing()
    {
        // A database restored without its files, pointed at another folder of uploads.
        await using var db = CreateDb(nameof(DatabaseReferencingNoneOfTheFolder_DeletesNothing));
        AddStatement(db, "3f2b6c1e-0000-4000-8000-000000000001.pdf");
        await db.SaveChangesAsync();
        var file = StoredPdf(Old);

        var deleted = await CreateCleanup(db).RunAsync();

        Assert.Equal(0, deleted);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task MixedFolder_DeletesOnlyOldUnreferencedFiles()
    {
        await using var db = CreateDb(nameof(MixedFolder_DeletesOnlyOldUnreferencedFiles));
        var statementPdf = StoredPdf(Old);
        var slipPdf = StoredPdf(Old);
        var receiptPdf = StoredPdf(Old);
        var orphan = StoredPdf(Old);
        var inProgress = StoredPdf(TimeSpan.FromMinutes(1));
        AddStatement(db, Path.GetFileName(statementPdf));
        AddSalarySlip(db, slipPdf);
        AddGroceryReceipt(db, $"/workspaces/beacon/local/uploads/{Path.GetFileName(receiptPdf)}");
        await db.SaveChangesAsync();

        var deleted = await CreateCleanup(db).RunAsync();

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(orphan));
        Assert.All([statementPdf, slipPdf, receiptPdf, inProgress], f => Assert.True(File.Exists(f)));
    }
}
