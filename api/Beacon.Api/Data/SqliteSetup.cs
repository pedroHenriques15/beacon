using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Beacon.Api.Data;

/// <summary>
/// How Beacon opens its SQLite database. The API, the tests and the tools in scripts/ all go
/// through here, so every connection behaves the same.
/// </summary>
public static class SqliteSetup
{
    /// <summary>
    /// The collation for text shown in order: case ignored, accented letters beside their base
    /// letter ("Água" among the A's), as SQL Server sorted it. Queries apply it in ORDER BY with
    /// <c>EF.Functions.Collate</c>. The schema keeps the built-in NOCASE, so a change in .NET's
    /// sort rules can reorder a list but never invalidate an index.
    /// </summary>
    public const string DisplayOrder = "DISPLAY_ORDER";

    private static readonly StringComparer DisplayOrderComparer =
        StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);

    public static TBuilder UseBeaconSqlite<TBuilder>(this TBuilder options, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        options.UseSqlite(connectionString).AddInterceptors(ConnectionInterceptor.Instance);
        return options;
    }

    /// <summary>For a connection the caller opens and keeps open, such as an in-memory database.</summary>
    public static TBuilder UseBeaconSqlite<TBuilder>(this TBuilder options, SqliteConnection connection)
        where TBuilder : DbContextOptionsBuilder
    {
        // EF does not open a connection that is already open, so the interceptor would not run.
        ConfigureConnection(connection);
        options.UseSqlite(connection).AddInterceptors(ConnectionInterceptor.Instance);
        return options;
    }

    /// <summary>
    /// Creates the folder that holds the database file: SQLite creates a missing file but not a
    /// missing folder.
    /// </summary>
    public static void EnsureDatabaseFolder(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.Mode == SqliteOpenMode.Memory || builder.DataSource is "" or ":memory:")
            return;

        var folder = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
    }

    /// <summary>
    /// Registers <see cref="DisplayOrder"/>, and replaces SQLite's lower() and upper(), which fold
    /// ASCII letters only, with .NET's, so a case-insensitive search also matches accented letters
    /// ("serviços" finds "SERVIÇOS"). EF translates <c>ToLower()</c> and <c>ToUpper()</c> in
    /// queries to these functions.
    /// </summary>
    public static void ConfigureConnection(SqliteConnection connection)
    {
        connection.CreateFunction("lower", (string? value) => value?.ToLowerInvariant(), isDeterministic: true);
        connection.CreateFunction("upper", (string? value) => value?.ToUpperInvariant(), isDeterministic: true);
        connection.CreateCollation(DisplayOrder, DisplayOrderComparer.Compare);
    }

    private sealed class ConnectionInterceptor : DbConnectionInterceptor
    {
        public static readonly ConnectionInterceptor Instance = new();

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
            ConfigureConnection((SqliteConnection)connection);

        public override Task ConnectionOpenedAsync(
            DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            ConfigureConnection((SqliteConnection)connection);
            return Task.CompletedTask;
        }
    }
}
