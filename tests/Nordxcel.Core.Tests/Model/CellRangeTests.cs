using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Model;

public class CellRangeTests
{
    [Fact]
    public void Construtor_NormalizaOsCantos()
    {
        var range = new CellRange(CellAddress.Parse("C10"), CellAddress.Parse("A2"));

        Assert.Equal(CellAddress.Parse("A2"), range.Start);
        Assert.Equal(CellAddress.Parse("C10"), range.End);
        Assert.Equal("A2:C10", range.ToString());
    }

    [Fact]
    public void Dimensoes_SaoInclusivas()
    {
        CellRange range = CellRange.Parse("B2:D5");

        Assert.Equal(4, range.RowCount);
        Assert.Equal(3, range.ColumnCount);
        Assert.Equal(12L, range.CellCount);
        Assert.False(range.IsSingleCell);
    }

    [Fact]
    public void CellCount_NaoEstouraEmIntervalosEnormes()
    {
        var range = new CellRange(
            CellAddress.Origin,
            new CellAddress(CellAddress.MaxRows - 1, CellAddress.MaxColumns - 1));

        Assert.Equal(17_179_869_184L, range.CellCount);
    }

    [Fact]
    public void CelulaUnica_SeComportaComoIntervaloDeUmaCelula()
    {
        CellRange range = CellRange.Parse("B7");

        Assert.True(range.IsSingleCell);
        Assert.Equal(1L, range.CellCount);
        Assert.Equal("B7", range.ToString());
    }

    [Fact]
    public void Contains_TestaCelulaEIntervalo()
    {
        CellRange range = CellRange.Parse("B2:D5");

        Assert.True(range.Contains(CellAddress.Parse("C3")));
        Assert.True(range.Contains(CellAddress.Parse("B2")));
        Assert.True(range.Contains(CellAddress.Parse("D5")));
        Assert.False(range.Contains(CellAddress.Parse("A1")));
        Assert.False(range.Contains(CellAddress.Parse("E5")));

        Assert.True(range.Contains(CellRange.Parse("C3:D4")));
        Assert.False(range.Contains(CellRange.Parse("A1:C3")));
    }

    [Fact]
    public void Intersects_DetectaSobreposicao()
    {
        CellRange range = CellRange.Parse("B2:D5");

        Assert.True(range.Intersects(CellRange.Parse("D5:F9")));
        Assert.True(range.Intersects(CellRange.Parse("A1:Z100")));
        Assert.False(range.Intersects(CellRange.Parse("E1:F9")));
        Assert.False(range.Intersects(CellRange.Parse("B6:D9")));
    }

    [Fact]
    public void Addresses_PercorreEmVarreduraPorLinha()
    {
        string[] visited = CellRange.Parse("A1:B2").Addresses().Select(a => a.ToString()).ToArray();

        Assert.Equal(["A1", "B1", "A2", "B2"], visited);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A1:")]
    [InlineData(":B2")]
    [InlineData("A1:B")]
    [InlineData("$A$1:$B$2")]
    public void TryParse_RecusaIntervalosInvalidos(string text) =>
        Assert.False(CellRange.TryParse(text, out _));
}
