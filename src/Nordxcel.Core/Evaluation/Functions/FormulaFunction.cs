using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions;

/// <summary>Base das funções embutidas, só para não repetir nome e aridade.</summary>
public abstract class FormulaFunction : IFormulaFunction
{
    protected FormulaFunction(string name, int minArguments, int maxArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(minArguments);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxArguments, minArguments);

        Name = name.ToUpperInvariant();
        MinArguments = minArguments;
        MaxArguments = maxArguments;
    }

    public string Name { get; }

    public int MinArguments { get; }

    public int MaxArguments { get; }

    public abstract CellValue Invoke(FunctionCall call);

    public override string ToString() => Name;
}
