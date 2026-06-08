using Beacon.Api.Services.Parsing;
using Xunit;

namespace Beacon.Tests.Parsing;

public class ContinenteParserTests
{
    private static readonly ContinenteParser Parser = new();

    private static readonly string SamplePage =
        "BD Quinta Portas\n" +
        "Modelo Continente Hipermercados S.A.\n" +
        "Rua Joao Mendonca, 505 4464-503 Senhora da Hora\n" +
        "NIF: PT502011475\n" +
        "Fatura Simplificada Original\n" +
        "Nro:FS FIQ203/048287 10/05/2026 15:53 | NIF:PT999999990\n" +
        "IVA DESCRICAO VALOR\n" +
        "Mercearia Doce:\n" +
        "(C) CREPES DENT. C/ CHOC. LEITE CNT\n" +
        "2 X 2,29 4,58\n" +
        "Soft Drinks:\n" +
        "(C) REF.C/GAS C.COLA LATA SLEEK33CL 0,90\n" +
        "(C) ICE TEA FUZE TEA PEACH HIBISCUS 1,99\n" +
        "Taras e Valor de Deposito:\n" +
        "NS VALOR DE DEPOSITO UN\n" +
        "2 X 0,10 0,20\n" +
        "IVA Nao sujeito - Decreto-Lei n.o 152-D/2017, de 11/12\n" +
        "Congelados:\n" +
        "(A) PAO DE ALHO CNT BAGUETES 350G 1,59\n" +
        "Take Away:\n" +
        "(B) LASANHA BOLONHESA CNT 1KG 4,49\n" +
        "(C) FLAUTAS DE BACON E 2 QJ 220G 2,69\n" +
        "Casa-Cozinha/Lavand:\n" +
        "(C) SACO PLAS RE CNT 0,10\n" +
        "TOTAL A PAGAR 16,54\n" +
        "Cartao Credito 16,54\n" +
        "%IVA Total Liq. IVA Total\n" +
        "NS 0,20 0,00 0,20\n";

    [Theory]
    [InlineData("Modelo Continente Hipermercados S.A.\noutro texto")]
    [InlineData("texto\nModelo Continente\nmais texto")]
    public void CanParse_ReturnsTrueForContinenteReceipt(string text) =>
        Assert.True(Parser.CanParse(text));

    [Theory]
    [InlineData("Pingo Doce Lda\noutro texto")]
    [InlineData("")]
    public void CanParse_ReturnsFalseForOtherText(string text) =>
        Assert.False(Parser.CanParse(text));

    [Fact]
    public void Parse_ExtractsStoreName()
    {
        var result = Parser.Parse("receipt.pdf", [SamplePage]);
        Assert.Equal("Continente", result.StoreName);
    }

    [Fact]
    public void Parse_ExtractsDateFromNroLine()
    {
        var result = Parser.Parse("receipt.pdf", [SamplePage]);
        Assert.Equal(new DateOnly(2026, 5, 10), result.ReceiptDate);
    }

    [Fact]
    public void Parse_ExtractsTotal()
    {
        var result = Parser.Parse("receipt.pdf", [SamplePage]);
        Assert.Equal(16.54m, result.Total);
    }

    [Fact]
    public void Parse_ExtractsCorrectItemCount()
    {
        var result = Parser.Parse("receipt.pdf", [SamplePage]);
        Assert.Equal(8, result.Items.Count);
    }

    [Fact]
    public void Parse_HandlesMultiQuantityItem()
    {
        var result = Parser.Parse("receipt.pdf", [SamplePage]);
        var crepes = result.Items.Single(i => i.Description.Contains("CREPES"));
        Assert.Equal(2m, crepes.Quantity);
        Assert.Equal(4.58m, crepes.Amount);
    }

    [Fact]
    public void Parse_HandlesSingleQuantityItem()
    {
        var result = Parser.Parse("receipt.pdf", [SamplePage]);
        var cola = result.Items.Single(i => i.Description.Contains("C.COLA"));
        Assert.Equal(1m, cola.Quantity);
        Assert.Equal(0.90m, cola.Amount);
    }

    [Fact]
    public void Parse_HandlesNsDepositItem()
    {
        var result = Parser.Parse("receipt.pdf", [SamplePage]);
        var deposit = result.Items.Single(i => i.Description.Contains("VALOR DE DEPOSITO"));
        Assert.Equal(2m, deposit.Quantity);
        Assert.Equal(0.20m, deposit.Amount);
    }

    [Fact]
    public void Parse_AssignsReceiptCategoriesToItems()
    {
        var result = Parser.Parse("receipt.pdf", [SamplePage]);

        Assert.Equal("Mercearia Doce",          result.Items.Single(i => i.Description.Contains("CREPES")).ReceiptCategory);
        Assert.Equal("Soft Drinks",             result.Items.Single(i => i.Description.Contains("C.COLA")).ReceiptCategory);
        Assert.Equal("Soft Drinks",             result.Items.Single(i => i.Description.Contains("ICE TEA")).ReceiptCategory);
        Assert.Equal("Taras e Valor de Deposito", result.Items.Single(i => i.Description.Contains("VALOR DE DEPOSITO")).ReceiptCategory);
        Assert.Equal("Congelados",              result.Items.Single(i => i.Description.Contains("PAO DE ALHO")).ReceiptCategory);
        Assert.Equal("Take Away",               result.Items.Single(i => i.Description.Contains("LASANHA")).ReceiptCategory);
        Assert.Equal("Take Away",               result.Items.Single(i => i.Description.Contains("FLAUTAS")).ReceiptCategory);
        Assert.Equal("Casa-Cozinha/Lavand",     result.Items.Single(i => i.Description.Contains("SACO PLAS")).ReceiptCategory);
    }

    [Fact]
    public void Parse_FallsBackToTodayWhenNoDateFound()
    {
        var pageWithoutDate =
            "Modelo Continente Hipermercados S.A.\n" +
            "IVA DESCRICAO VALOR\n" +
            "TOTAL A PAGAR 5,00\n";

        var result = Parser.Parse("receipt.pdf", [pageWithoutDate]);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), result.ReceiptDate);
    }

    [Fact]
    public void Parse_ReturnsEmptyItemsWhenSectionMarkersAbsent()
    {
        var pageWithoutMarkers = "Modelo Continente Hipermercados S.A.\nNro:FS X 01/01/2026 10:00\n";
        var result = Parser.Parse("receipt.pdf", [pageWithoutMarkers]);
        Assert.Empty(result.Items);
    }
}
