using Nordxcel.Core.Layout;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Layout;

public class SheetSelectionTests
{
    private static CellAddress At(string address) => CellAddress.Parse(address);

    [Fact]
    public void ComecaEmA1()
    {
        var selection = new SheetSelection();

        Assert.Equal(CellAddress.Origin, selection.Active);
        Assert.True(selection.IsSingleCell);
        Assert.Equal("A1", selection.ToReferenceText());
    }

    [Fact]
    public void MoveTo_ColapsaASelecao()
    {
        var selection = new SheetSelection();
        selection.ExtendTo(At("D10"));

        selection.MoveTo(At("B2"));

        Assert.Equal(At("B2"), selection.Active);
        Assert.Equal(At("B2"), selection.Focus);
        Assert.True(selection.IsSingleCell);
    }

    [Fact]
    public void ExtendTo_NaoMoveACelulaAtiva()
    {
        // O que digitar continua indo para B2 mesmo com B2:D10 selecionado.
        var selection = new SheetSelection();
        selection.MoveTo(At("B2"));

        selection.ExtendTo(At("D10"));

        Assert.Equal(At("B2"), selection.Active);
        Assert.Equal(CellRange.Parse("B2:D10"), selection.Range);
        Assert.Equal("B2:D10", selection.ToReferenceText());
    }

    [Fact]
    public void ExtendTo_ParaTrasNormalizaOIntervalo()
    {
        var selection = new SheetSelection();
        selection.MoveTo(At("D10"));
        selection.ExtendTo(At("B2"));

        Assert.Equal(At("D10"), selection.Active);
        Assert.Equal(CellRange.Parse("B2:D10"), selection.Range);
    }

    [Fact]
    public void Contains_TestaOIntervaloInteiro()
    {
        var selection = new SheetSelection();
        selection.MoveTo(At("B2"));
        selection.ExtendTo(At("D10"));

        Assert.True(selection.Contains(At("C5")));
        Assert.False(selection.Contains(At("E5")));
    }

    [Fact]
    public void SelectColumns_PegaAColunaInteiraMasDigitaNaPrimeiraLinha()
    {
        var selection = new SheetSelection();

        selection.SelectColumns(1, 3);

        Assert.Equal(At("B1"), selection.Active);
        Assert.True(selection.IsWholeColumns);
        Assert.False(selection.IsWholeRows);
        Assert.Equal(CellAddress.MaxRows - 1, selection.Range.End.Row);
    }

    [Fact]
    public void SelectRows_PegaALinhaInteiraMasDigitaNaPrimeiraColuna()
    {
        var selection = new SheetSelection();

        selection.SelectRows(4, 4);

        Assert.Equal(At("A5"), selection.Active);
        Assert.True(selection.IsWholeRows);
        Assert.Equal(CellAddress.MaxColumns - 1, selection.Range.End.Column);
    }

    [Fact]
    public void SelectAll_PegaAPlanilhaToda()
    {
        var selection = new SheetSelection();

        selection.SelectAll();

        Assert.True(selection.IsWholeColumns);
        Assert.True(selection.IsWholeRows);
        Assert.Equal(CellAddress.Origin, selection.Active);
    }
}
