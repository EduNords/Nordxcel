namespace Nordxcel.Core.Model.Styling;

/// <summary>
/// Traço de borda. <see cref="Double"/> é obrigatório em modelos financeiros:
/// é a convenção de linha de total geral (Enterprise Value, Equity Value).
/// </summary>
public enum BorderLineStyle
{
    None = 0,
    Thin,
    Medium,
    Thick,
    Double,
}

/// <summary>Borda de um dos lados da célula.</summary>
public readonly record struct BorderEdge(BorderLineStyle Style, RgbColor Color)
{
    /// <summary>Ausência de borda — também é o <c>default</c> do struct.</summary>
    public static BorderEdge None => default;

    public static BorderEdge Thin(RgbColor color) => new(BorderLineStyle.Thin, color);

    public bool IsVisible => Style is not BorderLineStyle.None;
}

/// <summary>Conjunto das quatro bordas de uma célula.</summary>
public readonly record struct CellBorders(BorderEdge Top, BorderEdge Right, BorderEdge Bottom, BorderEdge Left)
{
    /// <summary>Célula sem nenhuma borda — também é o <c>default</c> do struct.</summary>
    public static CellBorders None => default;

    /// <summary>Mesmo traço nos quatro lados, atalho de "todas as bordas" da toolbar.</summary>
    public static CellBorders All(BorderLineStyle style, RgbColor color)
    {
        var edge = new BorderEdge(style, color);
        return new CellBorders(edge, edge, edge, edge);
    }

    public bool HasAny => Top.IsVisible || Right.IsVisible || Bottom.IsVisible || Left.IsVisible;
}
