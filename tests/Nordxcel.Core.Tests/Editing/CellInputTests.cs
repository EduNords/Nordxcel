using Nordxcel.Core.Editing;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Tests.Editing;

public class CellInputTests
{
    private static Cell Parse(string text, Cell? existing = null) => CellInput.Parse(text, existing);

    // -------------------------------------------------------------- números

    [Theory]
    [InlineData("42", 42)]
    [InlineData("1,5", 1.5)]
    [InlineData("-3,25", -3.25)]
    [InlineData("0", 0)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("-1.000", -1000)]
    public void Numero_UsaAVirgulaDecimalEOPontoDeMilhar(string text, double expected)
    {
        Cell cell = Parse(text);

        Assert.True(cell.Value.IsNumber, $"'{text}' devia virar número, virou {cell.Value.Kind}.");
        Assert.Equal(expected, cell.Value.AsNumber());
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1.23")]
    [InlineData("12.3456")]
    public void SeparadorDeMilharMalFormado_ViraTexto(string text)
    {
        // Aceitar isso faria "1.5" virar 15 silenciosamente: um erro de 10x que
        // ninguém percebe até o valuation ficar pronto.
        Assert.True(Parse(text).Value.IsText);
    }

    [Fact]
    public void Numero_NaoInventaMascara() =>
        Assert.Null(Parse("1000").NumberFormat);

    // --------------------------------------------------------- porcentagem

    [Fact]
    public void Porcentagem_GravaAFracaoEAplicaAMascara()
    {
        Cell cell = Parse("12,5%");

        Assert.Equal(0.125d, cell.Value.AsNumber());
        Assert.Equal(StandardNumberFormats.Percent, cell.NumberFormat);
    }

    [Fact]
    public void Porcentagem_NaoSobrescreveUmaMascaraJaEscolhida()
    {
        var existing = new Cell { NumberFormat = "0.000%" };

        Assert.Equal("0.000%", Parse("8%", existing).NumberFormat);
    }

    // -------------------------------------------------------------- moeda

    [Theory]
    [InlineData("R$ 1.500", 1500)]
    [InlineData("R$1.500,50", 1500.50)]
    public void Moeda_EmReal(string text, double expected)
    {
        Cell cell = Parse(text);

        Assert.Equal(expected, cell.Value.AsNumber());
        Assert.Equal(StandardNumberFormats.CurrencyReal, cell.NumberFormat);
    }

    [Fact]
    public void Moeda_EmDolar()
    {
        Cell cell = Parse("US$ 2.000");

        Assert.Equal(2000d, cell.Value.AsNumber());
        Assert.Equal(StandardNumberFormats.CurrencyDollar, cell.NumberFormat);
    }

    // --------------------------------------------------------------- datas

    [Fact]
    public void Data_GravaOSerialEAplicaAMascara()
    {
        Cell cell = Parse("31/12/2025");

        Assert.Equal(ExcelDate.ToSerial(new DateTime(2025, 12, 31)), cell.Value.AsNumber());
        Assert.Equal(StandardNumberFormats.ShortDate, cell.NumberFormat);
    }

    // ------------------------------------------------------------- fórmulas

    [Fact]
    public void Formula_GuardaAExpressaoSemOIgual()
    {
        Cell cell = Parse("=SOMA(A1:A3)");

        Assert.True(cell.HasFormula);
        Assert.Equal("SOMA(A1:A3)", cell.Formula);
    }

    [Fact]
    public void IgualSozinho_EhTexto() =>
        Assert.True(Parse("=").Value.IsText);

    // ---------------------------------------------------------- outros tipos

    [Theory]
    [InlineData("VERDADEIRO", true)]
    [InlineData("falso", false)]
    public void Logico(string text, bool expected) =>
        Assert.Equal(expected, Parse(text).Value.AsLogical());

    [Fact]
    public void Erro_DigitadoDiretamente() =>
        Assert.Equal(CellErrorType.NotAvailable, Parse("#N/D").Value.AsError());

    [Fact]
    public void Apostrofo_ForcaTexto()
    {
        Cell cell = Parse("'001");

        Assert.True(cell.Value.IsText);
        Assert.Equal("001", cell.Value.AsText());
    }

    [Fact]
    public void TextoComum() =>
        Assert.Equal("Receita líquida", Parse("Receita líquida").Value.AsText());

    [Fact]
    public void TextoVazio_LimpaOValor()
    {
        var existing = new Cell { Value = CellValue.Number(10) };

        Assert.True(Parse("", existing).Value.IsBlank);
    }

    // ------------------------------------------- preservação de formato e estilo

    [Fact]
    public void Preserva_FormatoEEstiloDaCelula()
    {
        var existing = new Cell
        {
            Value = CellValue.Number(1),
            NumberFormat = StandardNumberFormats.Thousands,
            Style = CellStyle.Default with { Bold = true },
        };

        Cell updated = Parse("2000", existing);

        Assert.Equal(StandardNumberFormats.Thousands, updated.NumberFormat);
        Assert.True(updated.Style.Bold);
    }

    [Fact]
    public void TrocarValorPorFormula_PreservaFormatoEEstilo()
    {
        var existing = new Cell
        {
            Value = CellValue.Number(1),
            NumberFormat = StandardNumberFormats.Thousands,
            Style = CellStyle.Default with { Italic = true },
        };

        Cell updated = Parse("=A1*2", existing);

        Assert.True(updated.HasFormula);
        Assert.Equal(StandardNumberFormats.Thousands, updated.NumberFormat);
        Assert.True(updated.Style.Italic);
    }

    [Fact]
    public void TrocarFormulaPorValor_ApagaAFormula()
    {
        Cell existing = Cell.FromFormula("A1*2");

        Assert.False(Parse("500", existing).HasFormula);
    }

    // -------------------------------------------------- texto da barra de fórmulas

    [Fact]
    public void ToEditText_MostraAFormulaComOIgual() =>
        Assert.Equal("=SOMA(A1:A3)", CellInput.ToEditText(Cell.FromFormula("SOMA(A1:A3)")));

    [Fact]
    public void ToEditText_MostraONumeroBrutoESemMascara()
    {
        var cell = new Cell
        {
            Value = CellValue.Number(1234.5),
            NumberFormat = StandardNumberFormats.Thousands,
        };

        // A célula mostra 1.235, mas quem edita precisa ver o número de verdade.
        Assert.Equal("1234,5", CellInput.ToEditText(cell));
    }

    [Fact]
    public void ToEditText_MostraPorcentagemComoPorcentagem()
    {
        var cell = new Cell
        {
            Value = CellValue.Number(0.125),
            NumberFormat = StandardNumberFormats.Percent,
        };

        Assert.Equal("12,5%", CellInput.ToEditText(cell));
    }

    [Fact]
    public void ToEditText_MostraDataComoData()
    {
        var cell = new Cell
        {
            Value = CellValue.Number(ExcelDate.ToSerial(new DateTime(2025, 3, 31))),
            NumberFormat = StandardNumberFormats.ShortDate,
        };

        // Ninguém quer editar 45747.
        Assert.Equal("31/03/2025", CellInput.ToEditText(cell));
    }

    [Fact]
    public void ToEditText_DosDemaisTipos()
    {
        Assert.Equal(string.Empty, CellInput.ToEditText(Cell.Empty));
        Assert.Equal("Receita", CellInput.ToEditText(Cell.FromText("Receita")));
        Assert.Equal("VERDADEIRO", CellInput.ToEditText(Cell.FromLogical(true)));
        Assert.Equal(
            "#DIV/0!",
            CellInput.ToEditText(new Cell { Value = CellValue.Error(CellErrorType.DivideByZero) }));
    }

    [Fact]
    public void EditarEReconfirmar_NaoMudaOValor()
    {
        // Selecionar, ver na barra e apertar Enter tem que dar na mesma.
        foreach (Cell original in new[]
                 {
                     Cell.FromNumber(1234.5),
                     Cell.FromText("Receita"),
                     Cell.FromLogical(true),
                     new Cell { Value = CellValue.Number(0.125), NumberFormat = StandardNumberFormats.Percent },
                     new Cell
                     {
                         Value = CellValue.Number(ExcelDate.ToSerial(new DateTime(2025, 3, 31))),
                         NumberFormat = StandardNumberFormats.ShortDate,
                     },
                 })
        {
            Cell roundTripped = CellInput.Parse(CellInput.ToEditText(original), original);

            Assert.Equal(original.Value, roundTripped.Value);
        }
    }
}
