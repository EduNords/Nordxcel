using Nordxcel.Core.Model;

namespace Nordxcel.Core.Layout;

public enum NavigationDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// Para onde o cursor vai a cada tecla. Separado da interface para poder ser
/// testado sem abrir janela.
/// </summary>
public static class SelectionNavigator
{
    /// <summary>Um passo na direção, parando nas bordas da planilha.</summary>
    public static CellAddress Step(CellAddress from, NavigationDirection direction, int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        (int rowStep, int columnStep) = Delta(direction);

        return Offset(from, rowStep * count, columnStep * count);
    }

    /// <summary>
    /// Ctrl+seta: pula para a borda do bloco de dados.
    /// <para>
    /// A regra é a do Excel, e é o que torna o atalho útil num modelo: de dentro de
    /// um bloco preenchido vai para o fim dele; de uma célula solta, pula o vazio até
    /// o próximo conteúdo; sem nada pela frente, vai para a borda da planilha.
    /// </para>
    /// </summary>
    public static CellAddress JumpToEdge(Worksheet sheet, CellAddress from, NavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        int available = StepsWithinContent(sheet, from, direction);

        if (available <= 0)
        {
            return EdgeOf(from, direction);
        }

        bool currentFilled = !sheet.GetValue(from).IsBlank;
        bool nextFilled = !sheet.GetValue(Step(from, direction)).IsBlank;

        if (currentFilled && nextFilled)
        {
            CellAddress last = Step(from, direction);

            for (int distance = 2; distance <= available; distance++)
            {
                CellAddress candidate = Step(from, direction, distance);

                if (sheet.GetValue(candidate).IsBlank)
                {
                    break;
                }

                last = candidate;
            }

            return last;
        }

        for (int distance = 1; distance <= available; distance++)
        {
            CellAddress candidate = Step(from, direction, distance);

            if (!sheet.GetValue(candidate).IsBlank)
            {
                return candidate;
            }
        }

        return EdgeOf(from, direction);
    }

    /// <summary>Ctrl+Home: volta para A1.</summary>
    public static CellAddress HomeOfSheet() => CellAddress.Origin;

    /// <summary>Home: começo da linha atual.</summary>
    public static CellAddress HomeOfRow(CellAddress from) => new(from.Row, 0);

    /// <summary>Ctrl+End: última célula da área usada, ou A1 se a aba estiver vazia.</summary>
    public static CellAddress EndOfContent(Worksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        return sheet.GetUsedRange()?.End ?? CellAddress.Origin;
    }

    /// <summary>Célula da borda da planilha na direção informada.</summary>
    public static CellAddress EdgeOf(CellAddress from, NavigationDirection direction) => direction switch
    {
        NavigationDirection.Up => new CellAddress(0, from.Column),
        NavigationDirection.Down => new CellAddress(CellAddress.MaxRows - 1, from.Column),
        NavigationDirection.Left => new CellAddress(from.Row, 0),
        _ => new CellAddress(from.Row, CellAddress.MaxColumns - 1),
    };

    /// <summary>
    /// Quantos passos vale a pena percorrer: além da área usada não existe conteúdo,
    /// então varrer um milhão de linhas vazias seria desperdício.
    /// </summary>
    private static int StepsWithinContent(Worksheet sheet, CellAddress from, NavigationDirection direction)
    {
        CellRange? used = sheet.GetUsedRange();

        return direction switch
        {
            NavigationDirection.Up => from.Row - Math.Min(used?.Start.Row ?? 0, from.Row),
            NavigationDirection.Down => Math.Max(used?.End.Row ?? 0, from.Row) - from.Row,
            NavigationDirection.Left => from.Column - Math.Min(used?.Start.Column ?? 0, from.Column),
            _ => Math.Max(used?.End.Column ?? 0, from.Column) - from.Column,
        };
    }

    private static (int Row, int Column) Delta(NavigationDirection direction) => direction switch
    {
        NavigationDirection.Up => (-1, 0),
        NavigationDirection.Down => (1, 0),
        NavigationDirection.Left => (0, -1),
        _ => (0, 1),
    };

    private static CellAddress Offset(CellAddress from, int rows, int columns) => new(
        Math.Clamp(from.Row + rows, 0, CellAddress.MaxRows - 1),
        Math.Clamp(from.Column + columns, 0, CellAddress.MaxColumns - 1));
}
