using Nordxcel.Core.Editing;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Tests.Editing;

public class RangeEditingTests
{
    private static CellAddress At(string address) => CellAddress.Parse(address);

    [Fact]
    public void Apply_MudaCadaCelulaDoIntervalo()
    {
        var sheet = new Worksheet("DCF");
        sheet.SetValue(At("A1"), CellValue.Number(1));
        sheet.SetValue(At("B1"), CellValue.Number(2));

        RangeEditing.Apply(sheet, CellRange.Parse("A1:B1"), cell => cell with { NumberFormat = "0.00" });

        Assert.Equal("0.00", sheet.GetCell(At("A1")).NumberFormat);
        Assert.Equal("0.00", sheet.GetCell(At("B1")).NumberFormat);
    }

    [Fact]
    public void Apply_MaterializaCelulaVaziaQuandoOIntervaloEhLimitado()
    {
        // Nem A1 nem B1 foram preenchidas, mas o intervalo é pequeno (não é
        // linha/coluna inteira), então aplicar negrito nelas é esperado, como no
        // Excel: as duas passam a existir só para guardar o estilo.
        var sheet = new Worksheet("DCF");

        RangeEditing.ApplyStyle(sheet, CellRange.Parse("A1:B1"), s => s with { Bold = true });

        Assert.True(sheet.GetCell(At("A1")).Style.Bold);
        Assert.True(sheet.GetCell(At("B1")).Style.Bold);
        Assert.Equal(2, sheet.CellCount);
    }

    [Fact]
    public void Apply_SelecaoDeColunaInteira_SoTocaCelulasComConteudo()
    {
        // Simula o clique no cabeçalho da coluna B: intervalo cobre a planilha
        // inteira no eixo das linhas.
        var sheet = new Worksheet("DCF");
        sheet.SetValue(At("B3"), CellValue.Number(10));
        sheet.SetValue(At("B900"), CellValue.Number(20));

        var wholeColumnB = new CellRange(new CellAddress(0, 1), new CellAddress(CellAddress.MaxRows - 1, 1));

        RangeEditing.ApplyStyle(sheet, wholeColumnB, s => s with { Bold = true });

        // Nem uma célula extra foi criada: só as duas que já existiam.
        Assert.Equal(2, sheet.CellCount);
        Assert.True(sheet.GetCell(At("B3")).Style.Bold);
        Assert.True(sheet.GetCell(At("B900")).Style.Bold);
    }

    [Fact]
    public void Apply_SelecaoDeLinhaInteira_SoTocaCelulasComConteudo()
    {
        var sheet = new Worksheet("DCF");
        sheet.SetValue(At("C5"), CellValue.Number(1));

        var wholeRow5 = new CellRange(new CellAddress(4, 0), new CellAddress(4, CellAddress.MaxColumns - 1));

        RangeEditing.ApplyStyle(sheet, wholeRow5, s => s with { Italic = true });

        Assert.Equal(1, sheet.CellCount);
        Assert.True(sheet.GetCell(At("C5")).Style.Italic);
    }

    [Fact]
    public void ApplyStyle_PreservaValorEFormula()
    {
        var sheet = new Worksheet("DCF");
        sheet.SetCell(At("A1"), Cell.FromFormula("SOMA(B1:B2)"));

        RangeEditing.ApplyStyle(sheet, CellRange.Parse("A1"), s => s with { Bold = true });

        Cell cell = sheet.GetCell(At("A1"));

        Assert.True(cell.HasFormula);
        Assert.Equal("SOMA(B1:B2)", cell.Formula);
        Assert.True(cell.Style.Bold);
    }

    [Fact]
    public void ApplyNumberFormat_TrocaSoAMascara()
    {
        var sheet = new Worksheet("DCF");
        sheet.SetCell(At("A1"), new Cell { Value = CellValue.Number(1234), Style = CellStyle.Default with { Bold = true } });

        RangeEditing.ApplyNumberFormat(sheet, CellRange.Parse("A1"), StandardNumberFormats.Thousands);

        Cell cell = sheet.GetCell(At("A1"));

        Assert.Equal(StandardNumberFormats.Thousands, cell.NumberFormat);
        Assert.True(cell.Style.Bold);
    }

    [Fact]
    public void ApplyNumberFormat_NuloVoltaAoFormatoGeral()
    {
        var sheet = new Worksheet("DCF");
        sheet.SetCell(At("A1"), new Cell { Value = CellValue.Number(1234), NumberFormat = StandardNumberFormats.Percent });

        RangeEditing.ApplyNumberFormat(sheet, CellRange.Parse("A1"), null);

        Assert.Null(sheet.GetCell(At("A1")).NumberFormat);
    }

    [Fact]
    public void Apply_NaoGravaQuandoATransformacaoNaoMudaNada()
    {
        // Célula em branco que já é o padrão: aplicar o mesmo estilo padrão não
        // deve materializar uma entrada nova no dicionário esparso.
        var sheet = new Worksheet("DCF");

        RangeEditing.ApplyStyle(sheet, CellRange.Parse("A1"), s => s);

        Assert.Equal(0, sheet.CellCount);
    }
}
