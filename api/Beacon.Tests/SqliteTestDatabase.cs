using Beacon.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Tests;

/// <summary>
/// A private in-memory SQLite database with Beacon's schema, set up the way the API sets up its
/// own, that lives until the instance is disposed. Contexts from <see cref="CreateContext"/>
/// share it, so a test can read back through a fresh context what another one saved. A test
/// class holds one in a field: xUnit creates the class, and so the database, for every test.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SqliteTestDatabase()
    {
        _connection.Open();
        Options = new DbContextOptionsBuilder<AppDbContext>().UseBeaconSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public DbContextOptions<AppDbContext> Options { get; }

    public AppDbContext CreateContext() => new(Options);

    public void Dispose() => _connection.Dispose();
}
