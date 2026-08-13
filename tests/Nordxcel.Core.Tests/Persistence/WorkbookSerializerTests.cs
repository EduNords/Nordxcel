using System.Linq;
using Nordxcel.Core.Calculation;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;
using Nordxcel.Core.Persistence;

namespace Nordxcel.Core.Tests.Persistence;

public class WorkbookSerializerTests
{
    private static CellAddress At(string address) => CellAddress.Parse(address);

    private static Workbook RoundTrip(Workbook original) =>
        WorkbookSerializer.Deserialize(WorkbookSerializer.Serialize(original));

    // ------------------------------------------------------------------- abas

    [Fact]
    public void PastaVazia_RoundTripPreservaUmaAbaVazia()
    {
        Workbook original = Workbook.CreateDefault();

        Workbook loaded = RoundTrip(original);

        Worksheet sheet = Assert.Single(loaded.Worksheets);
        Assert.Equal("Planilha1", sheet.Name);
        Assert.Equal(0, sheet.CellCount);
    }

    [Fact]
    public void VariasAbas_PreservamNomeEOrdem()
    {
        var original = new Workbook();
        original.AddWorksheet("Premissas");
        original.AddWorksheet("DCF");
        original.AddWorksheet("Sensibilidade");

        Workbook loaded = RoundTrip(original);

        Assert.Equal(["Premissas", "DCF", "Sensibilidade"], loaded.Worksheets.Select(s => s.Name));
    }

    [Fact]
    public void PainesCongelados_SaoPreservados()
    {
        var original = new Workbook();
        Worksheet sheet = original.AddWorksheet("DCF");
        sheet.FrozenRows = 2;
        sheet.FrozenColumns = 1;

        Workbook loaded = RoundTrip(original);

        Assert.Equal(2, loaded["DCF"].FrozenRows);
        Assert.Equal(1, loaded["DCF"].FrozenColumns);
    }

    [Fact]
    public void LargurasEAlturasCustomizadas_SaoPreservadas()
    {
        var original = new Workbook();
        Worksheet sheet = original.AddWorksheet("DCF");
        sheet.SetColumnWidth(0, 220);
        sheet.SetRowHeight(3, 32);

        Workbook loaded = RoundTrip(original);

        Worksheet loadedSheet = loaded["DCF"];
        Assert.Equal(220, loadedSheet.GetColumnWidth(0));
        Assert.Equal(32, loadedSheet.GetRowHeight(3));
        Assert.Equal(Worksheet.DefaultColumnWidth, loadedSheet.GetColumnWidth(1));
    }

    // ---------------------------------------------------------------- valores

    [Theory]
    [InlineData(0d)]
    [InlineData(1234.5678)]
    [InlineData(-999.5)]
    public void NumeroLiteral_RoundTrip(double number)
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetValue(At("A1"), CellValue.Number(number));

        Workbook loaded = RoundTrip(original);

