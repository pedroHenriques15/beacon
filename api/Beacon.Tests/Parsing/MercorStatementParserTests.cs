using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class MercorStatementParserTests
{
    private readonly MercorStatementParser _parser = new();

    [Fact]
    public void CanParse_ByTheStatementTitle()
    {
        Assert.True(_parser.CanParse(MercorStatementText.Page()));
    }

    [Fact]
    public void CanParse_NotABankLineThatNamesMercor()
    {
        Assert.False(_parser.CanParse("Revolut Bank UAB\n14 Aug 2026 Carregamento de MERCOR.IO CORPORATION €88.20"));
    }

    [Fact]
    public void Parse_ReadsTheMonthFromTheStatementPeriod()
    {
        var statement = _parser.Parse([MercorStatementText.Page()]);
        Assert.Equal(new DateOnly(2026, 8, 1), statement.Period);
    }

    [Fact]
    public void Parse_ReadsDoubledDollarAmounts_InAnyLineOrder()
    {
        var statement = _parser.Parse([MercorStatementText.Page()]);

        Assert.Equal(145.17m, statement.TotalPayUsd);
        Assert.Equal(145.17m, statement.ShiftPayUsd);
        Assert.Equal(40.00m, statement.PayRateUsd);
    }

    [Fact]
    public void Parse_TakesHoursFromAmountOverRate_NotTheMinuteColumn()
    {
        // 100/40 + 45.10/40 + 0.07/40 = 3.62925 h; the column says 02:30 + 01:07 + 00:00 = 3.62 h.
        var statement = _parser.Parse([MercorStatementText.Page()]);
        Assert.Equal(3.63m, statement.HoursWorked);
    }

    [Fact]
    public void Parse_MixedRates_GiveTheirAverageByHours()
    {
        var statement = _parser.Parse([MercorStatementText.Page(
            shiftPay: "190.00", totalPay: "190.00",
            lines:
            [
                "August 3, 2026, 9:00AM August 3, 2026, 11:30AM $$40.00 02:30 $$100.00",
                "August 4, 2026, 9:00AM August 4, 2026, 11:00AM $$45.00 02:00 $$90.00",
            ])]);

        Assert.Equal(4.50m, statement.HoursWorked);
        Assert.Equal(42.22m, statement.PayRateUsd);
    }

    [Fact]
    public void Parse_KeepsPayBeyondTheHourlyLines()
    {
        var statement = _parser.Parse([MercorStatementText.Page(totalPay: "160.17")]);

        Assert.Equal(160.17m, statement.TotalPayUsd);
        Assert.Equal(145.17m, statement.ShiftPayUsd);
    }

    [Fact]
    public void Parse_RefusesLinesThatDoNotSumToShiftPay_NamingBoth()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _parser.Parse([MercorStatementText.Page(shiftPay: "150.00", totalPay: "150.00")]));

        Assert.Contains("$145.17", ex.Message);
        Assert.Contains("$150.00", ex.Message);
    }

    [Fact]
    public void Parse_RefusesTotalPayBelowShiftPay()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _parser.Parse([MercorStatementText.Page(totalPay: "140.00")]));

        Assert.Contains("less than its Total Shift Pay", ex.Message);
    }

    [Fact]
    public void Parse_RefusesAStatementWithoutHourlyLines()
    {
        var text = MercorStatementText.Page().Replace("$$", "#");

        var ex = Assert.Throws<InvalidOperationException>(() => _parser.Parse([text]));
        Assert.Contains("hourly payment", ex.Message);
    }

    [Fact]
    public void Parse_RefusesAStatementWithoutItsPeriod()
    {
        var text = MercorStatementText.Page().Replace("Statement Period:", "Period");

        var ex = Assert.Throws<InvalidOperationException>(() => _parser.Parse([text]));
        Assert.Contains("Statement Period", ex.Message);
    }
}
