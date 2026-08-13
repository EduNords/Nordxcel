namespace Nordxcel.Core.Persistence;

/// <summary>
/// Formato de arquivo nativo do Nordxcel (<c>.nxcl</c>), em JSON. Estes tipos são
/// o contrato de gravação — deliberadamente separados do modelo de domínio
/// (<see cref="Model.Cell"/>, <see cref="Model.Styling.CellStyle"/> etc.), para o
/// formato de arquivo poder evoluir sem arrastar o domínio junto, e vice-versa.
/// <para>
/// Não é o <c>.xlsx</c> — isso é a exportação, uma fase própria do roadmap. Este
/// é o formato de salvar/abrir do dia a dia, com fidelidade total ao modelo
/// (fórmula, estilo, formato de número, painéis congelados, largura de coluna).
/// </para>
/// </summary>
internal sealed class WorkbookDocument
{
    /// <summary>
    /// Versão do formato. Existe desde a primeira versão de propósito: o dia em
    /// que o formato precisar mudar, o leitor consegue decidir como interpretar
    /// um arquivo antigo em vez de simplesmente falhar.
    /// </summary>
    public int FormatVersion { get; set; } = 1;

    public List<WorksheetDocument> Worksheets { get; set; } = [];
}

internal sealed class WorksheetDocument
{
    public string Name { get; set; } = string.Empty;

    public int FrozenRows { get; set; }

    public int FrozenColumns { get; set; }

    public Dictionary<int, double> ColumnWidths { get; set; } = [];

    public Dictionary<int, double> RowHeights { get; set; } = [];

    /// <summary>Só as células com conteúdo — o mesmo armazenamento esparso do <see cref="Model.Worksheet"/>.</summary>
    public List<CellDocument> Cells { get; set; } = [];
}

internal sealed class CellDocument
{
    public int Row { get; set; }

    public int Column { get; set; }

    /// <summary>Sem o <c>=</c> inicial, do jeito que <see cref="Model.Cell.Formula"/> guarda.</summary>
    public string? Formula { get; set; }

    /// <summary>
    /// <c>null</c> quando a célula está em branco ou quando tem fórmula — o valor
    /// de uma fórmula é recalculado ao abrir o arquivo, não precisa ser salvo.
    /// </summary>
    public CellValueDocument? Value { get; set; }

    public string? NumberFormat { get; set; }

    /// <summary><c>null</c> quando o estilo é o padrão — a grande maioria das células.</summary>
    public CellStyleDocument? Style { get; set; }
}

internal sealed class CellValueDocument
{
    /// <summary><c>Number</c>, <c>Text</c>, <c>Logical</c> ou <c>Error</c> — o nome do <see cref="Model.CellValueKind"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public double? Number { get; set; }

    public string? Text { get; set; }

    public bool? Logical { get; set; }

    /// <summary>Nome do <see cref="Model.CellErrorType"/>, como <c>DivideByZero</c>.</summary>
    public string? Error { get; set; }
}

internal sealed class CellStyleDocument
{
    public string FontFamily { get; set; } = "Calibri";

    public double FontSize { get; set; } = 11d;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public bool Underline { get; set; }

    /// <summary><c>#RRGGBB</c>, ou <c>null</c> para a cor automática do sistema azul/preto/verde.</summary>
    public string? FontColor { get; set; }

    /// <summary><c>#RRGGBB</c>, ou <c>null</c> para sem preenchimento.</summary>
    public string? BackgroundColor { get; set; }

    public string HorizontalAlignment { get; set; } = "General";

    public string VerticalAlignment { get; set; } = "Bottom";

    public BorderEdgeDocument? BorderTop { get; set; }

    public BorderEdgeDocument? BorderRight { get; set; }

    public BorderEdgeDocument? BorderBottom { get; set; }

    public BorderEdgeDocument? BorderLeft { get; set; }

    public int IndentLevel { get; set; }
}

internal sealed class BorderEdgeDocument
{
    /// <summary>Nome do <see cref="Model.Styling.BorderLineStyle"/>, como <c>Thin</c> ou <c>Double</c>.</summary>
    public string Style { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;
}
