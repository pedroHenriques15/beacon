using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class DomirestParserTests
{
    private readonly DomirestParser _parser = new();

    [Fact]
    public void CanParse_ReturnsTrueForDomirestSignal()
    {
        Assert.True(_parser.CanParse("some text DOMIREST more text"));
    }

    [Fact]
    public void CanParse_ReturnsFalseForUnrelatedText()
    {
        Assert.False(_parser.CanParse("CentralGest Software Recibo de Remuneração"));
    }

    [Fact]
    public void ParserName_IsDomirest()
    {
        Assert.Equal("Domirest", _parser.ParserName);
    }

    private static string BuildSamplePage(
        string employer   = "DOMIREST - RESTAURAÇÃO, LDA",
        string nif        = "999000001",
        string date       = "31-03-2026",
        string gross      = "436,55",
        string net        = "394,22",
        string deductions = "42,33",
        string incomeLines = """
            1 Remuner. Normal 68,70 5,31 364,80
            45 Premio Produtividade 1,00 20,00 20,00
            72 Sub.Kms/Deslocaç 115,00 0,45 51,75
            """,
        string ssLine     = "Segurança Social 11,00% 384,80 42,33",
        string irsLine    = "Acumulados para Irs: - Incidência: 434,18 - Retenção: 0,00")
    {
        var section = $"""
            {employer} RECIBO DE REMUNERAÇÕES
            Rua Exemplo, Nº 100 Valores em EUR ORIGINAL
            4000 - 001 Porto NIF: {nif}
            DATA MÊS ESTABEL. SECÇÃO CATEGORIA NOME
            {date} Março / 26 0 Distribuidor
            Nº INTERNO REMUNERAÇÃO BASE Nº SEG. SOCIAL Nº CONTRIBUINTE
            1001 345,15 11111111111 22222222222
            CÓD. REMUNERAÇÕES TEMPOS VALOR UNITÁRIO VALOR REMUNERAÇÃO
            {incomeLines}
            AUSÊNCIAS DESCONTOS INCIDÊNCIAS VALOR DO DESCONTO
            {ssLine}
            Dec. Lei 98/2009 : Exemplo Seguros, S.A. - 200000001 {irsLine}
            VALOR ILÍQUIDO DESCONTOS FORMA DE PAGAMENTO VALOR LÍQUIDO A RECEBER
             {gross} {deductions} Transferência Bancária {net}
            Nº CONTA Declaro que me foi entregue cópia deste recibo conforme Dec.-Lei 7/2009 de 12/02
            Documento processado por computador
            Exemplo Contabilidade, Lda.
            """;

        return section + "\n" + section.Replace("ORIGINAL", "DUPLICADO");
    }

    [Fact]
    public void Parse_ExtractsEmployer()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal("DOMIREST - RESTAURAÇÃO, LDA", result.Employer);
    }

    [Fact]
    public void Parse_ExtractsEmployerNif()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal("999000001", result.EmployerNif);
    }

    [Fact]
    public void Parse_ExtractsPeriod_March2026()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal(new DateOnly(2026, 3, 1), result.Period);
    }

    [Theory]
    [InlineData("15-01-2025", 2025, 1)]
    [InlineData("28-02-2024", 2024, 2)]
    [InlineData("30-11-2023", 2023, 11)]
    [InlineData("31-12-2026", 2026, 12)]
    public void Parse_ExtractsPeriod_VariousDates(string dateStr, int expectedYear, int expectedMonth)
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage(date: dateStr)]);

        Assert.Equal(expectedYear,  result.Period.Year);
        Assert.Equal(expectedMonth, result.Period.Month);
        Assert.Equal(1, result.Period.Day);
    }

    [Fact]
    public void Parse_ExtractsGrossAmount()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal(436.55m, result.GrossAmount);
    }

    [Fact]
    public void Parse_ExtractsNetAmount()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal(394.22m, result.NetAmount);
    }

    [Fact]
    public void Parse_ExtractsThreeIncomeLineItems()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var incomeItems = result.LineItems.Where(i => i.ItemType == "income").ToList();

        Assert.Equal(3, incomeItems.Count);
    }

    [Fact]
    public void Parse_ExtractsRemunerNormal_AsIncome()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var item = result.LineItems.First(i => i.Description == "Remuneração Normal");

        Assert.Equal(364.80m, item.Amount);
        Assert.Equal("income", item.ItemType);
    }

    [Fact]
    public void Parse_ExtractsPremioProductividade_AsIncome()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var item = result.LineItems.First(i => i.Description == "Prémio de Produtividade");

        Assert.Equal(20.00m, item.Amount);
        Assert.Equal("income", item.ItemType);
    }

    [Fact]
    public void Parse_ExtractsSubKmsDeslocac_AsIncome()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var item = result.LineItems.First(i => i.Description == "Subsídio de Deslocação (Km)");

        Assert.Equal(51.75m, item.Amount);
        Assert.Equal("income", item.ItemType);
    }

    [Fact]
    public void Parse_ExtractsSegurancaSocial_AsDeduction()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);
        var item = result.LineItems.First(i => i.Description == "Segurança Social");

        Assert.Equal(42.33m, item.Amount);
        Assert.Equal("deduction", item.ItemType);
    }

    [Fact]
    public void Parse_ZeroIrsRetention_DoesNotAddIrsItem()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.DoesNotContain(result.LineItems, i => i.Description == "IRS");
    }

    [Fact]
    public void Parse_NonZeroIrsRetention_AddsIrsItem()
    {
        var page = BuildSamplePage(
            irsLine: "Acumulados para Irs: - Incidência: 1.000,00 - Retenção: 50,00");

        var result = _parser.Parse("slip.pdf", [page]);
        var item = result.LineItems.First(i => i.Description == "IRS");

        Assert.Equal(50.00m, item.Amount);
        Assert.Equal("tax", item.ItemType);
    }

    [Fact]
    public void Parse_DuplicateContent_ExtractsEachIncomeItemOnce()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        var descriptions = result.LineItems.Select(i => i.Description).ToList();
        Assert.Equal(descriptions.Distinct().Count(), descriptions.Count);
    }

    [Fact]
    public void Parse_DuplicateContent_CorrectTotalItemCount()
    {
        var result = _parser.Parse("slip.pdf", [BuildSamplePage()]);

        Assert.Equal(4, result.LineItems.Count);
    }

    [Theory]
    [InlineData("364,80", 364.80)]
    [InlineData("42,33",  42.33)]
    [InlineData("51,75",  51.75)]
    [InlineData("436,55", 436.55)]
    [InlineData("394,22", 394.22)]
    public void ParsePt_ConvertsPortugueseDecimals(string input, double expected)
    {
        Assert.Equal((decimal)expected, DomirestParser.ParsePt(input));
    }
}
