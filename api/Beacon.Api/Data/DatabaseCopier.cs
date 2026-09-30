using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Data;

/// <summary>
/// Copies every table of one Beacon database into another, keeping ids, then compares the two
/// row by row and column by column. scripts/MigrateToSqlite uses it to move a SQL Server
/// database to SQLite; both sides go through EF, so values land in the form the app writes.
/// </summary>
public static class DatabaseCopier
{
    public sealed record TableResult(string Table, int SourceRows, int TargetRows, IReadOnlyList<string> Differences);

    /// <summary>Parents before children, so every foreign key finds its row.</summary>
    private static readonly ITable[] Tables =
    [
        new Table<Category>(),
        new Table<CategoryRule>(),
        new Table<MonthlyStatement>(),
        new Table<Transaction>(),
        new Table<SalaryProfile>(),
        new Table<SalaryItemCategory>(),
        new Table<SalarySlip>(),
        new Table<SalaryLineItem>(),
        new Table<GroceryCategory>(),
        new Table<GroceryCategoryRule>(),
        new Table<GroceryReceiptCategoryMapping>(),
        new Table<GroceryReceipt>(),
        new Table<GroceryItem>(),
        new Table<InvestmentAsset>(),
        new Table<InvestmentLot>(),
        new Table<InvestmentPriceSnapshot>(),
        new Table<GoogleOAuthToken>(),
    ];

    /// <summary>Copies every row into an empty <paramref name="target"/>, in one transaction.</summary>
    public static async Task CopyAsync(AppDbContext source, AppDbContext target, CancellationToken ct = default)
    {
        var unlisted = target.Model.GetEntityTypes().Select(e => e.ClrType)
            .Except(Tables.Select(t => t.EntityType)).ToList();
        if (unlisted.Count > 0)
            throw new InvalidOperationException(
                $"DatabaseCopier does not copy {string.Join(", ", unlisted.Select(t => t.Name))}; add it to Tables.");

        await using (var transaction = await target.Database.BeginTransactionAsync(ct))
        {
            foreach (var table in Tables)
                await table.CopyAsync(source, target, ct);
            await transaction.CommitAsync(ct);
        }
    }

    /// <summary>Compares every table of the two databases, row by row and column by column.</summary>
    public static async Task<IReadOnlyList<TableResult>> CompareAsync(
        AppDbContext source, AppDbContext target, CancellationToken ct = default)
    {
        var results = new List<TableResult>();
        foreach (var table in Tables)
            results.Add(await table.CompareAsync(source, target, ct));
        return results;
    }

    private interface ITable
    {
        Type EntityType { get; }
        Task CopyAsync(AppDbContext source, AppDbContext target, CancellationToken ct);
        Task<TableResult> CompareAsync(AppDbContext source, AppDbContext target, CancellationToken ct);
    }

    private sealed class Table<T> : ITable where T : class
    {
        public Type EntityType => typeof(T);

        public async Task CopyAsync(AppDbContext source, AppDbContext target, CancellationToken ct)
        {
            // Untracked rows carry no loaded navigations, so each row is inserted on its own,
            // with its id.
            var rows = await source.Set<T>().AsNoTracking().ToListAsync(ct);
            target.Set<T>().AddRange(rows);
            await target.SaveChangesAsync(ct);
            target.ChangeTracker.Clear();
        }

        public async Task<TableResult> CompareAsync(AppDbContext source, AppDbContext target, CancellationToken ct)
        {
            var entityType = target.Model.FindEntityType(typeof(T))!;
            var key = entityType.FindPrimaryKey()!.Properties.Single().PropertyInfo!;
            var columns = entityType.GetProperties().Where(p => p.PropertyInfo is not null).ToList();

            var sourceRows = (await source.Set<T>().AsNoTracking().ToListAsync(ct)).ToDictionary(r => key.GetValue(r)!);
            var targetRows = (await target.Set<T>().AsNoTracking().ToListAsync(ct)).ToDictionary(r => key.GetValue(r)!);

            var differences = new List<string>();
            foreach (var (id, sourceRow) in sourceRows)
            {
                if (!targetRows.TryGetValue(id, out var targetRow))
                {
                    differences.Add($"id {id} is missing");
                    continue;
                }

                // Equals compares decimals by value, so 12.50 and 12.5 are the same amount.
                foreach (var column in columns)
                {
                    var expected = column.PropertyInfo!.GetValue(sourceRow);
                    var actual = column.PropertyInfo.GetValue(targetRow);
                    if (!Equals(expected, actual))
                        differences.Add(FormattableString.Invariant($"id {id}, {column.Name}: '{expected}' became '{actual}'"));
                }
            }

            foreach (var id in targetRows.Keys.Except(sourceRows.Keys))
                differences.Add($"id {id} is not in the source");

            return new TableResult(entityType.GetTableName()!, sourceRows.Count, targetRows.Count, differences);
        }
    }
}
