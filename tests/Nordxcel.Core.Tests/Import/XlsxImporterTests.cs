using ClosedXML.Excel;
using Nordxcel.Core.Calculation;
using Nordxcel.Core.Export;
using Nordxcel.Core.Import;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Tests.Import;

public class XlsxImporterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nordxcel-import-test-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // Um arquivo propositalmente corrompido pode deixar um handle preso
            // até o coletor de lixo derrubar o stream que o ClosedXML abriu
            // antes de lançar — não é motivo pra falhar o teste, só limpar
            // o arquivo temporário do melhor jeito possível.
            GC.Collect();
            GC.WaitForPendingFinalizers();

            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Workbook CreateWorkbook(out Worksheet sheet)
    {
        var workbook = new Workbook();
        sheet = workbook.AddWorksheet("DCF");
        return workbook;
    }

    // ------------------------------------------------ ida e volta com o exportador

    [Fact]
    public void ExportarEImportar_PreservaValorETraduzAFormulaDeVolta()
    {
        Workbook original = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(1));
        sheet.SetCell(CellAddress.Parse("A2"), Cell.FromNumber(2));
        sheet.SetCell(CellAddress.Parse("A3"), Cell.FromFormula("SOMA(A1;A2)"));

        XlsxExporter.Export(original, _path);
        XlsxImportResult result = XlsxImporter.Import(_path);

        Worksheet imported = result.Workbook["DCF"];
        Assert.Equal("SOMA(A1;A2)", imported.GetCell(CellAddress.Parse("A3")).Formula);
        Assert.Empty(result.UnsupportedFunctions);

        // A fórmula importada calcula direito através do motor de verdade.
        var engine = new CalculationEngine(result.Workbook);
        Assert.Equal(3d, engine.Workbook["DCF"].GetValue(CellAddress.Parse("A3")).AsNumber());
    }

    [Fact]
    public void ExportarEImportar_PreservaMultiplasAbasNaMesmaOrdem()
    {
        var original = new Workbook();
        Worksheet premissas = original.AddWorksheet("Premissas");
        Worksheet dcf = original.AddWorksheet("DCF");
        premissas.SetCell(CellAddress.Parse("B2"), Cell.FromNumber(0.1));
        dcf.SetCell(CellAddress.Parse("A1"), Cell.FromFormula("Premissas!B2*2"));

        XlsxExporter.Export(original, _path);
        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.Equal(["Premissas", "DCF"], result.Workbook.Worksheets.Select(w => w.Name));
        Assert.Equal("Premissas!B2*2", result.Workbook["DCF"].GetCell(CellAddress.Parse("A1")).Formula);
    }

    [Fact]
    public void ExportarEImportar_PreservaEstiloEBordaDupla()
    {
        Workbook original = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(
            CellAddress.Parse("A1"),
            Cell.FromNumber(1) with
            {
                Style = CellStyle.Default with
                {
                    Bold = true,
                    Italic = true,
                    FontFamily = "Arial",
                    FontSize = 14,
                    BackgroundColor = new RgbColor(255, 255, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Borders = new CellBorders(
                        BorderEdge.None, BorderEdge.None,
                        new BorderEdge(BorderLineStyle.Double, RgbColor.Black), BorderEdge.None),
                },
            });

        XlsxExporter.Export(original, _path);
        XlsxImportResult result = XlsxImporter.Import(_path);

        CellStyle style = result.Workbook["DCF"].GetCell(CellAddress.Parse("A1")).Style;
        Assert.True(style.Bold);
        Assert.True(style.Italic);
        Assert.Equal("Arial", style.FontFamily);
        Assert.Equal(14, style.FontSize);
        Assert.Equal(new RgbColor(255, 255, 0), style.BackgroundColor);
        Assert.Equal(HorizontalAlignment.Right, style.HorizontalAlignment);
        Assert.Equal(BorderLineStyle.Double, style.Borders.Bottom.Style);
    }

    [Fact]
    public void ExportarEImportar_CorAutomaticaVoltaAutomatica()
    {
        // Fórmula na mesma aba, sem cor manual — exporta preta (convenção),
        // e ao reimportar tem que reconhecer que é a cor automática, não uma
        // escolha manual, senão a célula nunca mais se recoloriria sozinha.
        Workbook original = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(1));
        sheet.SetCell(CellAddress.Parse("A2"), Cell.FromFormula("A1*2"));

        XlsxExporter.Export(original, _path);
        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.Null(result.Workbook["DCF"].GetCell(CellAddress.Parse("A2")).Style.FontColor);
    }

    [Fact]
    public void ExportarEImportar_CorManualDiferenteDaConvencaoEhPreservada()
    {
        Workbook original = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(
            CellAddress.Parse("A1"),
            Cell.FromNumber(1) with { Style = CellStyle.Default with { FontColor = new RgbColor(200, 0, 0) } });

        XlsxExporter.Export(original, _path);
        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.Equal(new RgbColor(200, 0, 0), result.Workbook["DCF"].GetCell(CellAddress.Parse("A1")).Style.FontColor);
    }

    [Fact]
    public void ExportarEImportar_PreservaFormatoDeNumeroSemTraducao()
    {
        Workbook original = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(-1000) with { NumberFormat = "#,##0;(#,##0)" });

        XlsxExporter.Export(original, _path);
        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.Equal("#,##0;(#,##0)", result.Workbook["DCF"].GetCell(CellAddress.Parse("A1")).NumberFormat);
    }

    [Fact]
    public void ExportarEImportar_PreservaPaineisCongelados()
    {
        Workbook original = CreateWorkbook(out Worksheet sheet);
        sheet.FrozenRows = 2;
        sheet.FrozenColumns = 1;
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(1));

        XlsxExporter.Export(original, _path);
        XlsxImportResult result = XlsxImporter.Import(_path);

        Worksheet imported = result.Workbook["DCF"];
        Assert.Equal(2, imported.FrozenRows);
        Assert.Equal(1, imported.FrozenColumns);
    }

    [Fact]
    public void ExportarEImportar_TextoELogicoEErro()
    {
        Workbook original = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromText("Receita Líquida"));
        sheet.SetCell(CellAddress.Parse("A2"), Cell.FromLogical(true));
        sheet.SetCell(CellAddress.Parse("A3"), new Cell { Value = CellValue.Error(CellErrorType.DivideByZero) });

        XlsxExporter.Export(original, _path);
        XlsxImportResult result = XlsxImporter.Import(_path);

        Worksheet imported = result.Workbook["DCF"];
        Assert.Equal("Receita Líquida", imported.GetValue(CellAddress.Parse("A1")).AsText());
        Assert.True(imported.GetValue(CellAddress.Parse("A2")).AsLogical());
        Assert.Equal(CellErrorType.DivideByZero, imported.GetValue(CellAddress.Parse("A3")).AsError());
    }

    [Theory]
    [InlineData("ESCOLHER(2;\"A\";\"B\";\"C\")", "ESCOLHER")]
    [InlineData("MULT(2;3;4)", "MULT")]
    [InlineData("MED(1;2;3)", "MED")]
    [InlineData("HOJE()", "HOJE")]
    public void ExportarEImportar_FuncoesNovasTambemTraduzem(string formula, string expectedPrefix)
    {
        // A ida e volta com o próprio exportador já garante que a tabela de
        // tradução das 6 funções novas funciona nos dois sentidos.
        Workbook original = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("B1"), Cell.FromFormula(formula));

        XlsxExporter.Export(original, _path);
        XlsxImportResult result = XlsxImporter.Import(_path);

        string? imported = result.Workbook["DCF"].GetCell(CellAddress.Parse("B1")).Formula;
        Assert.NotNull(imported);
        Assert.StartsWith(expectedPrefix, imported);
        Assert.Empty(result.UnsupportedFunctions);
    }

    // ------------------------------------------------------- função não suportada

    [Fact]
    public void FuncaoNaoSuportada_ViraNomeSozinhaEEntraNoRelatorio()
    {
        using (var xlsx = new XLWorkbook())
        {
            IXLWorksheet sheet = xlsx.Worksheets.Add("DCF");
            sheet.Cell("A1").Value = 10;
            sheet.Cell("A2").FormulaA1 = "OFFSET(A1,0,0)";
            xlsx.SaveAs(_path);
        }

        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.Equal(1, result.UnsupportedFunctions["OFFSET"]);

        var engine = new CalculationEngine(result.Workbook);
        Assert.Equal(CellErrorType.Name, engine.Workbook["DCF"].GetValue(CellAddress.Parse("A2")).AsError());
    }

    [Fact]
    public void FuncaoNaoSuportada_ContaUmaVezPorCelulaMesmoUsadaDuasVezesNaMesmaFormula()
    {
        using (var xlsx = new XLWorkbook())
        {
            IXLWorksheet sheet = xlsx.Worksheets.Add("DCF");
            sheet.Cell("A1").FormulaA1 = "OFFSET(A1,0,0)+OFFSET(A1,0,0)";
            xlsx.SaveAs(_path);
        }

        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.Equal(1, result.UnsupportedFunctions["OFFSET"]);
    }

    [Fact]
    public void MisturaDeFuncaoConhecidaENaoSuportada_TraduzSoAConhecida()
    {
        using (var xlsx = new XLWorkbook())
        {
            IXLWorksheet sheet = xlsx.Worksheets.Add("DCF");
            sheet.Cell("A1").Value = 10;
            sheet.Cell("A2").FormulaA1 = "SUM(OFFSET(A1,0,0),1)";
            xlsx.SaveAs(_path);
        }

        XlsxImportResult result = XlsxImporter.Import(_path);

        string? formula = result.Workbook["DCF"].GetCell(CellAddress.Parse("A2")).Formula;
        Assert.NotNull(formula);
        Assert.StartsWith("SOMA(", formula);
        Assert.Contains("OFFSET", formula);
        Assert.Equal(1, result.UnsupportedFunctions["OFFSET"]);
    }

    [Fact]
    public void LiteralLogicoEDeErroEmIngles_SaoReconhecidos()
    {
        using (var xlsx = new XLWorkbook())
        {
            IXLWorksheet sheet = xlsx.Worksheets.Add("DCF");
            sheet.Cell("A1").FormulaA1 = "IF(TRUE,1,0)";
            sheet.Cell("A2").FormulaA1 = "IFERROR(1/0,#N/A)";
            xlsx.SaveAs(_path);
        }

        XlsxImportResult result = XlsxImporter.Import(_path);

        var engine = new CalculationEngine(result.Workbook);
        Assert.Equal(1d, engine.Workbook["DCF"].GetValue(CellAddress.Parse("A1")).AsNumber());
        Assert.Equal(CellErrorType.NotAvailable, engine.Workbook["DCF"].GetValue(CellAddress.Parse("A2")).AsError());
        Assert.Empty(result.UnsupportedFunctions);
    }

    // ---------------------------------------------------------- nome definido

    [Fact]
    public void NomeDefinido_ViraNomeSozinhaEEntraNoRelatorioSeparado()
    {
        // Uma referência a um intervalo nomeado do Excel (WACC em vez de
        // Premissas!$B$3) — o Nordxcel não tem nome definido, então vira
        // #NOME? mas precisa aparecer separado de "função não suportada": a
        // causa (e a correção) são diferentes.
        using (var xlsx = new XLWorkbook())
        {
            IXLWorksheet sheet = xlsx.Worksheets.Add("DCF");
            sheet.Cell("A1").Value = 10;
            sheet.Cell("A2").FormulaA1 = "WACC*A1";
            xlsx.SaveAs(_path);
        }

        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.Equal(1, result.UnrecognizedNames["WACC"]);
        Assert.Empty(result.UnsupportedFunctions);

        var engine = new CalculationEngine(result.Workbook);
        Assert.Equal(CellErrorType.Name, engine.Workbook["DCF"].GetValue(CellAddress.Parse("A2")).AsError());
    }

    // ------------------------------------------------------- fórmula de matriz

    [Fact]
    public void FormulaDeMatrizEntreChaves_ContaComoNaoInterpretadaSemDerrubarAImportacao()
    {
        // A Tabela de Dados do próprio Excel usa {=TABLE(...)}, sintaxe que o
        // parser não tenta entender — precisa virar #NOME? sem lançar exceção.
        using (var xlsx = new XLWorkbook())
        {
            IXLWorksheet sheet = xlsx.Worksheets.Add("DCF");
            sheet.Cell("A1").Value = 10;
            sheet.Cell("A2").FormulaA1 = "{=TABLE(A1,A1)}";
            xlsx.SaveAs(_path);
        }

        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.True(result.UnparseableFormulaCount > 0);

        var engine = new CalculationEngine(result.Workbook);
        Assert.Equal(CellErrorType.Name, engine.Workbook["DCF"].GetValue(CellAddress.Parse("A2")).AsError());
    }

    // -------------------------------------------------------------- nome de aba

    [Fact]
    public void NomeDeAbaValido_EhPreservado()
    {
        using (var xlsx = new XLWorkbook())
        {
            xlsx.Worksheets.Add("Fluxo de Caixa");
            xlsx.SaveAs(_path);
        }

        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.Equal("Fluxo de Caixa", result.Workbook.Worksheets[0].Name);
    }

    // ---------------------------------------------------------------- recursos

    [Fact]
    public void CelulaMesclada_EhContadaComoRecursoIgnorado()
    {
        using (var xlsx = new XLWorkbook())
        {
            IXLWorksheet sheet = xlsx.Worksheets.Add("DCF");
            sheet.Range("A1:B1").Merge();
            xlsx.SaveAs(_path);
        }

        XlsxImportResult result = XlsxImporter.Import(_path);

        Assert.True(result.SkippedFeatureCount > 0);
    }

    // ------------------------------------------------------------------- erros

    [Fact]
    public void ArquivoQueNaoExiste_LancaXlsxImportException() =>
        Assert.Throws<XlsxImportException>(() => XlsxImporter.Import(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx")));

    [Fact]
    public void ArquivoCorrompido_LancaXlsxImportException()
    {
        File.WriteAllText(_path, "isto não é um .xlsx");

        Assert.Throws<XlsxImportException>(() => XlsxImporter.Import(_path));
    }
}
