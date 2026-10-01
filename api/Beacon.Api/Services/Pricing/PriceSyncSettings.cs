namespace Beacon.Api.Services.Pricing;

/// <summary>The <c>Prices</c> configuration section, with its defaults.</summary>
public static class PriceSyncSettings
{
    public static bool Enabled(IConfiguration configuration) =>
        configuration.GetValue("Prices:Enabled", true);

    public static int HistoryYears(IConfiguration configuration) =>
        Math.Max(1, configuration.GetValue("Prices:HistoryYears", 15));

    public static TimeOnly DailyRunTime(IConfiguration configuration) =>
        TimeOnly.TryParse(configuration["Prices:DailyRunTime"], System.Globalization.CultureInfo.InvariantCulture, out var time)
            ? time
            : new TimeOnly(22, 0);

    /// <summary>Gold is priced in EUR per gram through an EUR-listed physical gold ETC (ADR-011).</summary>
    public static string GoldProxySymbol(IConfiguration configuration) =>
        configuration["Prices:GoldProxySymbol"] is { Length: > 0 } symbol ? symbol : "4GLD.DE";

    public static decimal JumpWarningPercent(IConfiguration configuration) =>
        configuration.GetValue("Prices:JumpWarningPercent", 20m);

    /// <summary>
    /// True when the latest price is missing or more than one trading day behind: a day's close is
    /// synced that evening, so before then the newest close to expect is the previous trading
    /// day's, and one missed sync is allowed on top. Weekends and Xetra holidays don't count.
    /// </summary>
    public static bool IsStale(DateOnly? latestPrice, DateOnly today) =>
        latestPrice is null
        || latestPrice < XetraCalendar.PreviousTradingDay(XetraCalendar.PreviousTradingDay(today));
}
