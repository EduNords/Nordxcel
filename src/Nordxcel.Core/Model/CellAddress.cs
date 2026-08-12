namespace Nordxcel.Core.Model;

/// <summary>
/// Endereço de uma célula dentro de uma planilha, em coordenadas base zero.
/// A notação A1 exposta em <see cref="ToString"/> é base um, como no Excel:
/// <c>new CellAddress(0, 0)</c> equivale a <c>A1</c>.
/// </summary>
public readonly record struct CellAddress : IComparable<CellAddress>
{
    /// <summary>Número máximo de linhas, igual ao limite do Excel.</summary>
    public const int MaxRows = 1_048_576;

    /// <summary>Número máximo de colunas, igual ao limite do Excel (A até XFD).</summary>
    public const int MaxColumns = 16_384;

    /// <summary>Endereço da primeira célula da planilha (<c>A1</c>).</summary>
    public static readonly CellAddress Origin = new(0, 0);

    public CellAddress(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, MaxRows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, MaxColumns);

        Row = row;
        Column = column;
    }

    /// <summary>Índice da linha, base zero.</summary>
    public int Row { get; }

    /// <summary>Índice da coluna, base zero.</summary>
    public int Column { get; }

    /// <summary>Nome da coluna em letras (<c>A</c>, <c>Z</c>, <c>AA</c>, <c>XFD</c>).</summary>
    public string ColumnName => ColumnToName(Column);

    /// <summary>
    /// Desloca o endereço. Lança se o resultado sair dos limites da planilha —
    /// use <see cref="TryOffset"/> quando o estouro for esperado (ex.: colar fórmula).
    /// </summary>
    public CellAddress Offset(int rows, int columns) => new(Row + rows, Column + columns);

    /// <summary>Desloca o endereço, devolvendo <c>false</c> se o resultado sair da planilha.</summary>
    public bool TryOffset(int rows, int columns, out CellAddress result)
    {
        long row = (long)Row + rows;
        long column = (long)Column + columns;

        if (row < 0 || row >= MaxRows || column < 0 || column >= MaxColumns)
        {
            result = default;
            return false;
        }

        result = new CellAddress((int)row, (int)column);
        return true;
    }

    /// <summary>Converte um índice de coluna base zero para letras: <c>0 → A</c>, <c>26 → AA</c>.</summary>
    public static string ColumnToName(int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, MaxColumns);

        // Base 26 bijetiva: não existe "dígito zero", por isso o -1 a cada volta.
        Span<char> buffer = stackalloc char[3];
        int index = buffer.Length;
        int remaining = column;

        do
        {
            buffer[--index] = (char)('A' + remaining % 26);
            remaining = remaining / 26 - 1;
        }
        while (remaining >= 0);

        return new string(buffer[index..]);
    }

    /// <summary>Converte letras de coluna para índice base zero: <c>A → 0</c>, <c>AA → 26</c>.</summary>
    public static bool TryParseColumnName(ReadOnlySpan<char> name, out int column)
    {
        column = 0;

        if (name.Length is 0 or > 3)
        {
            return false;
        }

        int accumulated = 0;

        foreach (char c in name)
        {
            if (!char.IsAsciiLetter(c))
            {
                return false;
            }

            accumulated = accumulated * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        if (accumulated > MaxColumns)
        {
            return false;
        }

        column = accumulated - 1;
        return true;
    }

    /// <summary>Interpreta um endereço em notação A1. Não aceita <c>$</c> — veja <see cref="CellReference"/>.</summary>
    public static CellAddress Parse(string text) =>
        TryParse(text, out CellAddress address)
            ? address
            : throw new FormatException($"'{text}' não é um endereço de célula válido.");

    /// <inheritdoc cref="Parse"/>
    public static bool TryParse(ReadOnlySpan<char> text, out CellAddress address)
    {
        address = default;

        int split = 0;
        while (split < text.Length && char.IsAsciiLetter(text[split]))
        {
            split++;
        }

        if (split == 0 || split == text.Length)
        {
            return false;
        }

        if (!TryParseColumnName(text[..split], out int column))
        {
            return false;
        }

        ReadOnlySpan<char> digits = text[split..];

        foreach (char c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        if (!int.TryParse(digits, out int oneBasedRow) || oneBasedRow < 1 || oneBasedRow > MaxRows)
        {
            return false;
        }

        address = new CellAddress(oneBasedRow - 1, column);
        return true;
    }

    /// <summary>Ordena em varredura por linha (esquerda para a direita, de cima para baixo).</summary>
    public int CompareTo(CellAddress other) =>
        Row != other.Row ? Row.CompareTo(other.Row) : Column.CompareTo(other.Column);

    public override string ToString() => $"{ColumnName}{Row + 1}";
}
