using Beacon.Api.Services.Pricing;

namespace Beacon.Tests.Services;

public class XetraCalendarTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(2024, "2024-03-31")]
    [InlineData(2025, "2025-04-20")]
    [InlineData(2026, "2026-04-05")]
    [InlineData(2027, "2027-03-28")]
    public void EasterSunday_IsTheGregorianOne(int year, string expected) =>
        Assert.Equal(D(expected), XetraCalendar.EasterSunday(year));

    [Theory]
    [InlineData("2026-01-01", false)] // New Year's Day
    [InlineData("2026-04-03", false)] // Good Friday
    [InlineData("2026-04-06", false)] // Easter Monday
    [InlineData("2026-05-01", false)] // Labour Day
    [InlineData("2026-12-24", false)]
    [InlineData("2026-12-25", false)]
    [InlineData("2026-12-31", false)]
    [InlineData("2025-12-26", false)]
    [InlineData("2026-09-26", false)] // Saturday
    [InlineData("2026-05-25", true)]  // Whit Monday: Xetra trades
    [InlineData("2026-10-05", true)]  // the Monday after German Unity Day
    [InlineData("2026-09-30", true)]
    public void TradingDays_SkipWeekendsAndXetraHolidays(string date, bool trading) =>
        Assert.Equal(trading, XetraCalendar.IsTradingDay(D(date)));

    [Theory]
    // An ordinary week: on Wednesday, Monday's close is fine and Friday's is stale.
    [InlineData("2026-07-27", "2026-07-29", false)]
    [InlineData("2026-07-24", "2026-07-29", true)]
    // Monday morning: Thursday's close is fine, Wednesday's is stale.
    [InlineData("2026-07-23", "2026-07-27", false)]
    [InlineData("2026-07-22", "2026-07-27", true)]
    // Easter: on the Tuesday after, Thursday's close is the latest to expect, and Wednesday's
    // is one missed sync behind.
    [InlineData("2026-04-02", "2026-04-07", false)]
    [InlineData("2026-04-01", "2026-04-07", false)]
    [InlineData("2026-03-31", "2026-04-07", true)]
    // Christmas 2025: on Monday the 29th, the 23rd's close is the latest to expect.
    [InlineData("2025-12-23", "2025-12-29", false)]
    [InlineData("2025-12-22", "2025-12-29", false)]
    [InlineData("2025-12-19", "2025-12-29", true)]
    public void Prices_AreStale_MoreThanOneTradingDayBehind(string latest, string today, bool stale) =>
        Assert.Equal(stale, PriceSyncSettings.IsStale(D(latest), D(today)));

    [Fact]
    public void Prices_AreStale_WhenThereAreNone() =>
        Assert.True(PriceSyncSettings.IsStale(null, D("2026-07-29")));
}
