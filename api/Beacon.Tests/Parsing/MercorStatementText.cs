namespace Beacon.Tests.Parsing;

/// <summary>
/// Builds synthetic Mercor "Line Item Statement" text in the shape pdfplumber gives the real one:
/// a summary, then hourly lines in no date order, with a doubled "$$" before the rate and the amount.
/// The name, times and amounts are invented.
/// </summary>
internal static class MercorStatementText
{
    /// <summary>Three lines at $40/h summing to $145.17; HOURS WORKED is cut to the minute, so the
    /// $0.07 line shows 00:00. By amount ÷ rate the hours are 3.62925.</summary>
    public static readonly string[] DefaultLines =
    [
        "August 21, 2026, 2:00PM August 21, 2026, 4:30PM $$40.00 02:30 $$100.00",
        "August 5, 2026, 9:10AM August 5, 2026, 10:17AM $$40.00 01:07 $$45.10",
        "August 14, 2026, 6:01PM August 14, 2026, 6:01PM $$40.00 00:00 $$0.07",
    ];

    public static string Page(
        string start = "August 1, 2026",
        string end = "August 31, 2026",
        string shiftPay = "145.17",
        string totalPay = "145.17",
        params string[] lines) => $"""
        Mercor Line Item Statement
        Statement Period: {start} to {end}
        Summary
        Name Example Contractor
        Payment Period Start {start}
        Payment Period End {end}
        Total Shift Pay ${shiftPay}
        Total Pay ${totalPay}
        Hourly Payments
        START TIME END TIME PAY RATE ($/HR) HOURS WORKED AMOUNT PAID
        {string.Join("\n", lines.Length > 0 ? lines : DefaultLines)}
        This document is a breakdown of payment for this contract and pay period by line item.
        """;
}
