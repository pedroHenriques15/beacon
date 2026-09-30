using Beacon.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Tests;

/// <summary>
/// A private in-memory SQLite database with Beacon's schema, set up the way the API sets up its
/// own, that lives until the instance is disposed. Contexts from <see cref="CreateContext"/>
/// share it, so a test can read back through a fresh context what another one saved.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SqliteTestDatabase()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseBeaconSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}
