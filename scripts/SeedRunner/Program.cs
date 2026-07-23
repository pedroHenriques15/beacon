using Microsoft.Data.SqlClient;

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

using var conn = new SqlConnection(connStr);

conn.InfoMessage += (_, e) => Console.WriteLine(e.Message);

conn.Open();
Console.WriteLine($"Connected → {conn.Database} on {conn.DataSource}");

using var cmd = conn.CreateCommand();
cmd.CommandText = sql;
cmd.CommandTimeout = 120;
cmd.ExecuteNonQuery();

return 0;
