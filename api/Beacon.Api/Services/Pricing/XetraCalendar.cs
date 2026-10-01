namespace Beacon.Api.Services.Pricing;

/// <summary>
/// Xetra's trading days, which every priced listing follows (symbols end in ".DE"): weekdays
/// except New Year's Day, Good Friday, Easter Monday, Labour Day and 24, 25, 26 and 31 December.
/// The web client keeps a copy (<c>core/utils/xetra-calendar.ts</c>); change both together.
/// </summary>
public static class XetraCalendar
{
    public static bool IsTradingDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if ((date.Month, date.Day) is (1, 1) or (5, 1) or (12, 24) or (12, 25) or (12, 26) or (12, 31)) return false;

        var easter = EasterSunday(date.Year);
        return date != easter.AddDays(-2) && date != easter.AddDays(1);
    }

    /// <summary>The last trading day before <paramref name="date"/>.</summary>
    public static DateOnly PreviousTradingDay(DateOnly date)
    {
        var day = date.AddDays(-1);
        while (!IsTradingDay(day)) day = day.AddDays(-1);
        return day;
    }

    /// <summary>Easter Sunday in the Gregorian calendar (the anonymous Gregorian algorithm).</summary>
    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
