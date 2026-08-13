using ClosedXML.Excel;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Import;

/// <summary>O arquivo não pôde ser aberto ou lido como <c>.xlsx</c>.</summary>
public sealed class XlsxImportException : Exception
{
    public XlsxImportException(string message) : base(message)
    {
    }

    public XlsxImportException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Pasta importada mais o relatório do que não veio: fórmula com função sem
/// equivalente no Nordxcel (a célula veio como fórmula mesmo assim — vira
/// <c>#NOME?</c> sozinha ao calcular, o mesmo aviso que qualquer fórmula
/// inválida já dá) e recursos ignorados de propósito (gráfico, imagem, tabela
/// do Excel, validação de dados, formatação condicional — fora do escopo do
/// MVP, mesma linha do roadmap que já deixa "Gráficos" de fora).
/// </summary>
public sealed class XlsxImportResult
{
    public required Workbook Workbook { get; init; }

    /// <summary>Nome da função (em inglês, como veio no arquivo) → quantas células a usam.</summary>
    public required IReadOnlyDictionary<string, int> UnsupportedFunctions { get; init; }

    /// <summary>
    /// Nome definido (intervalo nomeado, tipo <c>WACC</c> em vez de
    /// <c>Premissas!$B$3</c>) → quantas células referenciam esse nome. O
    /// Nordxcel ainda não tem nome definido — vira <c>#NOME?</c>, o mesmo
    /// aviso de sempre para referência não resolvida.
    /// </summary>
    public required IReadOnlyDictionary<string, int> UnrecognizedNames { get; init; }

    /// <summary>
    /// Células cuja fórmula nem chegou a interpretar — fórmula de matriz
    /// (<c>{=...}</c>, como a Tabela de Dados do próprio Excel) ou célula de
    /// "derramamento" de uma função dinâmica. Também vira <c>#NOME?</c>.
    /// </summary>
    public required int UnparseableFormulaCount { get; init; }

    public required int SkippedFeatureCount { get; init; }
}

/// <summary>
/// Importa um <c>.xlsx</c> de verdade para uma <see cref="Workbook"/> do
/// Nordxcel — o caminho inverso de <see cref="Export.XlsxExporter"/>. Espelha
/// cada decisão da exportação: fórmula (via <see cref="FormulaSyntax.EnUs"/> +
/// <see cref="FormulaSyntaxTranslator"/>), valor, formato de número (sem
/// tradução — mesma sintaxe de máscara nos dois), estilo completo, largura de
/// coluna/altura de linha (aproximação inversa da mesma conversão da
/// exportação) e painéis congelados.
/// </summary>
public static class XlsxImporter
{
    /// <summary>Inverso de <see cref="Export.XlsxExporter"/>: pontos → pixels lógicos.</summary>
    private const double PixelsToPointsFactor = 0.75d;

    /// <summary>Inverso de <see cref="Export.XlsxExporter"/>: unidade "caracteres" do Excel → pixels lógicos.</summary>
    private const double PixelsToColumnWidthDivisor = 7d;

    private const double PixelsToColumnWidthOffset = 5d;

    /// <summary>Contadores acumulados durante a importação, para virar <see cref="XlsxImportResult"/> no fim.</summary>
    private sealed class ImportReport
    {
        public Dictionary<string, int> UnsupportedFunctions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> UnrecognizedNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int UnparseableFormulaCount { get; set; }

        public int SkippedFeatureCount { get; set; }
    }

    public static XlsxImportResult Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        XLWorkbook xlsx;

        try
        {
            xlsx = new XLWorkbook(path);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new XlsxImportException($"'{Path.GetFileName(path)}' não pôde ser aberto como .xlsx: {exception.Message}", exception);
        }

        using (xlsx)
        {
            var workbook = new Workbook();
            var report = new ImportReport();

            foreach (IXLWorksheet xlSheet in xlsx.Worksheets)
            {
                Worksheet sheet = AddSheetSafely(workbook, xlSheet.Name);
                ImportSheet(xlSheet, sheet, report, xlsx.Theme);

                try
                {
                    report.SkippedFeatureCount += xlSheet.MergedRanges.Count;
                }
                catch
                {
                    // Só conta pro relatório; um recurso que a lib não consiga listar
                    // não pode derrubar a importação inteira.
                }
            }

            return new XlsxImportResult
            {
                Workbook = workbook,
                UnsupportedFunctions = report.UnsupportedFunctions,
                UnrecognizedNames = report.UnrecognizedNames,
                UnparseableFormulaCount = report.UnparseableFormulaCount,
                SkippedFeatureCount = report.SkippedFeatureCount,
            };
        }
    }

