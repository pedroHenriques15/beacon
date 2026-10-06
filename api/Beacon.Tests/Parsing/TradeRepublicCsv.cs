namespace Beacon.Tests.Parsing;

/// <summary>
/// Builds synthetic Trade Republic transaction exports in the real file's shape: every field
/// quoted, the real header. Merchants, ids and amounts are invented.
/// </summary>
internal static class TradeRepublicCsv
{
    public const string Header =
        "\"datetime\",\"date\",\"account_type\",\"category\",\"type\",\"asset_class\",\"name\",\"symbol\"," +
        "\"shares\",\"price\",\"amount\",\"fee\",\"tax\",\"currency\",\"original_amount\",\"original_currency\"," +
        "\"fx_rate\",\"description\",\"transaction_id\",\"counterparty_name\",\"counterparty_iban\"," +
        "\"payment_reference\",\"mcc_code\"";

    public const string Isin = "IE00BK5BQT80";

    public static string Cash(
        string date, string type, string amount, string description,
        string time = "10:00:00", string fee = "", string tax = "", string currency = "EUR",
        string accountType = "DEFAULT", string category = "CASH", string id = "") =>
        Row(date, time, accountType, category, type, "", "", "", "", "", amount, fee, tax, currency, description, id);

    public static string Buy(
        string date, string amount, string shares, string price, string id,
        string fee = "", string tax = "", string time = "15:00:00", string assetClass = "FUND",
        string description = $"Savings plan execution {Isin} Example World Fund, quantity: 0.500000") =>
        Row(date, time, "DEFAULT", "TRADING", "BUY", assetClass, "Example World ETF", Isin,
            shares, price, amount, fee, tax, "EUR", description, id);

    public static string File(params string[] rows) => string.Join("\n", [Header, .. rows]) + "\n";

    private static string Row(
        string date, string time, string accountType, string category, string type, string assetClass,
        string name, string symbol, string shares, string price, string amount, string fee, string tax,
        string currency, string description, string id)
    {
        string[] fields =
        [
            $"{date}T{time}.123456Z", date, accountType, category, type, assetClass, name, symbol,
            shares, price, amount, fee, tax, currency, "", "", "", description, id, "", "", "", "",
        ];
        return string.Join(",", fields.Select(f => $"\"{f.Replace("\"", "\"\"")}\""));
    }
}
