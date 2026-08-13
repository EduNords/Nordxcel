using ClosedXML.Excel;
using Nordxcel.Core.Calculation;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Export;

/// <summary>A pasta não pôde ser exportada para <c>.xlsx</c> como está.</summary>
public sealed class XlsxExportException : Exception
{
    public XlsxExportException(string message) : base(message)
    {
    }
}

/// <summary>
/// Exporta uma <see cref="Workbook"/> para <c>.xlsx</c> real, via ClosedXML —
/// não o formato nativo do Nordxcel (isso é <see cref="Persistence.WorkbookSerializer"/>).
/// Preserva fórmula, valor, formato de número, estilo e painéis congelados.
/// </summary>
public static class XlsxExporter
{
    /// <summary>
    /// Pixels lógicos → pontos, a unidade de altura de linha do Excel.
    /// <c>20px</c> (padrão do Nordxcel) vira <c>15pt</c>, que por acaso é
    /// exatamente a altura de linha padrão do próprio Excel para Calibri 11 —
    /// então a maioria das planilhas exporta sem nenhum ajuste visível.
    /// </summary>
    private const double PixelsToPointsFactor = 0.75d;

    /// <summary>
    /// Pixels lógicos → "caracteres", a unidade de largura de coluna do Excel.
    /// Não existe fórmula exata — o próprio Excel arredonda em função da fonte
    /// padrão da pasta — então isto é uma aproximação deliberada; largura de
    /// coluna é cosmético, não afeta nenhum número do modelo.
    /// </summary>
    private const double PixelsToColumnWidthDivisor = 7d;

    private const double PixelsToColumnWidthOffset = 5d;

    public static void Export(Workbook workbook, string path)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var engine = new CalculationEngine(workbook);

        if (engine.HasCircularReferences)
        {
            throw new XlsxExportException(
                $"A pasta tem {engine.CircularCells.Count} célula(s) em referência circular (#CIRC!). " +
                "O Excel não tem esse erro — corrija o ciclo antes de exportar, ou o arquivo sairia com números incorretos.");
        }

        using var xlsx = new XLWorkbook();

        foreach (Worksheet sheet in workbook.Worksheets)
        {
            IXLWorksheet xlSheet = xlsx.Worksheets.Add(sheet.Name);
            ExportSheet(sheet, xlSheet, engine);
        }