    /// <summary>
    /// O Excel já segue quase as mesmas regras de nome que
    /// <see cref="Worksheet.ValidateName"/> exige, mas nome inválido ou
    /// duplicado (depois de sanitizar) não pode travar a importação inteira —
    /// nesse caso raro, cai para um nome gerado.
    /// </summary>
    private static Worksheet AddSheetSafely(Workbook workbook, string desiredName)
    {
        try
        {
            return workbook.AddWorksheet(desiredName);
        }
        catch (ArgumentException)
        {
            return workbook.AddWorksheet(workbook.CreateUniqueWorksheetName("Planilha"));
        }
    }

    private static void ImportSheet(IXLWorksheet xlSheet, Worksheet sheet, ImportReport report, IXLTheme theme)
    {
        foreach (IXLCell xlCell in xlSheet.CellsUsed())
        {
            int row = xlCell.Address.RowNumber - 1;
            int column = xlCell.Address.ColumnNumber - 1;

            if (row < 0 || row >= CellAddress.MaxRows || column < 0 || column >= CellAddress.MaxColumns)
            {
                continue;
            }

            sheet.SetCell(new CellAddress(row, column), ImportCell(xlCell, sheet.Name, report, theme));
        }

        ImportGeometry(xlSheet, sheet);
    }

    private static void ImportGeometry(IXLWorksheet xlSheet, Worksheet sheet)
    {
        double defaultColumnWidth = xlSheet.ColumnWidth;

        foreach (IXLColumn column in xlSheet.ColumnsUsed())
        {
            int index = column.ColumnNumber() - 1;

            if (index < 0 || index >= CellAddress.MaxColumns || Math.Abs(column.Width - defaultColumnWidth) < 0.01)
            {
                continue;
            }

            double pixels = column.Width * PixelsToColumnWidthDivisor + PixelsToColumnWidthOffset;
            sheet.SetColumnWidth(index, Math.Max(1d, pixels));
        }

        double defaultRowHeight = xlSheet.RowHeight;

        foreach (IXLRow row in xlSheet.RowsUsed())
        {
            int index = row.RowNumber() - 1;

            if (index < 0 || index >= CellAddress.MaxRows || Math.Abs(row.Height - defaultRowHeight) < 0.01)
            {
                continue;
            }

            sheet.SetRowHeight(index, Math.Max(1d, row.Height / PixelsToPointsFactor));
        }

        sheet.FrozenRows = Math.Clamp(xlSheet.SheetView.SplitRow, 0, CellAddress.MaxRows);
        sheet.FrozenColumns = Math.Clamp(xlSheet.SheetView.SplitColumn, 0, CellAddress.MaxColumns);
    }

    private static Cell ImportCell(IXLCell xlCell, string sheetName, ImportReport report, IXLTheme theme)
    {
        string? formulaText = null;
        FormulaNode? formulaNode = null;
        CellValue value = CellValue.Blank;

        if (xlCell.HasFormula)
        {
            (formulaText, formulaNode) = TranslateFormula(xlCell.FormulaA1, report);
        }
        else
        {
            value = ReadLiteralValue(xlCell);
        }

        string? numberFormat = xlCell.Style.NumberFormat.Format;

        if (string.IsNullOrEmpty(numberFormat) || string.Equals(numberFormat, "General", StringComparison.OrdinalIgnoreCase))
        {
            numberFormat = null;
        }

        return new Cell
        {
            Formula = formulaText,
            Value = value,
            NumberFormat = numberFormat,
            Style = ImportStyle(xlCell.Style, formulaNode, value, sheetName, theme),
        };
    }

    /// <summary>
    /// Interpreta com <see cref="FormulaSyntax.EnUs"/> e traduz nome de função
    /// conhecida para o português. Fórmula que nem chega a parsear — fórmula de
    /// matriz entre chaves (a Tabela de Dados do próprio Excel usa essa
    /// sintaxe), célula de "derramamento" de função dinâmica, referência
    /// estruturada de tabela — entra como texto original: o motor de cálculo
    /// recusa ao carregar e a célula vira <c>#NOME?</c> sozinha, sem lançar
    /// exceção nem derrubar a importação.
    /// </summary>
    private static (string Text, FormulaNode? Node) TranslateFormula(string formulaA1, ImportReport report)
    {
        FormulaNode node;

        try
        {
            node = new FormulaParser(FormulaSyntax.EnUs).Parse(formulaA1);
        }
        catch (FormulaSyntaxException)
        {
            report.UnparseableFormulaCount++;
            return (formulaA1, null);
        }

        var unknownFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FormulaNode translated = FormulaSyntaxTranslator.ToDefault(node, FormulaSyntax.EnUs, unknownFunctions);

        foreach (string name in unknownFunctions)
        {
            report.UnsupportedFunctions[name] = report.UnsupportedFunctions.GetValueOrDefault(name) + 1;
        }

        var unresolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectNames(translated, unresolvedNames);

        foreach (string name in unresolvedNames)
        {
            report.UnrecognizedNames[name] = report.UnrecognizedNames.GetValueOrDefault(name) + 1;
        }

        return (FormulaWriter.Write(translated, FormulaSyntax.Default), translated);
    }

