namespace Nordxcel.Core.Model;

/// <summary>
/// Referência a uma célula como ela aparece dentro de uma fórmula: com marcação
/// de absoluto por eixo (<c>$A$1</c>, <c>A$1</c>, <c>$A1</c>) e, opcionalmente,
/// nome de aba (<c>Premissas!B5</c>).
/// </summary>
public readonly record struct CellReference
{
    public CellReference(CellAddress address, bool absoluteColumn = false, bool absoluteRow = false, string? sheet = null)
    {
        Address = address;
        AbsoluteColumn = absoluteColumn;
        AbsoluteRow = absoluteRow;
        Sheet = string.IsNullOrEmpty(sheet) ? null : sheet;
    }

    public CellAddress Address { get; }

    /// <summary>Coluna travada com <c>$</c>: não se desloca ao copiar a fórmula.</summary>
    public bool AbsoluteColumn { get; }

    /// <summary>Linha travada com <c>$</c>: não se desloca ao copiar a fórmula.</summary>
    public bool AbsoluteRow { get; }

    /// <summary>Aba referenciada, ou <c>null</c> quando a referência é da própria aba.</summary>
    public string? Sheet { get; }

    /// <summary>Verdadeiro quando a referência aponta para outra aba — pinta de verde no sistema de cores.</summary>
    public bool IsExternal => Sheet is not null;

    /// <summary>
    /// Desloca a referência ao copiar a fórmula para outra célula. Os eixos marcados
    /// como absolutos ficam parados. Devolve <c>false</c> se o destino sair da planilha,
    /// caso em que a fórmula colada vira <c>#REF!</c>.
    /// </summary>
    public bool TryTranslate(int rowDelta, int columnDelta, out CellReference result)
    {
        int rows = AbsoluteRow ? 0 : rowDelta;
        int columns = AbsoluteColumn ? 0 : columnDelta;

        if (!Address.TryOffset(rows, columns, out CellAddress moved))
        {
            result = default;
            return false;
        }

        result = new CellReference(moved, AbsoluteColumn, AbsoluteRow, Sheet);
        return true;
    }

    /// <summary>Interpreta <c>Premissas!$B$5</c>, <c>'Fluxo de Caixa'!A1</c>, <c>A$1</c>.</summary>
    public static CellReference Parse(string text) =>
        TryParse(text, out CellReference reference)
            ? reference
            : throw new FormatException($"'{text}' não é uma referência de célula válida.");

    /// <inheritdoc cref="Parse"/>
    public static bool TryParse(string? text, out CellReference reference)
    {
        reference = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> remaining = text.AsSpan().Trim();
        string? sheet = null;

        int separator = remaining.LastIndexOf('!');

        if (separator >= 0)
        {
            if (!TryReadSheetName(remaining[..separator], out sheet))
            {
                return false;
            }

            remaining = remaining[(separator + 1)..];
        }

        bool absoluteColumn = false;

        if (remaining.Length > 0 && remaining[0] == '$')
        {
            absoluteColumn = true;
            remaining = remaining[1..];
        }

        int letters = 0;
        while (letters < remaining.Length && char.IsAsciiLetter(remaining[letters]))
        {
            letters++;
        }

        if (letters == 0 || !CellAddress.TryParseColumnName(remaining[..letters], out int column))
        {
            return false;
        }

        remaining = remaining[letters..];
        bool absoluteRow = false;

        if (remaining.Length > 0 && remaining[0] == '$')
        {
            absoluteRow = true;
            remaining = remaining[1..];
        }

        if (remaining.IsEmpty)
        {
            return false;
        }

        foreach (char c in remaining)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        if (!int.TryParse(remaining, out int oneBasedRow) || oneBasedRow < 1 || oneBasedRow > CellAddress.MaxRows)
        {
            return false;
        }

        reference = new CellReference(
            new CellAddress(oneBasedRow - 1, column),
            absoluteColumn,
            absoluteRow,
            sheet);

        return true;
    }

    private static bool TryReadSheetName(ReadOnlySpan<char> raw, out string? sheet)
    {
        sheet = null;

        if (raw.IsEmpty)
        {
            return false;
        }

        if (raw[0] == '\'')
        {
            if (raw.Length < 2 || raw[^1] != '\'')
            {
                return false;
            }

            // Dentro de aspas simples, o apóstrofo do nome vem duplicado.
            sheet = raw[1..^1].ToString().Replace("''", "'", StringComparison.Ordinal);
            return sheet.Length > 0;
        }

        sheet = raw.ToString();
        return !sheet.Contains('\'', StringComparison.Ordinal);
    }

    public override string ToString()
    {
        string cell = $"{(AbsoluteColumn ? "$" : string.Empty)}{Address.ColumnName}" +
                      $"{(AbsoluteRow ? "$" : string.Empty)}{Address.Row + 1}";

        if (Sheet is null)
        {
            return cell;
        }

        bool needsQuotes = Sheet.Any(c => !char.IsLetterOrDigit(c) && c != '_');
        string sheet = needsQuotes
            ? $"'{Sheet.Replace("'", "''", StringComparison.Ordinal)}'"
            : Sheet;

        return $"{sheet}!{cell}";
    }
}