        Assert.Equal(number, loaded["DCF"].GetValue(At("A1")).AsNumber());
    }

    [Fact]
    public void TextoComAcentosEAspas_RoundTrip()
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetValue(At("A1"), CellValue.Text("Receita líquida \"ajustada\""));

        Workbook loaded = RoundTrip(original);

        Assert.Equal("Receita líquida \"ajustada\"", loaded["DCF"].GetValue(At("A1")).AsText());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Logico_RoundTrip(bool value)
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetValue(At("A1"), CellValue.Logical(value));

        Workbook loaded = RoundTrip(original);

        Assert.Equal(value, loaded["DCF"].GetValue(At("A1")).AsLogical());
    }

    [Theory]
    [InlineData(CellErrorType.DivideByZero)]
    [InlineData(CellErrorType.Reference)]
    [InlineData(CellErrorType.Circular)]
    public void ErroLiteral_RoundTrip(CellErrorType error)
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetValue(At("A1"), CellValue.Error(error));

        Workbook loaded = RoundTrip(original);

        Assert.Equal(error, loaded["DCF"].GetValue(At("A1")).AsError());
    }

    [Fact]
    public void CelulaEmBranco_NaoEntraNoArquivo()
    {
        // Confirma o armazenamento esparso: uma célula nunca tocada não deve
        // virar uma entrada no documento.
        var original = new Workbook();
        original.AddWorksheet("DCF");

        string json = WorkbookSerializer.Serialize(original);

        Assert.DoesNotContain("\"row\"", json, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- fórmulas

    [Fact]
    public void Formula_PreservaOTexto()
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("B2"), Cell.FromFormula("SOMA(A1:A10)*1,1"));

        Workbook loaded = RoundTrip(original);

        Cell cell = loaded["DCF"].GetCell(At("B2"));
        Assert.True(cell.HasFormula);
        Assert.Equal("SOMA(A1:A10)*1,1", cell.Formula);
    }

    [Fact]
    public void Formula_NaoSalvaOValorCalculado_MasRecalculaAoAbrirNoMotor()
    {
        // O ponto central: o arquivo não precisa carregar o valor calculado,
        // porque abrir a pasta num CalculationEngine recalcula tudo sozinho.
        var original = new Workbook();
        Worksheet sheet = original.AddWorksheet("DCF");
        sheet.SetValue(At("A1"), CellValue.Number(10));
        sheet.SetCell(At("B1"), Cell.FromFormula("A1*2"));

        string json = WorkbookSerializer.Serialize(original);

        // O documento não carrega "20" em lugar nenhum para a fórmula.
        Assert.DoesNotContain("\"number\": 20", json, StringComparison.Ordinal);

        Workbook loaded = WorkbookSerializer.Deserialize(json);
        var engine = new CalculationEngine(loaded);

        Assert.Equal(20d, loaded["DCF"].GetValue(At("B1")).AsNumber());
    }

    [Fact]
    public void FormulaEntreAbas_ContinuaFuncionandoDepoisDeCarregarNoMotor()
    {
        var original = new Workbook();
        original.AddWorksheet("Premissas").SetValue(At("B1"), CellValue.Number(0.11));
        original.AddWorksheet("DCF").SetCell(At("B1"), Cell.FromFormula("Premissas!B1*100"));

        Workbook loaded = RoundTrip(original);
        var engine = new CalculationEngine(loaded);

        Assert.Equal(11d, loaded["DCF"].GetValue(At("B1")).AsNumber(), 9);
    }

    // ---------------------------------------------------------- formato de número

    [Fact]
    public void FormatoDeNumero_RoundTrip()
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("A1"), new Cell
        {
            Value = CellValue.Number(1234567),
            NumberFormat = "#,##0;(#,##0)",
        });

        Workbook loaded = RoundTrip(original);

        Assert.Equal("#,##0;(#,##0)", loaded["DCF"].GetCell(At("A1")).NumberFormat);
    }

    [Fact]
    public void SemFormatoDeNumero_ContinuaNulo()
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetValue(At("A1"), CellValue.Number(1));

        Workbook loaded = RoundTrip(original);

        Assert.Null(loaded["DCF"].GetCell(At("A1")).NumberFormat);
    }

    // ------------------------------------------------------------------ estilo

    [Fact]
    public void EstiloPadrao_NaoEntraNoArquivo()
    {
        // Uma célula só com valor, sem nenhuma formatação manual, não deveria
        // carregar um objeto de estilo inteiro no arquivo.
        var original = new Workbook();
        original.AddWorksheet("DCF").SetValue(At("A1"), CellValue.Number(1));

        string json = WorkbookSerializer.Serialize(original);

        Assert.DoesNotContain("\"style\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FonteNegritoItalicoSublinhado_RoundTrip()
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("A1"), new Cell
        {
            Style = CellStyle.Default with
            {
                FontFamily = "Arial",
                FontSize = 14,
                Bold = true,
                Italic = true,
                Underline = true,
            },
        });

        CellStyle loaded = RoundTrip(original)["DCF"].GetCell(At("A1")).Style;

        Assert.Equal("Arial", loaded.FontFamily);
        Assert.Equal(14, loaded.FontSize);
        Assert.True(loaded.Bold);
        Assert.True(loaded.Italic);
        Assert.True(loaded.Underline);
    }

    [Fact]
    public void CoresDeFonteEPreenchimento_RoundTrip()
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("A1"), new Cell
        {
            Style = CellStyle.Default with
            {
                FontColor = new RgbColor(0, 0, 255),
                BackgroundColor = new RgbColor(255, 255, 0),
            },
        });

        CellStyle loaded = RoundTrip(original)["DCF"].GetCell(At("A1")).Style;

        Assert.Equal(new RgbColor(0, 0, 255), loaded.FontColor);
        Assert.Equal(new RgbColor(255, 255, 0), loaded.BackgroundColor);
    }

    [Fact]
    public void CorAutomatica_ContinuaNula()
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("A1"), new Cell { Style = CellStyle.Default with { Bold = true } });

        CellStyle loaded = RoundTrip(original)["DCF"].GetCell(At("A1")).Style;

        Assert.Null(loaded.FontColor);
        Assert.Null(loaded.BackgroundColor);
    }

    [Theory]
    [InlineData(HorizontalAlignment.Left)]
    [InlineData(HorizontalAlignment.Center)]
    [InlineData(HorizontalAlignment.Right)]
    public void AlinhamentoHorizontal_RoundTrip(HorizontalAlignment alignment)
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("A1"), new Cell { Style = CellStyle.Default with { HorizontalAlignment = alignment } });

        CellStyle loaded = RoundTrip(original)["DCF"].GetCell(At("A1")).Style;

        Assert.Equal(alignment, loaded.HorizontalAlignment);
    }

    [Theory]
    [InlineData(VerticalAlignment.Top)]
    [InlineData(VerticalAlignment.Center)]
    [InlineData(VerticalAlignment.Bottom)]
    public void AlinhamentoVertical_RoundTrip(VerticalAlignment alignment)
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("A1"), new Cell { Style = CellStyle.Default with { VerticalAlignment = alignment } });

        CellStyle loaded = RoundTrip(original)["DCF"].GetCell(At("A1")).Style;

        Assert.Equal(alignment, loaded.VerticalAlignment);
    }

    [Fact]
    public void IndentLevel_RoundTrip()
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("A1"), new Cell { Style = CellStyle.Default with { IndentLevel = 2 } });

        CellStyle loaded = RoundTrip(original)["DCF"].GetCell(At("A1")).Style;

        Assert.Equal(2, loaded.IndentLevel);
    }

    [Fact]
    public void BordasEmTodosOsLados_RoundTrip()
    {
        var original = new Workbook();
        var borders = new CellBorders(
            Top: new BorderEdge(BorderLineStyle.Thin, RgbColor.Black),
            Right: new BorderEdge(BorderLineStyle.Medium, new RgbColor(255, 0, 0)),
            Bottom: new BorderEdge(BorderLineStyle.Double, RgbColor.Black),
            Left: new BorderEdge(BorderLineStyle.Thick, new RgbColor(0, 128, 0)));

        original.AddWorksheet("DCF").SetCell(At("A1"), new Cell { Style = CellStyle.Default with { Borders = borders } });

        CellBorders loaded = RoundTrip(original)["DCF"].GetCell(At("A1")).Style.Borders;

        Assert.Equal(BorderLineStyle.Thin, loaded.Top.Style);
        Assert.Equal(BorderLineStyle.Medium, loaded.Right.Style);
        Assert.Equal(new RgbColor(255, 0, 0), loaded.Right.Color);
        Assert.Equal(BorderLineStyle.Double, loaded.Bottom.Style);
        Assert.Equal(BorderLineStyle.Thick, loaded.Left.Style);
        Assert.Equal(new RgbColor(0, 128, 0), loaded.Left.Color);
    }

    [Fact]
    public void SemBorda_ContinuaSemBorda()
    {
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("A1"), new Cell { Style = CellStyle.Default with { Bold = true } });

        CellBorders loaded = RoundTrip(original)["DCF"].GetCell(At("A1")).Style.Borders;

        Assert.False(loaded.HasAny);
    }

    [Fact]
    public void FormulaComEstiloEFormato_PreservaTudoJunto()
    {
        // O caso real de uma linha de total: fórmula, negrito e borda superior juntos.
        var original = new Workbook();
        original.AddWorksheet("DCF").SetCell(At("B9"), Cell.FromFormula("B7+B8") with
        {
            NumberFormat = "#,##0;(#,##0)",
            Style = CellStyle.Default with
            {
                Bold = true,
                Borders = CellBorders.None with { Top = BorderEdge.Thin(RgbColor.Black) },
            },
        });

        Cell loaded = RoundTrip(original)["DCF"].GetCell(At("B9"));

        Assert.Equal("B7+B8", loaded.Formula);
        Assert.Equal("#,##0;(#,##0)", loaded.NumberFormat);
        Assert.True(loaded.Style.Bold);
        Assert.True(loaded.Style.Borders.Top.IsVisible);
    }

    // -------------------------------------------------------------- arquivo em disco

    [Fact]
    public void SerializeToFile_E_DeserializeFromFile_RoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nordxcel-teste-{Guid.NewGuid():N}.nxcl");

        try
        {
            var original = new Workbook();
            original.AddWorksheet("DCF").SetValue(At("A1"), CellValue.Number(42));

            WorkbookSerializer.SerializeToFile(original, path);

            Assert.True(File.Exists(path));

            Workbook loaded = WorkbookSerializer.DeserializeFromFile(path);

            Assert.Equal(42d, loaded["DCF"].GetValue(At("A1")).AsNumber());
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ------------------------------------------------------------------- erros

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{ \"worksheets\": [")]
    [InlineData("42")]
    public void JsonInvalido_LancaWorkbookFormatException(string malformed) =>
        Assert.Throws<WorkbookFormatException>(() => WorkbookSerializer.Deserialize(malformed));

    [Fact]
    public void VersaoDeFormatoFutura_Lanca()
    {
        string json = /*lang=json,strict*/ """
        { "formatVersion": 999, "worksheets": [] }
        """;

        WorkbookFormatException exception = Assert.Throws<WorkbookFormatException>(() => WorkbookSerializer.Deserialize(json));
        Assert.Contains("999", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NomeDeAbaInvalido_Lanca()
    {
        string json = /*lang=json,strict*/ """
        { "formatVersion": 1, "worksheets": [ { "name": "Nome/Invalido", "cells": [] } ] }
        """;

        Assert.Throws<WorkbookFormatException>(() => WorkbookSerializer.Deserialize(json));
    }

    [Fact]
    public void EnderecoDeCelulaInvalido_Lanca()
    {
        string json = /*lang=json,strict*/ """
        {
          "formatVersion": 1,
          "worksheets": [
            { "name": "DCF", "cells": [ { "row": -1, "column": 0 } ] }
          ]
        }
        """;

        Assert.Throws<WorkbookFormatException>(() => WorkbookSerializer.Deserialize(json));
    }

    [Fact]
    public void CorInvalida_Lanca()
    {
        string json = /*lang=json,strict*/ """
        {
          "formatVersion": 1,
          "worksheets": [
            { "name": "DCF", "cells": [
              { "row": 0, "column": 0, "style": { "fontColor": "não é cor" } }
            ] }
          ]
        }
        """;

        Assert.Throws<WorkbookFormatException>(() => WorkbookSerializer.Deserialize(json));
    }

    [Fact]
    public void TipoDeErroDesconhecido_Lanca()
    {
        string json = /*lang=json,strict*/ """
        {
          "formatVersion": 1,
          "worksheets": [
            { "name": "DCF", "cells": [
              { "row": 0, "column": 0, "value": { "kind": "Error", "error": "AlgoQueNaoExiste" } }
            ] }
          ]
        }
        """;

        Assert.Throws<WorkbookFormatException>(() => WorkbookSerializer.Deserialize(json));
    }
}