    /// <summary>
    /// Junta todo <see cref="NameNode"/> da árvore — nome definido (intervalo
    /// nomeado, tipo <c>WACC</c>) que o Excel resolveria sozinho, mas o
    /// Nordxcel ainda não tem onde procurar. Comuníssimo em modelo de banco de
    /// verdade; vale destacar separado de função não suportada no relatório,
    /// já que a causa e a correção são bem diferentes.
    /// </summary>
    private static void CollectNames(FormulaNode node, HashSet<string> names)
    {
        switch (node)
        {
            case NameNode name:
                names.Add(name.Name);
                break;

            case UnaryNode unary:
                CollectNames(unary.Operand, names);
                break;

            case BinaryNode binary:
                CollectNames(binary.Left, names);
                CollectNames(binary.Right, names);
                break;

            case FunctionNode function:
                foreach (FormulaNode argument in function.Arguments)
                {
                    CollectNames(argument, names);
                }

                break;
        }
    }

    private static CellValue ReadLiteralValue(IXLCell xlCell)
    {
        if (xlCell.IsEmpty())
        {
            return CellValue.Blank;
        }

        return xlCell.DataType switch
        {
            XLDataType.Number => CellValue.Number(xlCell.GetDouble()),
            XLDataType.Text => CellValue.Text(xlCell.GetString()),
            XLDataType.Boolean => CellValue.Logical(xlCell.GetBoolean()),
            XLDataType.DateTime => CellValue.Number(ExcelDate.ToSerial(xlCell.GetDateTime())),
            XLDataType.Error => CellValue.Error(FromXlError(xlCell.GetError())),
            _ => CellValue.Text(xlCell.GetString()),
        };
    }

    private static CellErrorType FromXlError(XLError error) => error switch
    {
        XLError.DivisionByZero => CellErrorType.DivideByZero,
        XLError.IncompatibleValue => CellErrorType.Value,
        XLError.CellReference => CellErrorType.Reference,
        XLError.NameNotRecognized => CellErrorType.Name,
        XLError.NumberInvalid => CellErrorType.Number,
        XLError.NoValueAvailable => CellErrorType.NotAvailable,
        XLError.NullValue => CellErrorType.Null,
        _ => CellErrorType.Value,
    };

    private static CellStyle ImportStyle(IXLStyle xlStyle, FormulaNode? formula, CellValue value, string sheetName, IXLTheme theme)
    {
        CellColorRole role = CellColorClassifier.Classify(formula, value, sheetName);

        RgbColor? fontColor = FromXlColor(xlStyle.Font.FontColor, theme);
        RgbColor? backgroundColor = xlStyle.Fill.BackgroundColor == XLColor.NoColor ? null : FromXlColor(xlStyle.Fill.BackgroundColor, theme);

        return new CellStyle
        {
            FontFamily = string.IsNullOrWhiteSpace(xlStyle.Font.FontName) ? CellStyle.Default.FontFamily : xlStyle.Font.FontName,
            FontSize = xlStyle.Font.FontSize > 0 ? xlStyle.Font.FontSize : CellStyle.Default.FontSize,
            Bold = xlStyle.Font.Bold,
            Italic = xlStyle.Font.Italic,
            Underline = xlStyle.Font.Underline != XLFontUnderlineValues.None,
            // Cor que já bate com o que o Nordxcel atribuiria sozinho (convenção
            // azul/preto/verde — muito comum nesses modelos de IB de verdade)
            // fica automática, pra célula continuar se recolorindo sozinha se a
            // fórmula mudar depois. Cor genuinamente diferente entra como
            // escolha manual, preservada tal e qual estava no arquivo.
            FontColor = fontColor == CellColorClassifier.ColorOf(role) ? null : fontColor,
            BackgroundColor = backgroundColor,
            HorizontalAlignment = xlStyle.Alignment.Horizontal switch
            {
                XLAlignmentHorizontalValues.Left => HorizontalAlignment.Left,
                XLAlignmentHorizontalValues.Center => HorizontalAlignment.Center,
                XLAlignmentHorizontalValues.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.General,
            },
            VerticalAlignment = xlStyle.Alignment.Vertical switch
            {
                XLAlignmentVerticalValues.Top => VerticalAlignment.Top,
                XLAlignmentVerticalValues.Center => VerticalAlignment.Center,
                _ => VerticalAlignment.Bottom,
            },
            IndentLevel = (int)xlStyle.Alignment.Indent,
            Borders = new CellBorders(
                ImportBorder(xlStyle.Border.TopBorder, xlStyle.Border.TopBorderColor, theme),
                ImportBorder(xlStyle.Border.RightBorder, xlStyle.Border.RightBorderColor, theme),
                ImportBorder(xlStyle.Border.BottomBorder, xlStyle.Border.BottomBorderColor, theme),
                ImportBorder(xlStyle.Border.LeftBorder, xlStyle.Border.LeftBorderColor, theme)),
        };
    }

