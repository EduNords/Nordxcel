using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Editing;

/// <summary>
/// Atalho de borda que a barra de formatação oferece, igual ao Excel: aplicar em
/// todas as células do intervalo, só no contorno externo, ou só num lado.
/// </summary>
public enum BorderPreset
{
    /// <summary>Remove toda borda do intervalo.</summary>
    None,

    /// <summary>Grade completa: toda aresta, interna e externa.</summary>
    All,

    /// <summary>Só o contorno externo do intervalo.</summary>
    Outline,

    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>
/// Aplica um <see cref="BorderPreset"/> a um intervalo.
/// <para>
/// Ao contrário de <see cref="RangeEditing.Apply"/>, aqui a transformação de cada
/// célula depende de <b>onde</b> ela está dentro do intervalo — a célula do canto
/// superior esquerdo de um contorno externo recebe borda em cima e à esquerda, uma
/// célula do meio não recebe nenhuma. Por isso não cabe no <c>Func&lt;Cell,Cell&gt;</c>
/// genérico do <see cref="RangeEditing"/>.
/// </para>
/// </summary>
public static class BorderEditing
{
    public static IReadOnlyList<CellEdit> Apply(
        Worksheet sheet,
        CellRange range,
        BorderPreset preset,
        BorderLineStyle style,
        RgbColor color)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var edits = new List<CellEdit>();

        // Contorno numa coluna ou linha inteira não faz sentido — seria uma borda
        // ao redor de mais de um milhão de linhas — então a operação é ignorada.
        if (range.RowCount >= CellAddress.MaxRows || range.ColumnCount >= CellAddress.MaxColumns)
        {
            return edits;
        }

        var edge = new BorderEdge(style, color);

        foreach (CellAddress address in range.Addresses())
        {
            Cell before = sheet.GetCell(address);
            CellBorders current = before.Style.Borders;
            CellBorders updated = ResolveBorders(current, edge, preset, range, address);

            if (updated.Equals(current))
            {
                continue;
            }

            Cell after = before with { Style = before.Style with { Borders = updated } };

            sheet.SetCell(address, after);
            edits.Add(new CellEdit(new CellLocation(sheet.Name, address), before, after));
        }

        return edits;
    }

    private static CellBorders ResolveBorders(
        CellBorders current,
        BorderEdge edge,
        BorderPreset preset,
        CellRange range,
        CellAddress address)
    {
        bool atTop = address.Row == range.Start.Row;
        bool atBottom = address.Row == range.End.Row;
        bool atLeft = address.Column == range.Start.Column;
        bool atRight = address.Column == range.End.Column;

        return preset switch
        {
            BorderPreset.None => CellBorders.None,
            BorderPreset.All => new CellBorders(edge, edge, edge, edge),

            // Contorno externo: cada lado só recebe o traço se a célula estiver
            // naquela borda do intervalo; o resto da célula fica como estava.
            BorderPreset.Outline => new CellBorders(
                Top: atTop ? edge : current.Top,
                Right: atRight ? edge : current.Right,
                Bottom: atBottom ? edge : current.Bottom,
                Left: atLeft ? edge : current.Left),

            BorderPreset.Top => atTop ? current with { Top = edge } : current,
            BorderPreset.Bottom => atBottom ? current with { Bottom = edge } : current,
            BorderPreset.Left => atLeft ? current with { Left = edge } : current,
            BorderPreset.Right => atRight ? current with { Right = edge } : current,

            _ => current,
        };
    }
}
