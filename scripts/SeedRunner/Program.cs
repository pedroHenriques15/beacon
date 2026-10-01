using System.Globalization;
using Beacon.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: SeedRunner <connectionString> <sqlFile>");
    return 1;
}

var connStr = args[0];
var sqlFile = args[1];

if (!File.Exists(sqlFile))
{
    Console.Error.WriteLine($"SQL file not found: {sqlFile}");
    return 1;
}

var sql = File.ReadAllText(sqlFile);

using var conn = new SqliteConnection(connStr);
conn.Open();
Console.WriteLine($"Connected → {conn.DataSource}");

using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = sql;
    cmd.CommandTimeout = 120;
    cmd.ExecuteNonQuery();
}

// The seed's newest month is March 2026. Move every date by whole months so that this month
// becomes the current one, and the dashboard and "this month" always have data.
var months = MonthsToCurrent(conn);
var now = DateTime.UtcNow;
now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
Console.WriteLine($"Dates moved by {months} month(s)");

// The SQL file writes plain literals: SQLite stores an amount such as 1250 as the text "1250"
// and keeps a date-time as written, while EF writes and compares "1250.0" and
// "2025-04-12 00:00:00". Rewrite every decimal and date column the way EF would, moving the dates
// on the way. A timestamp (when something was imported) is never left in the future; a date
// (a transaction, a statement period) may fall later in the current month.
var parsers = new Dictionary<Type, Func<string, object>>
{
    [typeof(decimal)] = text => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
    [typeof(DateTime)] = text =>
    {
        var moved = ShiftMonths(DateTime.Parse(text, CultureInfo.InvariantCulture), months);
        return moved > now ? now : moved;
    },
    [typeof(DateOnly)] = text => DateOnly.FromDateTime(
        ShiftMonths(DateOnly.Parse(text, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue), months)),
};
using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseBeaconSqlite(conn).Options);
var rewritten = 0;
foreach (var entityType in db.Model.GetEntityTypes())
{
    var table = entityType.GetTableName();
    foreach (var property in entityType.GetProperties())
    {
        if (!parsers.TryGetValue(Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType, out var parse))
            continue;

        var column = property.GetColumnName();
        var values = new List<(long RowId, object Value)>();
        using (var read = conn.CreateCommand())
        {
            // Newest first when moving forward, so a moved date never lands on one not yet moved
            // (statement periods and price snapshot dates are unique).
            read.CommandText = $"SELECT rowid, \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL " +
                $"ORDER BY \"{column}\" {(months >= 0 ? "DESC" : "ASC")}";
            using var reader = read.ExecuteReader();
            while (reader.Read())
                values.Add((reader.GetInt64(0), parse(reader.GetString(1))));
        }

        using var write = conn.CreateCommand();
        write.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = $value WHERE rowid = $rowid";
        // Bound as a decimal or DateTime, the value is written in the same text form EF writes.
        var valueParameter = write.Parameters.Add(new SqliteParameter { ParameterName = "$value" });
        var rowIdParameter = write.Parameters.Add("$rowid", SqliteType.Integer);
        foreach (var (rowId, value) in values)
        {
            valueParameter.Value = value;
            rowIdParameter.Value = rowId;
            rewritten += write.ExecuteNonQuery();
        }
    }
}

Console.WriteLine($"Values rewritten in EF's form: {rewritten}");
return 0;

// Months from the newest statement period to today's month.
static int MonthsToCurrent(SqliteConnection conn)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT max(\"PeriodTo\") FROM \"MonthlyStatements\"";
    if (cmd.ExecuteScalar() is not string newestText)
        return 0;

    var newest = DateOnly.Parse(newestText, CultureInfo.InvariantCulture);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    return (today.Year - newest.Year) * 12 + today.Month - newest.Month;
}

// AddMonths, except that the last day of a month stays the last day of a month, so a statement
// period (1 February - 28 February) still covers its whole month once moved.
static DateTime ShiftMonths(DateTime value, int months)
{
    var moved = value.AddMonths(months);
    if (value.Day != DateTime.DaysInMonth(value.Year, value.Month))
        return moved;

    return moved.AddDays(DateTime.DaysInMonth(moved.Year, moved.Month) - moved.Day);
}