    private static BorderEdge ImportBorder(XLBorderStyleValues style, XLColor color, IXLTheme theme)
    {
        BorderLineStyle mapped = style switch
        {
            XLBorderStyleValues.Thin or XLBorderStyleValues.Hair or XLBorderStyleValues.Dotted or XLBorderStyleValues.Dashed => BorderLineStyle.Thin,
            XLBorderStyleValues.Medium or XLBorderStyleValues.MediumDashed => BorderLineStyle.Medium,
            XLBorderStyleValues.Thick => BorderLineStyle.Thick,
            XLBorderStyleValues.Double => BorderLineStyle.Double,
            _ => BorderLineStyle.None,
        };

        return mapped == BorderLineStyle.None ? BorderEdge.None : new BorderEdge(mapped, FromXlColor(color, theme));
    }

    /// <summary>
    /// <c>XLColor.Color</c> só funciona pra cor RGB/indexada literal — cor de
    /// tema (muito comum em planilha de mercado de verdade: é o padrão do
    /// próprio Excel pra preenchimento e fonte sutil) não carrega RGB nenhum
    /// sozinha, só um nome simbólico (<c>Accent2</c> etc.) mais um "tint" que
    /// clareia ou escurece — precisa resolver contra o tema da pasta e aplicar
    /// o tint à mão, o mesmo cálculo que o próprio Excel faz internamente.
    /// </summary>
    private static RgbColor FromXlColor(XLColor color, IXLTheme theme)
    {
        System.Drawing.Color resolved = color.ColorType == XLColorType.Theme
            ? ApplyTint(theme.ResolveThemeColor(color.ThemeColor).Color, color.ThemeTint)
            : color.Color;

        return new RgbColor(resolved.R, resolved.G, resolved.B);
    }

    /// <summary>
    /// Clareia (tint positivo) ou escurece (tint negativo) a cor pela luminância
    /// em HSL — a fórmula que o próprio formato OOXML define para combinar cor
    /// de tema com tint.
    /// </summary>
    private static System.Drawing.Color ApplyTint(System.Drawing.Color color, double tint)
    {
        if (tint == 0d)
        {
            return color;
        }

        (double h, double s, double l) = RgbToHsl(color);

        l = tint < 0d ? l * (1d + tint) : l * (1d - tint) + tint;

        return HslToRgb(h, s, l);
    }

    private static (double H, double S, double L) RgbToHsl(System.Drawing.Color color)
    {
        double r = color.R / 255d;
        double g = color.G / 255d;
        double b = color.B / 255d;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2d;

        if (max == min)
        {
            return (0d, 0d, l);
        }

        double delta = max - min;
        double s = l > 0.5d ? delta / (2d - max - min) : delta / (max + min);

        double h;

        if (max == r)
        {
            h = (g - b) / delta + (g < b ? 6d : 0d);
        }
        else if (max == g)
        {
            h = (b - r) / delta + 2d;
        }
        else
        {
            h = (r - g) / delta + 4d;
        }

        return (h / 6d, s, l);
    }

    private static System.Drawing.Color HslToRgb(double h, double s, double l)
    {
        l = Math.Clamp(l, 0d, 1d);

        if (s == 0d)
        {
            int gray = (int)Math.Round(l * 255d);
            return System.Drawing.Color.FromArgb(gray, gray, gray);
        }

        double q = l < 0.5d ? l * (1d + s) : l + s - l * s;
        double p = 2d * l - q;

        int red = (int)Math.Round(HueToRgb(p, q, h + 1d / 3d) * 255d);
        int green = (int)Math.Round(HueToRgb(p, q, h) * 255d);
        int blue = (int)Math.Round(HueToRgb(p, q, h - 1d / 3d) * 255d);

        return System.Drawing.Color.FromArgb(
            Math.Clamp(red, 0, 255),
            Math.Clamp(green, 0, 255),
            Math.Clamp(blue, 0, 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0d)
        {
            t += 1d;
        }

        if (t > 1d)
        {
            t -= 1d;
        }

        if (t < 1d / 6d)
        {
            return p + (q - p) * 6d * t;
        }

        if (t < 1d / 2d)
        {
            return q;
        }

        if (t < 2d / 3d)
        {
            return p + (q - p) * (2d / 3d - t) * 6d;
        }

        return p;
    }
}
