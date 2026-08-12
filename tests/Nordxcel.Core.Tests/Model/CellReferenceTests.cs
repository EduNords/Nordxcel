using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Model;

public class CellReferenceTests
{
    [Theory]
    [InlineData("A1", false, false)]
    [InlineData("$A1", true, false)]
    [InlineData("A$1", false, true)]
    [InlineData("$A$1", true, true)]
    public void TryParse_LeAsMarcacoesDeAbsoluto(string text, bool absoluteColumn, bool absoluteRow)
    {
        Assert.True(CellReference.TryParse(text, out CellReference reference));

        Assert.Equal(CellAddress.Origin, reference.Address);
        Assert.Equal(absoluteColumn, reference.AbsoluteColumn);
        Assert.Equal(absoluteRow, reference.AbsoluteRow);
        Assert.Null(reference.Sheet);
        Assert.False(reference.IsExternal);
    }

    [Theory]
    [InlineData("Premissas!B5", "Premissas")]
    [InlineData("premissas!$B$5", "premissas")]
    [InlineData("'Fluxo de Caixa'!B5", "Fluxo de Caixa")]
    [InlineData("'Ano ''25'!B5", "Ano '25")]
    public void TryParse_LeNomeDeAba(string text, string expectedSheet)
    {
        Assert.True(CellReference.TryParse(text, out CellReference reference));

        Assert.Equal(expectedSheet, reference.Sheet);
        Assert.True(reference.IsExternal);
        Assert.Equal(CellAddress.Parse("B5"), reference.Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("1")]
    [InlineData("$")]
    [InlineData("$$A1")]
    [InlineData("A$")]
    [InlineData("!A1")]
    [InlineData("'Sem fechar!A1")]
    [InlineData("A1:B2")]
    public void TryParse_RecusaReferenciasInvalidas(string text) =>
        Assert.False(CellReference.TryParse(text, out _));

    [Theory]
    [InlineData("A1", "A1")]
    [InlineData("$a$1", "$A$1")]
    [InlineData("Premissas!B5", "Premissas!B5")]
    [InlineData("'Fluxo de Caixa'!$B5", "'Fluxo de Caixa'!$B5")]
    public void ToString_ReproduzOTextoOriginal(string text, string expected) =>
        Assert.Equal(expected, CellReference.Parse(text).ToString());

    [Fact]
    public void TryTranslate_MoveApenasOsEixosRelativos()
    {
        Assert.True(CellReference.Parse("B2").TryTranslate(1, 1, out CellReference relative));
        Assert.Equal("C3", relative.ToString());

        Assert.True(CellReference.Parse("$B$2").TryTranslate(1, 1, out CellReference absolute));
        Assert.Equal("$B$2", absolute.ToString());

        Assert.True(CellReference.Parse("$B2").TryTranslate(1, 1, out CellReference mixedColumn));
        Assert.Equal("$B3", mixedColumn.ToString());

        Assert.True(CellReference.Parse("B$2").TryTranslate(1, 1, out CellReference mixedRow));
        Assert.Equal("C$2", mixedRow.ToString());
    }

    [Fact]
    public void TryTranslate_PreservaAAba()
    {
        Assert.True(CellReference.Parse("Premissas!B2").TryTranslate(0, 1, out CellReference moved));
        Assert.Equal("Premissas!C2", moved.ToString());
    }

    [Fact]
    public void TryTranslate_FalhaQuandoODestinoSaiDaPlanilha()
    {
        // É o caso que vira #REF! ao colar uma fórmula perto da borda da planilha.
        Assert.False(CellReference.Parse("A1").TryTranslate(-1, 0, out _));
    }
}
