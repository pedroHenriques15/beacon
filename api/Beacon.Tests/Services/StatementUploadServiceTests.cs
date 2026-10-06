using Beacon.Api.Data;
using Beacon.Api.Features.Shared;
using Beacon.Api.Models;
using Beacon.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests.Services;

public class StatementUploadServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private AppDbContext CreateDb() => _database.CreateContext();

    private static MonthlyStatement MakeBpiStatement(
        DateOnly periodFrom, DateOnly periodTo, decimal pprBalance, Transaction? synthetic = null)
    {
        return new MonthlyStatement
        {
            Bank = "BPI",
            Account = "PT50",
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            PprBalance = pprBalance,
            Transactions = synthetic is null ? [] : [synthetic],
        };
    }

    private static Transaction MakeSynthetic(decimal amount, string type, decimal balance) => new()
    {
        DatePosting = new DateOnly(2026, 3, 31),
        DateValue = new DateOnly(2026, 3, 31),
        Description = "BPI Reforma - Ganhos",
        Amount = amount,
        Type = type,
        Balance = balance,
    };

    [Fact]
    public async Task RecomputeNextPprSynthetic_Backfill_UpdatesNextStatementsSynthetic()
    {
        await using var db = CreateDb();

        // Audit scenario: Jan (1000) then Mar (1300, synthetic 300 vs Jan). Backfilling
        // Feb (1100) must shrink Mar's synthetic gain to 200 - not leave 300 forever.
        db.MonthlyStatements.Add(MakeBpiStatement(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 1000m));
        db.MonthlyStatements.Add(MakeBpiStatement(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 1300m,
            MakeSynthetic(300m, "credit", 1300m)));
        await db.SaveChangesAsync();

        var recomputed = await StatementUploadService.RecomputeNextPprSyntheticAsync(
            db, [], null, new DateOnly(2026, 2, 1), 1100m);

        Assert.Equal(new DateOnly(2026, 3, 1), recomputed);
        var synthetic = await db.Transactions.SingleAsync(t => t.Description == "BPI Reforma - Ganhos");
        Assert.Equal(200m, synthetic.Amount);
        Assert.Equal("credit", synthetic.Type);
    }

    [Fact]
    public async Task RecomputeNextPprSynthetic_ZeroDelta_RemovesSynthetic()
    {
        await using var db = CreateDb();

        db.MonthlyStatements.Add(MakeBpiStatement(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 1100m,
            MakeSynthetic(100m, "credit", 1100m)));
        await db.SaveChangesAsync();

        var recomputed = await StatementUploadService.RecomputeNextPprSyntheticAsync(
            db, [], null, new DateOnly(2026, 2, 1), 1100m);

        Assert.Equal(new DateOnly(2026, 3, 1), recomputed);
        Assert.False(await db.Transactions.AnyAsync(t => t.Description == "BPI Reforma - Ganhos"));
    }

    [Fact]
    public async Task RecomputeNextPprSynthetic_MissingSynthetic_CreatesIt()
    {
        await using var db = CreateDb();

        // Next statement never got a synthetic (it was the first upload) - a lower
        // backfilled baseline reveals a loss that must now be recorded.
        db.MonthlyStatements.Add(MakeBpiStatement(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 900m));
        await db.SaveChangesAsync();

        var recomputed = await StatementUploadService.RecomputeNextPprSyntheticAsync(
            db, [], null, new DateOnly(2026, 2, 1), 1000m);

        Assert.Equal(new DateOnly(2026, 3, 1), recomputed);
        var synthetic = await db.Transactions.SingleAsync(t => t.Description == "BPI Reforma - Ganhos");
        Assert.Equal(100m, synthetic.Amount);
        Assert.Equal("debit", synthetic.Type);
    }

    [Fact]
    public async Task RecomputeNextPprSynthetic_MissingSynthetic_RuleToExcluded_CreatesItExcluded()
    {
        await using var db = CreateDb();

        var excluded = new Category { Name = ExcludedCategory.Name, Color = "#64748b", IsProtected = true };
        db.Categories.Add(excluded);
        db.MonthlyStatements.Add(MakeBpiStatement(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 900m));
        await db.SaveChangesAsync();
        var rule = new CategoryRule { CategoryId = excluded.Id, Pattern = "BPI Reforma" };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync();

        await StatementUploadService.RecomputeNextPprSyntheticAsync(
            db, [rule], excluded.Id, new DateOnly(2026, 2, 1), 1000m);

        await using var freshDb = CreateDb();
        var synthetic = await freshDb.Transactions.SingleAsync(t => t.Description == "BPI Reforma - Ganhos");
        Assert.Equal(excluded.Id, synthetic.CategoryId);
        Assert.True(synthetic.IsExcluded);
    }

    [Fact]
    public async Task RecomputeNextPprSynthetic_ZeroDelta_UserTouchedSynthetic_IsPreserved()
    {
        await using var db = CreateDb();

        var touched = MakeSynthetic(100m, "credit", 1100m);
        touched.CategorySetManually = true;
        db.MonthlyStatements.Add(MakeBpiStatement(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 1100m, touched));
        await db.SaveChangesAsync();

        var recomputed = await StatementUploadService.RecomputeNextPprSyntheticAsync(
            db, [], null, new DateOnly(2026, 2, 1), 1100m);

        // User-touched rows are never silently destroyed.
        Assert.Null(recomputed);
        Assert.True(await db.Transactions.AnyAsync(t => t.Description == "BPI Reforma - Ganhos"));
    }

    [Fact]
    public async Task RecomputeNextPprSynthetic_NoLaterStatement_DoesNothing()
    {
        await using var db = CreateDb();

        db.MonthlyStatements.Add(MakeBpiStatement(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 1000m));
        await db.SaveChangesAsync();

        var recomputed = await StatementUploadService.RecomputeNextPprSyntheticAsync(
            db, [], null, new DateOnly(2026, 2, 1), 1100m);

        Assert.Null(recomputed);
    }
}
