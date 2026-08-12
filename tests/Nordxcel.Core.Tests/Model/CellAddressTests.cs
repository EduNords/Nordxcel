using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Model;

public class CellAddressTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    [InlineData(16_383, "XFD")]
    public void ColumnToName_ConverteIndiceEmLetras(int column, string expected) =>
        Assert.Equal(expected, CellAddress.ColumnToName(column));

    [Theory]
    [InlineData("A", 0)]
    [InlineData("z", 25)]
    [InlineData("AA", 26)]
    [InlineData("XFD", 16_383)]
    public void TryParseColumnName_AceitaLetrasValidas(string name, int expected)
    {
        Assert.True(CellAddress.TryParseColumnName(name, out int column));
        Assert.Equal(expected, column);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XFE")]     // uma coluna além do limite
    [InlineData("AAAA")]    // mais de três letras
    [InlineData("A1")]
    public void TryParseColumnName_RecusaLetrasInvalidas(string name) =>
        Assert.False(CellAddress.TryParseColumnName(name, out _));

    [Fact]
    public void ColumnToName_EhInversoDeTryParseColumnName()
    {
        for (int column = 0; column < CellAddress.MaxColumns; column += 37)
        {
            string name = CellAddress.ColumnToName(column);

            Assert.True(CellAddress.TryParseColumnName(name, out int roundTripped));
            Assert.Equal(column, roundTripped);
        }
    }

    [Theory]
    [InlineData("A1", 0, 0)]
    [InlineData("B5", 4, 1)]
    [InlineData("aa10", 9, 26)]
    [InlineData("XFD1048576", 1_048_575, 16_383)]
    public void Parse_InterpretaNotacaoA1(string text, int row, int column)
    {
        CellAddress address = CellAddress.Parse(text);

        Assert.Equal(row, address.Row);
        Assert.Equal(column, address.Column);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]           // sem linha
    [InlineData("1")]           // sem coluna
    [InlineData("A0")]          // linha base um começa em 1
    [InlineData("A1048577")]    // uma linha além do limite
    [InlineData("$A$1")]        // o cifrão é responsabilidade de CellReference
    [InlineData("A1:B2")]
    [InlineData("A 1")]
    public void TryParse_RecusaEnderecosInvalidos(string text) =>
        Assert.False(CellAddress.TryParse(text, out _));

    [Fact]
    public void ToString_UsaNotacaoA1BaseUm() =>
        Assert.Equal("C7", new CellAddress(6, 2).ToString());

    [Fact]
    public void Construtor_RecusaCoordenadasForaDaPlanilha()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellAddress(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellAddress(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellAddress(CellAddress.MaxRows, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellAddress(0, CellAddress.MaxColumns));
    }

    [Fact]
    public void TryOffset_DevolveFalsoQuandoSaiDaPlanilha()
    {
        Assert.False(CellAddress.Origin.TryOffset(-1, 0, out _));
        Assert.False(CellAddress.Origin.TryOffset(0, -1, out _));
        Assert.False(CellAddress.Origin.TryOffset(CellAddress.MaxRows, 0, out _));

        Assert.True(CellAddress.Origin.TryOffset(3, 2, out CellAddress moved));
        Assert.Equal(CellAddress.Parse("C4"), moved);
    }

    [Fact]
    public void CompareTo_OrdenaPorLinhaEDepoisPorColuna()
    {
        List<CellAddress> ordered =
        [
            CellAddress.Parse("B10"),
            CellAddress.Parse("A1"),
            CellAddress.Parse("C1"),
            CellAddress.Parse("B1"),
        ];

        ordered.Sort();

        Assert.Equal(["A1", "B1", "C1", "B10"], ordered.Select(a => a.ToString()));
    }
}
