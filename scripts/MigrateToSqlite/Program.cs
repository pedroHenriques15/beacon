using Beacon.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Moves a Beacon SQL Server database into a new SQLite file: every table, every id, then a
// row-by-row comparison of the two. The SQL Server database is only read.
//
//   dotnet run --project scripts/MigrateToSqlite -- "<SQL Server connection string>" "<new .db file>"

// The last SQL Server migration Beacon shipped. The copy reads the source through the current
// model, so the source must have exactly that schema.
const string LastSqlServerMigration = "20260930114508_StorePdfPathsAsFileNames";

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MigrateToSqlite <SQL Server connection string> <path of the new SQLite file>");
    return 1;
}

var targetPath = Path.GetFullPath(args[1]);
if (File.Exists(targetPath))
{
    Console.Error.WriteLine($"{targetPath} already exists. Choose a new file; nothing is ever overwritten.");
    return 1;
}

await using var source = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(args[0]).Options);
var lastApplied = (await source.Database.GetAppliedMigrationsAsync()).LastOrDefault();
if (lastApplied != LastSqlServerMigration)
{
    Console.Error.WriteLine(
        $"The source database's last migration is '{lastApplied ?? "(none)"}', not '{LastSqlServerMigration}'. " +
        "Update it with the last SQL Server release of Beacon first.");
    return 1;
}

var targetConnection = $"Data Source={targetPath}";
SqliteSetup.EnsureDatabaseFolder(targetConnection);
var succeeded = false;
try
{
    await using (var target = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseBeaconSqlite(targetConnection).Options))
    {
        await target.Database.MigrateAsync();
        await DatabaseCopier.CopyAsync(source, target);
        var tables = await DatabaseCopier.CompareAsync(source, target);

        Console.WriteLine($"Copied into {targetPath}:");
        foreach (var table in tables)
        {
            var verdict = table.Differences.Count == 0 && table.SourceRows == table.TargetRows
                ? "identical"
                : $"{table.Differences.Count} difference(s)";
            Console.WriteLine($"  {table.Table,-32} {table.SourceRows,6} rows  {verdict}");
            foreach (var difference in table.Differences.Take(20))
                Console.WriteLine($"      {difference}");
        }

        succeeded = tables.All(t => t.Differences.Count == 0 && t.SourceRows == t.TargetRows);
        Console.WriteLine(succeeded
            ? $"All {tables.Count} tables identical ({tables.Sum(t => t.SourceRows)} rows)."
            : "The copy differs from the source; the new file is removed.");
    }
    return succeeded ? 0 : 1;
}
finally
{
    // Pooled connections keep the file open (and its WAL unmerged) until they are cleared.
    SqliteConnection.ClearAllPools();
    if (!succeeded)
    {
        foreach (var file in new[] { targetPath, targetPath + "-wal", targetPath + "-shm" })
            File.Delete(file);
    }
}
