namespace Nordxcel.Core.Model;

/// <summary>
/// Intervalo retangular de células, normalizado para que <see cref="Start"/> seja
/// sempre o canto superior esquerdo. É o que as funções de agregação recebem
/// quando a fórmula usa <c>B2:B10</c>.
/// </summary>
public readonly record struct CellRange
{
    public CellRange(CellAddress a, CellAddress b)
    {
        Start = new CellAddress(Math.Min(a.Row, b.Row), Math.Min(a.Column, b.Column));
        End = new CellAddress(Math.Max(a.Row, b.Row), Math.Max(a.Column, b.Column));
    }

    /// <summary>Canto superior esquerdo.</summary>
    public CellAddress Start { get; }

    /// <summary>Canto inferior direito, inclusivo.</summary>
    public CellAddress End { get; }

    public int RowCount => End.Row - Start.Row + 1;

    public int ColumnCount => End.Column - Start.Column + 1;

    /// <summary><c>long</c> porque um intervalo de planilha inteira estoura <c>int</c>.</summary>
    public long CellCount => (long)RowCount * ColumnCount;

    public bool IsSingleCell => RowCount == 1 && ColumnCount == 1;

    public static CellRange Single(CellAddress address) => new(address, address);

    public bool Contains(CellAddress address) =>
        address.Row >= Start.Row && address.Row <= End.Row &&
        address.Column >= Start.Column && address.Column <= End.Column;

    public bool Contains(CellRange other) => Contains(other.Start) && Contains(other.End);

    public bool Intersects(CellRange other) =>
        Start.Row <= other.End.Row && End.Row >= other.Start.Row &&
        Start.Column <= other.End.Column && End.Column >= other.Start.Column;

    /// <summary>Percorre o intervalo em varredura por linha, da esquerda para a direita.</summary>
    public IEnumerable<CellAddress> Addresses()
    {
        for (int row = Start.Row; row <= End.Row; row++)
        {
            for (int column = Start.Column; column <= End.Column; column++)
            {
                yield return new CellAddress(row, column);
            }
        }
    }

    /// <summary>Interpreta <c>A1:B10</c> ou <c>A1</c>. Não aceita <c>$</c> nem nome de aba.</summary>
    public static CellRange Parse(string text) =>
        TryParse(text, out CellRange range)
            ? range
            : throw new FormatException($"'{text}' não é um intervalo válido.");

    /// <inheritdoc cref="Parse"/>
    public static bool TryParse(ReadOnlySpan<char> text, out CellRange range)
    {
        range = default;

        int separator = text.IndexOf(':');

        if (separator < 0)
        {
            if (!CellAddress.TryParse(text, out CellAddress single))
            {
                return false;
            }

            range = Single(single);
            return true;
        }

        if (!CellAddress.TryParse(text[..separator], out CellAddress start) ||
            !CellAddress.TryParse(text[(separator + 1)..], out CellAddress end))
        {
            return false;
        }

        range = new CellRange(start, end);
        return true;
    }

    public override string ToString() => IsSingleCell ? Start.ToString() : $"{Start}:{End}";
}
