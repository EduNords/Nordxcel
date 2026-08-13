using System.Text.Json;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Persistence;

/// <summary>Arquivo que não é um <c>.nxcl</c> válido, ou é de uma versão mais nova que este Nordxcel entende.</summary>
public sealed class WorkbookFormatException : Exception
{
    public WorkbookFormatException(string message) : base(message)
    {
    }

    public WorkbookFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Salva e abre uma <see cref="Workbook"/> no formato nativo do Nordxcel — JSON,
/// com fidelidade total ao modelo: fórmula, estilo, formato de número, painéis
/// congelados, largura de coluna e altura de linha. Não é o <c>.xlsx</c>; essa é
/// a exportação, uma fase própria e futura do roadmap.
/// </summary>
public static class WorkbookSerializer
{
    /// <summary>Maior versão de formato que este Nordxcel sabe ler.</summary>
    private const int CurrentFormatVersion = 1;

    /// <summary>
    /// Só a chave da propriedade vira camelCase — os nomes de enum (estilo de
    /// borda, alinhamento, tipo de erro) são strings simples nos documentos,
    /// escritas e lidas à mão via <c>ToString()</c>/<c>Enum.TryParse</c>, então
    /// nenhum conversor de enum entra aqui.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Sem isso o Text.Json escreve "style": null em toda célula sem estilo
        // customizado — a maioria — em vez de simplesmente omitir a chave.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        WorkbookDocument document = ToDocument(workbook);

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static void SerializeToFile(Workbook workbook, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        File.WriteAllText(path, Serialize(workbook));
    }

    /// <exception cref="WorkbookFormatException">Quando o texto não é um <c>.nxcl</c> válido.</exception>
    public static Workbook Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        WorkbookDocument document;

        try
        {
            document = JsonSerializer.Deserialize<WorkbookDocument>(json, JsonOptions)
                ?? throw new WorkbookFormatException("O arquivo está vazio.");
        }
        catch (JsonException exception)
        {
            throw new WorkbookFormatException("O arquivo não é um modelo Nordxcel válido.", exception);
        }

        if (document.FormatVersion > CurrentFormatVersion)
        {
            throw new WorkbookFormatException(
                $"Este arquivo foi salvo por uma versão mais nova do Nordxcel (formato {document.FormatVersion}); " +
                $"esta versão só entende até o formato {CurrentFormatVersion}.");
        }

        return FromDocument(document);
    }

