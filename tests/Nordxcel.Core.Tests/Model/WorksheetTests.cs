using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Tests.Model;

public class WorksheetTests
{
    private static readonly CellAddress B2 = CellAddress.Parse("B2");

    [Fact]
    public void GetCell_DevolveCelulaVaziaQuandoNaoHaConteudo()
    {
        var sheet = new Worksheet("Premissas");

        Assert.Same(Cell.Empty, sheet.GetCell(B2));
        Assert.True(sheet.GetValue(B2).IsBlank);
        Assert.Equal(0, sheet.CellCount);
    }

    [Fact]
    public void SetCell_ArmazenaDeFormaEsparsa()
    {
        var sheet = new Worksheet("Premissas");

        sheet.SetCell(B2, Cell.FromNumber(0.12));

        Assert.Equal(1, sheet.CellCount);
        Assert.Equal(CellValue.Number(0.12), sheet.GetValue(B2));
    }

    [Fact]
    public void SetCell_NaoGuardaCelulaVazia()
    {
        var sheet = new Worksheet("Premissas");

        sheet.SetCell(B2, Cell.FromNumber(1));
        sheet.SetCell(B2, Cell.Empty);

        Assert.Equal(0, sheet.CellCount);
    }

    [Fact]
    public void SetValue_PreservaFormatoEEstilo()
    {
        var sheet = new Worksheet("Premissas");
        var estilizada = new Cell
        {
            Value = CellValue.Number(1),
            NumberFormat = "#,##0;(#,##0)",
            Style = CellStyle.Default with { Bold = true },
        };

        sheet.SetCell(B2, estilizada);
        sheet.SetValue(B2, CellValue.Number(2));

        Cell atualizada = sheet.GetCell(B2);

        Assert.Equal(CellValue.Number(2), atualizada.Value);
        Assert.Equal("#,##0;(#,##0)", atualizada.NumberFormat);
        Assert.True(atualizada.Style.Bold);
    }

    [Fact]
    public void SetValue_LimpaAFormulaAnterior()
    {
        var sheet = new Worksheet("Premissas");

        sheet.SetCell(B2, Cell.FromFormula("SOMA(A1:A3)"));
        sheet.SetValue(B2, CellValue.Number(10));

        Assert.False(sheet.GetCell(B2).HasFormula);
    }

    [Fact]
    public void CelulaComApenasFormatoNaoEhDescartada()
    {
        // Uma linha do template DCF pode estar formatada e ainda vazia de conteúdo.
        var sheet = new Worksheet("DCF");

        sheet.SetCell(B2, new Cell { NumberFormat = "0,0x" });

        Assert.Equal(1, sheet.CellCount);
    }

    [Fact]
    public void GetUsedRange_DelimitaAsCelulasPreenchidas()
    {
        var sheet = new Worksheet("DCF");

        Assert.Null(sheet.GetUsedRange());

        sheet.SetCell(CellAddress.Parse("C3"), Cell.FromNumber(1));
        sheet.SetCell(CellAddress.Parse("A10"), Cell.FromNumber(2));
        sheet.SetCell(CellAddress.Parse("E1"), Cell.FromNumber(3));

        Assert.Equal("A1:E10", sheet.GetUsedRange()!.Value.ToString());
    }

    [Fact]
    public void LarguraEAltura_CaemNoPadraoQuandoNaoDefinidas()
    {
        var sheet = new Worksheet("DCF");

        Assert.Equal(Worksheet.DefaultColumnWidth, sheet.GetColumnWidth(0));
        Assert.Equal(Worksheet.DefaultRowHeight, sheet.GetRowHeight(0));

        sheet.SetColumnWidth(0, 220);
        sheet.SetRowHeight(0, 32);

        Assert.Equal(220, sheet.GetColumnWidth(0));
        Assert.Equal(32, sheet.GetRowHeight(0));
        Assert.Equal(Worksheet.DefaultColumnWidth, sheet.GetColumnWidth(1));
    }

    [Fact]
    public void SetColumnWidth_RecusaValoresNaoPositivos()
    {
        var sheet = new Worksheet("DCF");

        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.SetColumnWidth(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.SetRowHeight(0, -1));
    }

    [Fact]
    public void PaineisCongelados_ComecamEmZeroEAceitamValoresPositivos()
    {
        var sheet = new Worksheet("DCF") { FrozenRows = 4, FrozenColumns = 2 };

        Assert.Equal(4, sheet.FrozenRows);
        Assert.Equal(2, sheet.FrozenColumns);
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.FrozenRows = -1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nome muito comprido para uma aba do Excel")]
    [InlineData("Fluxo/Caixa")]
    [InlineData("Prem[issas]")]
    [InlineData("'Premissas")]
    public void Construtor_RecusaNomesInvalidos(string name) =>
        Assert.ThrowsAny<ArgumentException>(() => new Worksheet(name));
}
