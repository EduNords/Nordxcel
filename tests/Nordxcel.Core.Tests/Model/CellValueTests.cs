using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Model;

public class CellValueTests
{
    [Fact]
    public void Default_EhVazio()
    {
        CellValue value = default;

        Assert.Equal(CellValueKind.Blank, value.Kind);
        Assert.True(value.IsBlank);
        Assert.Equal(CellValue.Blank, value);
    }

    [Fact]
    public void Vazio_ValeZeroEmContextoNumericoETextoVazioEmContextoTextual()
    {
        Assert.Equal(0d, CellValue.Blank.AsNumber());
        Assert.Equal(string.Empty, CellValue.Blank.AsText());
        Assert.False(CellValue.Blank.AsLogical());
    }

    [Fact]
    public void Logico_ContaComoUmOuZero()
    {
        Assert.Equal(1d, CellValue.True.AsNumber());
        Assert.Equal(0d, CellValue.False.AsNumber());
    }

    [Fact]
    public void Numero_ConverteParaLogicoPelaRegraDoExcel()
    {
        Assert.True(CellValue.Number(-2.5).AsLogical());
        Assert.False(CellValue.Number(0).AsLogical());
    }

    [Fact]
    public void TextoEErro_NaoSaoNumericos()
    {
        Assert.Throws<InvalidOperationException>(() => CellValue.Text("abc").AsNumber());
        Assert.Throws<InvalidOperationException>(() => CellValue.Error(CellErrorType.Value).AsNumber());

        Assert.False(CellValue.Text("abc").TryGetNumber(out _));
        Assert.False(CellValue.Error(CellErrorType.Value).TryGetNumber(out _));
    }

    [Fact]
    public void TryGetNumber_AceitaNumeroLogicoEVazio()
    {
        Assert.True(CellValue.Number(7).TryGetNumber(out double number));
        Assert.Equal(7d, number);

        Assert.True(CellValue.True.TryGetNumber(out number));
        Assert.Equal(1d, number);

        Assert.True(CellValue.Blank.TryGetNumber(out number));
        Assert.Equal(0d, number);
    }

    [Fact]
    public void Error_RecusaNone() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CellValue.Error(CellErrorType.None));

    [Fact]
    public void Igualdade_ComparaTipoEConteudo()
    {
        Assert.Equal(CellValue.Number(1), CellValue.Number(1));
        Assert.NotEqual(CellValue.Number(1), CellValue.Text("1"));
        Assert.NotEqual(CellValue.Number(1), CellValue.True);
        Assert.Equal(CellValue.Text("DCF"), CellValue.Text("DCF"));
        Assert.NotEqual(CellValue.Text("DCF"), CellValue.Text("dcf"));
        Assert.Equal(CellValue.Error(CellErrorType.DivideByZero), CellValue.Error(CellErrorType.DivideByZero));
        Assert.NotEqual(CellValue.Error(CellErrorType.DivideByZero), CellValue.Error(CellErrorType.Value));

        Assert.True(CellValue.Number(2) == CellValue.Number(2));
        Assert.True(CellValue.Number(2) != CellValue.Number(3));
    }

    [Fact]
    public void ToString_NaoAplicaMascaraEUsaCulturaInvariante()
    {
        Assert.Equal("1234.5", CellValue.Number(1234.5).ToString());
        Assert.Equal("VERDADEIRO", CellValue.True.ToString());
        Assert.Equal(string.Empty, CellValue.Blank.ToString());
        Assert.Equal("#DIV/0!", CellValue.Error(CellErrorType.DivideByZero).ToString());
    }

    [Theory]
    [InlineData(CellErrorType.DivideByZero, "#DIV/0!")]
    [InlineData(CellErrorType.Value, "#VALOR!")]
    [InlineData(CellErrorType.Reference, "#REF!")]
    [InlineData(CellErrorType.Name, "#NOME?")]
    [InlineData(CellErrorType.Number, "#NÚM!")]
    [InlineData(CellErrorType.NotAvailable, "#N/D")]
    [InlineData(CellErrorType.Circular, "#CIRC!")]
    public void Erros_UsamOsTextosEmPortugues(CellErrorType error, string expected)
    {
        Assert.Equal(expected, error.ToDisplayText());

        Assert.True(CellErrors.TryParse(expected, out CellErrorType parsed));
        Assert.Equal(error, parsed);
    }

    [Fact]
    public void CellErrors_TryParse_RecusaTextoComum() =>
        Assert.False(CellErrors.TryParse("#QUALQUER", out _));
}
