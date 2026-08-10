using System.Globalization;

namespace Beacon.Api.Services.Parsing;

/// <summary>Shared numeric helpers for the micro1/Deel two-PDF flow (US-format amounts, cent rounding).</summary>
internal static class AmountUtils
{
    public static decimal ParseUsd(string s) =>
        decimal.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);

    public static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
