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

// The SQL file writes plain literals: SQLite stores an amount such as 1250 as the text "1250"
// and keeps a date-time as written, while EF writes and compares "1250.0" and
// "2025-04-12 00:00:00". Rewrite every decimal and date-time column the way EF would.
var parsers = new Dictionary<Type, Func<string, object>>
{
    [typeof(decimal)] = text => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
    [typeof(DateTime)] = text => DateTime.Parse(text, CultureInfo.InvariantCulture),
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
            read.CommandText = $"SELECT rowid, \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL";
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