    /// <inheritdoc cref="Deserialize(string)"/>
    public static Workbook DeserializeFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Deserialize(File.ReadAllText(path));
    }

    // ------------------------------------------------------------- domínio → documento

    private static WorkbookDocument ToDocument(Workbook workbook)
    {
        var document = new WorkbookDocument { FormatVersion = CurrentFormatVersion };

        foreach (Worksheet sheet in workbook.Worksheets)
        {
            document.Worksheets.Add(ToDocument(sheet));
        }

        return document;
    }

    private static WorksheetDocument ToDocument(Worksheet sheet)
    {
        var document = new WorksheetDocument
        {
            Name = sheet.Name,
            FrozenRows = sheet.FrozenRows,
            FrozenColumns = sheet.FrozenColumns,
            ColumnWidths = new Dictionary<int, double>(sheet.ColumnWidths),
            RowHeights = new Dictionary<int, double>(sheet.RowHeights),
        };

        foreach ((CellAddress address, Cell cell) in sheet.Cells)
        {
            document.Cells.Add(ToDocument(address, cell));
        }

        return document;
    }

    private static CellDocument ToDocument(CellAddress address, Cell cell) => new()
    {
        Row = address.Row,
        Column = address.Column,
        Formula = cell.Formula,
        // O valor de uma fórmula é recalculado ao abrir; só vale a pena salvar
        // o de uma entrada literal, e mesmo assim só quando não está em branco.
        Value = cell.HasFormula || cell.Value.IsBlank ? null : ToDocument(cell.Value),
        NumberFormat = cell.NumberFormat,
        Style = cell.Style.IsDefault ? null : ToDocument(cell.Style),
    };

    private static CellValueDocument ToDocument(CellValue value) => value.Kind switch
    {
        CellValueKind.Number => new CellValueDocument { Kind = nameof(CellValueKind.Number), Number = value.AsNumber() },
        CellValueKind.Text => new CellValueDocument { Kind = nameof(CellValueKind.Text), Text = value.AsText() },
        CellValueKind.Logical => new CellValueDocument { Kind = nameof(CellValueKind.Logical), Logical = value.AsLogical() },
        CellValueKind.Error => new CellValueDocument { Kind = nameof(CellValueKind.Error), Error = value.AsError().ToString() },
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Tipo de valor não mapeado para o arquivo."),
    };

    private static CellStyleDocument ToDocument(CellStyle style) => new()
    {
        FontFamily = style.FontFamily,
        FontSize = style.FontSize,
        Bold = style.Bold,
        Italic = style.Italic,
        Underline = style.Underline,
        FontColor = style.FontColor?.ToString(),
        BackgroundColor = style.BackgroundColor?.ToString(),
        HorizontalAlignment = style.HorizontalAlignment.ToString(),
        VerticalAlignment = style.VerticalAlignment.ToString(),
        BorderTop = ToDocument(style.Borders.Top),
        BorderRight = ToDocument(style.Borders.Right),
        BorderBottom = ToDocument(style.Borders.Bottom),
        BorderLeft = ToDocument(style.Borders.Left),
        IndentLevel = style.IndentLevel,
    };

    private static BorderEdgeDocument? ToDocument(BorderEdge edge) =>
        edge.IsVisible
            ? new BorderEdgeDocument { Style = edge.Style.ToString(), Color = edge.Color.ToString() }
            : null;

    // ------------------------------------------------------------- documento → domínio

    private static Workbook FromDocument(WorkbookDocument document)
    {
        var workbook = new Workbook();

        foreach (WorksheetDocument sheetDocument in document.Worksheets)
        {
            FromDocument(workbook, sheetDocument);
        }

        return workbook;
    }

    private static void FromDocument(Workbook workbook, WorksheetDocument document)
    {
        Worksheet sheet;

        try
        {
            sheet = workbook.AddWorksheet(document.Name);
        }
        catch (ArgumentException exception)
        {
            throw new WorkbookFormatException($"Nome de aba inválido no arquivo: '{document.Name}'.", exception);
        }

        sheet.FrozenRows = Math.Clamp(document.FrozenRows, 0, CellAddress.MaxRows);
        sheet.FrozenColumns = Math.Clamp(document.FrozenColumns, 0, CellAddress.MaxColumns);

        foreach ((int column, double width) in document.ColumnWidths)
        {
            sheet.SetColumnWidth(column, width);
        }

        foreach ((int row, double height) in document.RowHeights)
        {
            sheet.SetRowHeight(row, height);
        }

        foreach (CellDocument cellDocument in document.Cells)
        {
            CellAddress address;

            try
            {
                address = new CellAddress(cellDocument.Row, cellDocument.Column);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new WorkbookFormatException(
                    $"Endereço de célula inválido na aba '{document.Name}': linha {cellDocument.Row}, coluna {cellDocument.Column}.",
                    exception);
            }

            sheet.SetCell(address, FromDocument(cellDocument));
        }
    }

    private static Cell FromDocument(CellDocument document) => new()
    {
        Formula = document.Formula,
        Value = document.Value is null ? CellValue.Blank : FromDocument(document.Value),
        NumberFormat = document.NumberFormat,
        Style = document.Style is null ? CellStyle.Default : FromDocument(document.Style),
    };

    private static CellValue FromDocument(CellValueDocument document)
    {
        if (!Enum.TryParse(document.Kind, out CellValueKind kind))
        {
            throw new WorkbookFormatException($"Tipo de valor desconhecido no arquivo: '{document.Kind}'.");
        }

        return kind switch
        {
            CellValueKind.Number => CellValue.Number(document.Number ?? 0d),
            CellValueKind.Text => CellValue.Text(document.Text ?? string.Empty),
            CellValueKind.Logical => CellValue.Logical(document.Logical ?? false),
            CellValueKind.Error => CellValue.Error(ParseEnum<CellErrorType>(document.Error, "erro")),
            _ => CellValue.Blank,
        };
    }

    private static CellStyle FromDocument(CellStyleDocument document) => new()
    {
        FontFamily = document.FontFamily,
        FontSize = document.FontSize,
        Bold = document.Bold,
        Italic = document.Italic,
        Underline = document.Underline,
        FontColor = ParseColor(document.FontColor),
        BackgroundColor = ParseColor(document.BackgroundColor),
        HorizontalAlignment = ParseEnum<HorizontalAlignment>(document.HorizontalAlignment, "alinhamento horizontal"),
        VerticalAlignment = ParseEnum<VerticalAlignment>(document.VerticalAlignment, "alinhamento vertical"),
        Borders = new CellBorders(
            FromDocument(document.BorderTop),
            FromDocument(document.BorderRight),
            FromDocument(document.BorderBottom),
            FromDocument(document.BorderLeft)),
        IndentLevel = document.IndentLevel,
    };

    private static BorderEdge FromDocument(BorderEdgeDocument? document)
    {
        if (document is null)
        {
            return BorderEdge.None;
        }

        return new BorderEdge(
            ParseEnum<BorderLineStyle>(document.Style, "estilo de borda"),
            RgbColor.TryParse(document.Color, out RgbColor color)
                ? color
                : throw new WorkbookFormatException($"Cor de borda inválida no arquivo: '{document.Color}'."));
    }

    private static RgbColor? ParseColor(string? hex)
    {
        if (hex is null)
        {
            return null;
        }

        return RgbColor.TryParse(hex, out RgbColor color)
            ? color
            : throw new WorkbookFormatException($"Cor inválida no arquivo: '{hex}'.");
    }

    private static T ParseEnum<T>(string? value, string description) where T : struct, Enum
    {
        if (value is not null && Enum.TryParse(value, out T parsed))
        {
            return parsed;
        }

        throw new WorkbookFormatException($"Valor de {description} desconhecido no arquivo: '{value}'.");
    }
}
