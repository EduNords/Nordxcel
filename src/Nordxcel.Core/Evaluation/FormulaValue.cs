using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation;

/// <summary>
/// Resultado intermediário de uma subexpressão: ou um valor escalar, ou um
/// intervalo ainda não expandido.
/// <para>
/// O intervalo precisa sobreviver como intervalo até chegar na função que o
/// consome — <c>SOMA(B2:B10)</c> quer os dez valores, enquanto <c>B2:B10*2</c>
/// não faz sentido e vira <c>#VALOR!</c>.
/// </para>
/// </summary>
public readonly struct FormulaValue : IEquatable<FormulaValue>
{
    private FormulaValue(CellValue scalar, string? sheetName, CellRange range, bool isRange)
    {
        Scalar = scalar;
        SheetName = sheetName;
        Range = range;
        IsRange = isRange;
    }

    public static FormulaValue FromScalar(CellValue value) => new(value, null, default, false);

    public static FormulaValue FromRange(string sheetName, CellRange range)
    {
        ArgumentException.ThrowIfNullOrEmpty(sheetName);
        return new FormulaValue(CellValue.Blank, sheetName, range, true);
    }

    public static FormulaValue FromError(CellErrorType error) => FromScalar(CellValue.Error(error));

    public bool IsRange { get; }

    /// <summary>Valor escalar. Só faz sentido quando <see cref="IsRange"/> é falso.</summary>
    public CellValue Scalar { get; }

    /// <summary>Aba do intervalo, já resolvida. Só faz sentido quando <see cref="IsRange"/> é verdadeiro.</summary>
    public string? SheetName { get; }

    /// <summary>Área do intervalo. Só faz sentido quando <see cref="IsRange"/> é verdadeiro.</summary>
    public CellRange Range { get; }

    public bool IsError => !IsRange && Scalar.IsError;

    /// <summary>
    /// Reduz a um escalar. Um intervalo usado onde se espera um único valor vira
    /// <c>#VALOR!</c> — o Nordxcel não implementa a interseção implícita do Excel.
    /// </summary>
    public CellValue ToScalar() =>
        IsRange ? CellValue.Error(CellErrorType.Value) : Scalar;

    public bool Equals(FormulaValue other) =>
        IsRange == other.IsRange &&
        (IsRange
            ? Range.Equals(other.Range) && string.Equals(SheetName, other.SheetName, StringComparison.OrdinalIgnoreCase)
            : Scalar.Equals(other.Scalar));

    public override bool Equals(object? obj) => obj is FormulaValue other && Equals(other);

    public override int GetHashCode() =>
        IsRange ? HashCode.Combine(true, SheetName, Range) : HashCode.Combine(false, Scalar);

    public override string ToString() => IsRange ? $"{SheetName}!{Range}" : Scalar.ToString();
}
