namespace Nordxcel.Core.Model.Styling;

public enum HorizontalAlignment
{
    /// <summary>Padrão do Excel: número à direita, texto à esquerda, lógico centralizado.</summary>
    General = 0,

    Left,
    Center,
    Right,
}

public enum VerticalAlignment
{
    Top = 0,
    Center,
    Bottom,
}

/// <summary>
/// Aparência de uma célula, sem incluir o formato de número (que fica em <see cref="Nordxcel.Core.Model.Cell.NumberFormat"/>).
/// É imutável para poder ser compartilhada entre milhares de células e para
/// que desfazer/refazer possa guardar apenas a referência anterior.
/// </summary>
public sealed record CellStyle
{
    /// <summary>Estilo padrão de célula nova.</summary>
    public static readonly CellStyle Default = new();

    /// <summary>Calibri 11 é o padrão do Excel e o que os modelos de IB/PE assumem.</summary>
    public string FontFamily { get; init; } = "Calibri";

    public double FontSize { get; init; } = 11d;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    /// <summary>
    /// Cor da fonte definida manualmente. <c>null</c> significa automática, isto é,
    /// decidida pelo sistema de cores azul/preto/verde a partir do conteúdo da célula.
    /// </summary>
    public RgbColor? FontColor { get; init; }

    /// <summary>Cor de preenchimento. <c>null</c> significa sem preenchimento.</summary>
    public RgbColor? BackgroundColor { get; init; }

    public HorizontalAlignment HorizontalAlignment { get; init; } = HorizontalAlignment.General;

    public VerticalAlignment VerticalAlignment { get; init; } = VerticalAlignment.Bottom;

    public CellBorders Borders { get; init; } = CellBorders.None;

    /// <summary>Recuo em níveis, usado para escalonar as linhas do template DCF.</summary>
    public int IndentLevel { get; init; }

    /// <summary>Verdadeiro quando o estilo não difere em nada do padrão.</summary>
    public bool IsDefault => Equals(Default);
}
