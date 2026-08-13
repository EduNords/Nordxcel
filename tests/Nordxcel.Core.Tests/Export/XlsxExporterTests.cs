using ClosedXML.Excel;
using Nordxcel.Core.Export;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Tests.Export;

public class XlsxExporterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nordxcel-export-test-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static Workbook CreateWorkbook(out Worksheet sheet)
    {
        var workbook = new Workbook();
        sheet = workbook.AddWorksheet("DCF");
        return workbook;
    }

    private XLWorkbook ExportAndReopen(Workbook workbook)
    {
        XlsxExporter.Export(workbook, _path);
        return new XLWorkbook(_path);
    }

    [Fact]
    public void ExportaValorNumericoLiteral()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(42.5));

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        Assert.Equal(42.5, xlsx.Worksheet("DCF").Cell("A1").GetDouble());
    }

    [Fact]
    public void ExportaTextoETrataAspaComoLiteral()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromText("Receita Líquida"));

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        Assert.Equal("Receita Líquida", xlsx.Worksheet("DCF").Cell("A1").GetString());
    }

    [Fact]
    public void ExportaLogico()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromLogical(true));

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        Assert.True(xlsx.Worksheet("DCF").Cell("A1").GetBoolean());
    }

    [Theory]
    [InlineData(CellErrorType.DivideByZero, XLError.DivisionByZero)]
    [InlineData(CellErrorType.Value, XLError.IncompatibleValue)]
    [InlineData(CellErrorType.Reference, XLError.CellReference)]
    [InlineData(CellErrorType.Name, XLError.NameNotRecognized)]
    [InlineData(CellErrorType.Number, XLError.NumberInvalid)]
    [InlineData(CellErrorType.NotAvailable, XLError.NoValueAvailable)]
    [InlineData(CellErrorType.Null, XLError.NullValue)]
    public void ExportaErroComoValorDeErroDoExcel(CellErrorType nordxcelError, XLError excelError)
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), new Cell { Value = CellValue.Error(nordxcelError) });

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        Assert.Equal(excelError, xlsx.Worksheet("DCF").Cell("A1").GetError());
    }

    [Fact]
    public void ExportaFormulaTraduzindoNomeEmPortuguesEArgumentoParaVirgula()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(1));
        sheet.SetCell(CellAddress.Parse("A2"), Cell.FromNumber(2));
        sheet.SetCell(CellAddress.Parse("A3"), Cell.FromFormula("SOMA(A1;A2)"));

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        Assert.Equal("SUM(A1,A2)", xlsx.Worksheet("DCF").Cell("A3").FormulaA1);
        Assert.Equal(3d, xlsx.Worksheet("DCF").Cell("A3").GetDouble());
    }

    [Fact]
    public void ExportaFormulaComReferenciaEntreAbas()
    {
        var workbook = new Workbook();
        Worksheet premissas = workbook.AddWorksheet("Premissas");
        Worksheet dcf = workbook.AddWorksheet("DCF");
        premissas.SetCell(CellAddress.Parse("B2"), Cell.FromNumber(0.1));
        dcf.SetCell(CellAddress.Parse("A1"), Cell.FromFormula("Premissas!B2*2"));

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        Assert.Equal(["Premissas", "DCF"], xlsx.Worksheets.Select(w => w.Name));
        Assert.Equal("Premissas!B2*2", xlsx.Worksheet("DCF").Cell("A1").FormulaA1);
        Assert.Equal(0.2, xlsx.Worksheet("DCF").Cell("A1").GetDouble(), precision: 10);
    }

    [Fact]
    public void ExportaFormatoDeNumeroSemTraducao()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(-1000) with { NumberFormat = "#,##0;(#,##0)" });

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        Assert.Equal("#,##0;(#,##0)", xlsx.Worksheet("DCF").Cell("A1").Style.NumberFormat.Format);
    }

    [Fact]
    public void CelulaDeFormulaSemCorManualExportaFontePreta()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(1));
        sheet.SetCell(CellAddress.Parse("A2"), Cell.FromFormula("A1*2"));

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        XLColor color = xlsx.Worksheet("DCF").Cell("A2").Style.Font.FontColor;
        Assert.Equal(new RgbColor(0, 0, 0), new RgbColor(color.Color.R, color.Color.G, color.Color.B));
    }

    [Fact]
    public void CelulaDeEntradaManualSemCorExplicitaExportaFonteAzul()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(8));

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        XLColor color = xlsx.Worksheet("DCF").Cell("A1").Style.Font.FontColor;
        Assert.Equal(new RgbColor(0, 0, 255), new RgbColor(color.Color.R, color.Color.G, color.Color.B));
    }

    [Fact]
    public void CorManualDaFonteSobrepoeAConvencaoAutomatica()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(
            CellAddress.Parse("A1"),
            Cell.FromNumber(8) with { Style = CellStyle.Default with { FontColor = new RgbColor(200, 0, 0) } });

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        XLColor color = xlsx.Worksheet("DCF").Cell("A1").Style.Font.FontColor;
        Assert.Equal(new RgbColor(200, 0, 0), new RgbColor(color.Color.R, color.Color.G, color.Color.B));
    }

    [Fact]
    public void ExportaEstiloDeFonteEPreenchimento()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(
            CellAddress.Parse("A1"),
            Cell.FromNumber(1) with
            {
                Style = CellStyle.Default with
                {
                    Bold = true,
                    Italic = true,
                    Underline = true,
                    FontFamily = "Arial",
                    FontSize = 14,
                    BackgroundColor = new RgbColor(255, 255, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                },
            });

        using XLWorkbook xlsx = ExportAndReopen(workbook);
        IXLStyle style = xlsx.Worksheet("DCF").Cell("A1").Style;

        Assert.True(style.Font.Bold);
        Assert.True(style.Font.Italic);
        Assert.Equal(XLFontUnderlineValues.Single, style.Font.Underline);
        Assert.Equal("Arial", style.Font.FontName);
        Assert.Equal(14, style.Font.FontSize);
        Assert.Equal(XLAlignmentHorizontalValues.Right, style.Alignment.Horizontal);
        Assert.Equal(XLAlignmentVerticalValues.Top, style.Alignment.Vertical);

        XLColor fill = style.Fill.BackgroundColor;
        Assert.Equal(new RgbColor(255, 255, 0), new RgbColor(fill.Color.R, fill.Color.G, fill.Color.B));
    }

    [Fact]
    public void ExportaBordaDupla_ConvencaoDeLinhaDeTotalGeral()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(
            CellAddress.Parse("A1"),
            Cell.FromNumber(1) with
            {
                Style = CellStyle.Default with
                {
                    Borders = new CellBorders(
                        BorderEdge.None,
                        BorderEdge.None,
                        new BorderEdge(BorderLineStyle.Double, RgbColor.Black),
                        BorderEdge.None),
                },
            });

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        Assert.Equal(XLBorderStyleValues.Double, xlsx.Worksheet("DCF").Cell("A1").Style.Border.BottomBorder);
    }

    [Fact]
    public void ExportaPaineisCongelados()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.FrozenRows = 2;
        sheet.FrozenColumns = 1;
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(1));

        using XLWorkbook xlsx = ExportAndReopen(workbook);
        IXLSheetView view = xlsx.Worksheet("DCF").SheetView;

        Assert.Equal(2, view.SplitRow);
        Assert.Equal(1, view.SplitColumn);
    }

    [Fact]
    public void CelulaEmBrancoNaoEntraNoArquivo()
    {
        Workbook workbook = CreateWorkbook(out Worksheet sheet);
        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromNumber(1));

        using XLWorkbook xlsx = ExportAndReopen(workbook);

        Assert.True(xlsx.Worksheet("DCF").Cell("B1").IsEmpty());
    }

    [Fact]
    public void ReferenciaCircularRecusaAExportacao()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("DCF");
        var engine = new Nordxcel.Core.Calculation.CalculationEngine(workbook);
        engine.SetFormula(new CellLocation("DCF", CellAddress.Parse("A1")), "B1");
        engine.SetFormula(new CellLocation("DCF", CellAddress.Parse("B1")), "A1");

        var exception = Assert.Throws<XlsxExportException>(() => XlsxExporter.Export(workbook, _path));

        Assert.Contains("circular", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(_path));
    }
}