        xlsx.SaveAs(path);
    }

    private static void ExportSheet(Worksheet sheet, IXLWorksheet xlSheet, CalculationEngine engine)
    {
        foreach ((CellAddress address, Cell cell) in sheet.Cells)
        {
            ExportCell(sheet.Name, address, cell, xlSheet, engine);
        }

        foreach ((int column, double width) in sheet.ColumnWidths)
        {
            xlSheet.Column(column + 1).Width = Math.Max(1d, (width - PixelsToColumnWidthOffset) / PixelsToColumnWidthDivisor);
        }

        foreach ((int row, double height) in sheet.RowHeights)
        {
            xlSheet.Row(row + 1).Height = height * PixelsToPointsFactor;
        }

        if (sheet.FrozenRows > 0 || sheet.FrozenColumns > 0)
        {
            xlSheet.SheetView.Freeze(sheet.FrozenRows, sheet.FrozenColumns);
        }
    }

    private static void ExportCell(string sheetName, CellAddress address, Cell cell, IXLWorksheet xlSheet, CalculationEngine engine)
    {
        IXLCell xlCell = xlSheet.Cell(address.Row + 1, address.Column + 1);
        var location = new CellLocation(sheetName, address);
        FormulaNode? formula = cell.HasFormula ? engine.GetFormula(location) : null;

        if (cell.HasFormula)
        {
            if (formula is null)
            {
                // Só acontece com fórmula inválida sobrevivendo de um arquivo adulterado —
                // a entrada normal já recusa gravar uma fórmula que não interpreta.
                throw new XlsxExportException($"A fórmula de {location} não pôde ser interpretada; corrija a célula antes de exportar.");
            }

            xlCell.FormulaA1 = FormulaWriter.Write(formula, FormulaSyntax.EnUs);
        }
        else
        {
            WriteLiteralValue(xlCell, cell.Value);
        }

        if (cell.NumberFormat is not null)
        {
            xlCell.Style.NumberFormat.Format = cell.NumberFormat;
        }

        ApplyStyle(xlCell, cell, formula, sheetName);
    }

    private static void WriteLiteralValue(IXLCell xlCell, CellValue value)
    {
        switch (value.Kind)
        {
            case CellValueKind.Number:
                xlCell.Value = value.AsNumber();
                break;

            case CellValueKind.Text:
                xlCell.Value = value.AsText();
                break;

            case CellValueKind.Logical:
                xlCell.Value = value.AsLogical();
                break;

            case CellValueKind.Error:
                xlCell.Value = ToXlError(value.AsError());
                break;

            case CellValueKind.Blank:
            default:
                break;
        }
    }

    private static XLError ToXlError(CellErrorType error) => error switch
    {
        CellErrorType.DivideByZero => XLError.DivisionByZero,
        CellErrorType.Value => XLError.IncompatibleValue,
        CellErrorType.Reference => XLError.CellReference,
        CellErrorType.Name => XLError.NameNotRecognized,
        CellErrorType.Number => XLError.NumberInvalid,
        CellErrorType.NotAvailable => XLError.NoValueAvailable,
        CellErrorType.Null => XLError.NullValue,
        _ => throw new XlsxExportException(
            $"O erro '{error.ToDisplayText()}' não existe no Excel e não pode ser exportado como valor de célula."),
    };

    /// <summary>
    /// A cor da fonte azul/preta/verde é decidida em tempo de renderização pelo
    /// <see cref="CellColorClassifier"/>, não fica gravada na célula — então
    /// precisa ser resolvida e gravada explicitamente aqui, ou o arquivo
    /// exportado perderia a convenção inteira e mostraria tudo em preto.
    /// </summary>
    private static void ApplyStyle(IXLCell xlCell, Cell cell, FormulaNode? formula, string sheetName)
    {
        CellStyle style = cell.Style;
        IXLStyle xlStyle = xlCell.Style;

        xlStyle.Font.FontName = style.FontFamily;
        xlStyle.Font.FontSize = style.FontSize;
        xlStyle.Font.Bold = style.Bold;
        xlStyle.Font.Italic = style.Italic;
        xlStyle.Font.Underline = style.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;

        CellColorRole role = CellColorClassifier.Classify(cell, formula, sheetName);
        RgbColor fontColor = CellColorClassifier.ResolveFontColor(style, role);
        xlStyle.Font.FontColor = ToXlColor(fontColor);

        if (style.BackgroundColor is { } background)
        {
            xlStyle.Fill.BackgroundColor = ToXlColor(background);
        }

        xlStyle.Alignment.Horizontal = style.HorizontalAlignment switch
        {
            HorizontalAlignment.Left => XLAlignmentHorizontalValues.Left,
            HorizontalAlignment.Center => XLAlignmentHorizontalValues.Center,
            HorizontalAlignment.Right => XLAlignmentHorizontalValues.Right,
            _ => XLAlignmentHorizontalValues.General,
        };

        xlStyle.Alignment.Vertical = style.VerticalAlignment switch
        {
            VerticalAlignment.Top => XLAlignmentVerticalValues.Top,
            VerticalAlignment.Center => XLAlignmentVerticalValues.Center,
            _ => XLAlignmentVerticalValues.Bottom,
        };

        if (style.IndentLevel > 0)
        {
            xlStyle.Alignment.Indent = style.IndentLevel;
        }

        if (style.Borders.Top.IsVisible)
        {
            xlStyle.Border.TopBorder = ToXlBorderStyle(style.Borders.Top.Style);
            xlStyle.Border.TopBorderColor = ToXlColor(style.Borders.Top.Color);
        }

        if (style.Borders.Right.IsVisible)
        {
            xlStyle.Border.RightBorder = ToXlBorderStyle(style.Borders.Right.Style);
            xlStyle.Border.RightBorderColor = ToXlColor(style.Borders.Right.Color);
        }

        if (style.Borders.Bottom.IsVisible)
        {
            xlStyle.Border.BottomBorder = ToXlBorderStyle(style.Borders.Bottom.Style);
            xlStyle.Border.BottomBorderColor = ToXlColor(style.Borders.Bottom.Color);
        }

        if (style.Borders.Left.IsVisible)
        {
            xlStyle.Border.LeftBorder = ToXlBorderStyle(style.Borders.Left.Style);
            xlStyle.Border.LeftBorderColor = ToXlColor(style.Borders.Left.Color);
        }
    }

    private static XLBorderStyleValues ToXlBorderStyle(BorderLineStyle style) => style switch
    {
        BorderLineStyle.Thin => XLBorderStyleValues.Thin,
        BorderLineStyle.Medium => XLBorderStyleValues.Medium,
        BorderLineStyle.Thick => XLBorderStyleValues.Thick,
        BorderLineStyle.Double => XLBorderStyleValues.Double,
        _ => XLBorderStyleValues.None,
    };

    private static XLColor ToXlColor(RgbColor color) => XLColor.FromArgb(color.R, color.G, color.B);
}
