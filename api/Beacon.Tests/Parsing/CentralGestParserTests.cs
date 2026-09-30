using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class CentralGestParserTests
{
    private readonly CentralGestParser _parser = new();


    [Fact]
    public void CanParse_ReturnsTrueForCentralGestSignal()
    {
        Assert.True(_parser.CanParse("some text CentralGest Software more text"));
    }

    [Fact]
    public void CanParse_ReturnsFalseForUnrelatedText()
    {
        Assert.False(_parser.CanParse("DOMIREST RECIBO DE REMUNERAÇÕES Exemplo Contabilidade"));
    }


    [Fact]
    public void ParserName_IsCentralGest()
    {
        Assert.Equal("CentralGest", _parser.ParserName);
    }

    private static string BuildSamplePage(
        string employer = "EXAMPLE TECH - CONSULTORIA INFORMÁTICA S.A.",
        string nif = "999000002",
        string period = "março - 2026",
        string vencimento = "1 200,00",
        string ppr = "300,00",
        string tickets = "180,00",
        string segSocial = "132,00",
        string irs = "60,00",
        string gross = "1,680.00",
        string deductions = "192.00",
        string net = "1,488.00") => $"""
        {employer} {employer}
        4000-000 - Porto
        N.º Contribuinte: {nif}
        Original
        Recibo de Remuneração
        Mês: {period}
         127
        Programador Informático 11111111111 22222222222 1,200.00
        Vencimento {vencimento} 0.00 Vencimento {vencimento} 0.00
        PPR 1.00 300.00 {ppr} 0.00 PPR 1.00 300.00 {ppr} 0.00
        Tickets Refeição 20.00 9.00 {tickets} 11.00 0.00 Tickets Refeição 20.00 9.00 {tickets} 11.00 0.00
        Segurança Social {segSocial} 11.00 1,200.00 Segurança Social {segSocial} 11.00 1,200.00
        IRS {irs} 12.50 480.00 IRS {irs} 12.50 480.00
         {gross} {deductions} {net}
         180,00 1 308,00
        CentralGest Software - RECIBA5_DetIRS_090.RPT CentralGest Software - RECIBA5_DetIRS_090.RPT
        """;

    [Fact]
    public void Parse_GrossUnderOneThousand_Parses()
    {
        var result = _parser.Parse("slip.pdf",
            [BuildSamplePage(gross: "950.00", net: "820.00", deductions: "130.00")]);

        Assert.Equal(950.00m, result.GrossAmount);
        Assert.Equal(1308.00m, result.NetAmount);
    }

    [Fact]
    public void Parse_ExtractsEmployer()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal("EXAMPLE TECH - CONSULTORIA INFORMÁTICA S.A.", result.Employer);
    }

    [Fact]
    public void Parse_ExtractsEmployerNif()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal("999000002", result.EmployerNif);
    }

    [Fact]
    public void Parse_ExtractsPeriod_March2026()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal(new DateOnly(2026, 3, 1), result.Period);
    }

    [Theory]
    [InlineData("janeiro - 2025", 1, 2025)]
    [InlineData("fevereiro - 2025", 2, 2025)]
    [InlineData("abril - 2024", 4, 2024)]
    [InlineData("dezembro - 2023", 12, 2023)]
    public void Parse_ExtractsPeriod_AllMonths(string periodStr, int expectedMonth, int expectedYear)
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage(period: periodStr)]);

        Assert.Equal(expectedMonth, result.Period.Month);
        Assert.Equal(expectedYear, result.Period.Year);
        Assert.Equal(1, result.Period.Day);
    }

    [Fact]
    public void Parse_ExtractsGrossAmount()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal(1680.00m, result.GrossAmount);
    }

    [Fact]
    public void Parse_ExtractsNetAmount()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal(1308.00m, result.NetAmount);
    }

    [Fact]
    public void Parse_ExtractsVencimento_AsIncome()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var item = result.LineItems.First(i => i.Description == "Vencimento");

        Assert.Equal(1200.00m, item.Amount);
        Assert.Equal("income", item.ItemType);
    }

    [Fact]
    public void Parse_ExtractsPPR_AsIncome()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var item = result.LineItems.First(i => i.Description == "PPR – Poupança Reforma");

        Assert.Equal(300.00m, item.Amount);
        Assert.Equal("income", item.ItemType);
    }

    [Fact]
    public void Parse_ExtractsTicketsRefeicao_AsIncome()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var item = result.LineItems.First(i => i.Description == "Tickets Refeição");

        Assert.Equal(180.00m, item.Amount);
        Assert.Equal("income", item.ItemType);
    }

    [Fact]
    public void Parse_ExtractsSegurancaSocial_AsDeduction()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var item = result.LineItems.First(i => i.Description == "Segurança Social");

        Assert.Equal(132.00m, item.Amount);
        Assert.Equal("deduction", item.ItemType);
    }

    [Fact]
    public void Parse_ExtractsIRS_AsTax()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var item = result.LineItems.First(i => i.Description == "IRS");

        Assert.Equal(60.00m, item.Amount);
        Assert.Equal("tax", item.ItemType);
    }

    [Fact]
    public void Parse_ExtractsFiveLineItemsTotal()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal(5, result.LineItems.Count);
    }

    [Fact]
    public void Parse_VencimentoWithSpaceThousandsSeparator_ParsesCorrectly()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage(vencimento: "1 000,00")]);
        var item = result.LineItems.First(i => i.Description == "Vencimento");

        Assert.Equal(1000.00m, item.Amount);
    }

    [Fact]
    public void Parse_GrossWithUsThousandsSeparator_ParsesCorrectly()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage(gross: "2,345.60", net: "2,153.60")]);

        Assert.Equal(2345.60m, result.GrossAmount);
        Assert.Equal(1308.00m, result.NetAmount);
    }

    [Fact]
    public void Parse_NoPPRLine_OmitsPPRItem()
    {
        var page = """
            EMPRESA X S.A. EMPRESA X S.A.
            N.º Contribuinte: 123456789
            Mês: maio - 2025
            Vencimento 1 500,00 0.00 Vencimento 1 500,00 0.00
            Segurança Social 165,00 11.00 1,500.00 Segurança Social 165,00 11.00 1,500.00
            IRS 100,00 24.10 600.00 IRS 100,00 24.10 600.00
             1,865.00 265.00 1,600.00
             0,00 1 600,00
            CentralGest Software - RECIBA5.RPT
            """;

        var result = _parser.Parse("slip.pdf", [page]);

        Assert.DoesNotContain(result.LineItems, i => i.Description == "PPR – Poupança Reforma");
        Assert.DoesNotContain(result.LineItems, i => i.Description == "Tickets Refeição");
    }

    [Theory]
    [InlineData("1 000,00", 1000.00)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("132,00", 132.00)]
    [InlineData("60,00", 60.00)]
    public void ParsePt_ConvertsPortugueseDecimals(string input, double expected)
    {
        Assert.Equal((decimal)expected, CentralGestParser.ParsePt(input));
    }

    [Theory]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("12,345.67", 12345.67)]
    [InlineData("192.00", 192.00)]
    public void ParseUs_ConvertsUsFormatDecimals(string input, double expected)
    {
        Assert.Equal((decimal)expected, CentralGestParser.ParseUs(input));
    }
}
