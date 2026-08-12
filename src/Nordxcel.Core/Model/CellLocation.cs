namespace Nordxcel.Core.Model;

/// <summary>
/// Endereço de célula qualificado pela aba. É a chave do grafo de dependências:
/// <c>Premissas!B3</c> e <c>DCF!B3</c> são células diferentes.
/// O nome da aba não diferencia maiúsculas, como no Excel.
/// </summary>
public readonly record struct CellLocation
{
    public CellLocation(string sheetName, CellAddress address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        SheetName = sheetName;
        Address = address;
    }

    public string SheetName { get; }

    public CellAddress Address { get; }

    public bool Equals(CellLocation other) =>
        Address.Equals(other.Address) &&
        string.Equals(SheetName, other.SheetName, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(SheetName), Address);

    public override string ToString() => $"{SheetName}!{Address}";
}

/// <summary>Intervalo qualificado pela aba, para dependências do tipo <c>SOMA(B2:B10)</c>.</summary>
public readonly record struct RangeLocation
{
    public RangeLocation(string sheetName, CellRange range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        SheetName = sheetName;
        Range = range;
    }

    public string SheetName { get; }

    public CellRange Range { get; }

    public bool Contains(CellLocation location) =>
        string.Equals(SheetName, location.SheetName, StringComparison.OrdinalIgnoreCase) &&
        Range.Contains(location.Address);

    public bool Equals(RangeLocation other) =>
        Range.Equals(other.Range) &&
        string.Equals(SheetName, other.SheetName, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(SheetName), Range);

    public override string ToString() => $"{SheetName}!{Range}";
}
