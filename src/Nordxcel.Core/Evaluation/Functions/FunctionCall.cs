using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions;

/// <summary>
/// Uma chamada de função em avaliação. Os argumentos são avaliados sob demanda e
/// o resultado fica em cache, de modo que ler o mesmo argumento duas vezes não
/// recalcula a subárvore.
/// </summary>
public sealed class FunctionCall
{
    private readonly IReadOnlyList<FormulaNode> _arguments;
    private readonly FormulaValue?[] _evaluated;

    internal FunctionCall(
        string name,
        IReadOnlyList<FormulaNode> arguments,
        FormulaEvaluator evaluator,
        EvaluationScope scope)
    {
        Name = name;
        _arguments = arguments;
        _evaluated = new FormulaValue?[arguments.Count];
        Evaluator = evaluator;
        Scope = scope;
    }

    public string Name { get; }

    public FormulaEvaluator Evaluator { get; }

    public EvaluationScope Scope { get; }

    public int ArgumentCount => _arguments.Count;

    /// <summary>Verdadeiro quando o argumento foi omitido, como o do meio em <c>SE(A1;;0)</c>.</summary>
    public bool IsMissing(int index) =>
        index >= 0 && index < _arguments.Count && _arguments[index] is MissingArgumentNode;

    /// <summary>Árvore do argumento, para quem precisar inspecioná-la sem avaliar.</summary>
    public FormulaNode GetNode(int index) => _arguments[index];

    /// <summary>Avalia o argumento, preservando intervalo como intervalo.</summary>
    public FormulaValue GetValue(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _arguments.Count);

        return _evaluated[index] ??= Evaluator.EvaluateOperand(_arguments[index], Scope);
    }

    /// <summary>Avalia o argumento reduzido a um único valor.</summary>
    public CellValue GetScalar(int index) => GetValue(index).ToScalar();

    /// <summary>
    /// Todos os valores do argumento: um intervalo é expandido em varredura por
    /// linha, e um escalar rende um único valor. É o que as agregações consomem.
    /// </summary>
    public IEnumerable<CellValue> EnumerateValues(int index)
    {
        FormulaValue value = GetValue(index);

        if (!value.IsRange)
        {
            return [value.Scalar];
        }

        return Evaluator.Context.GetValues(value.SheetName!, value.Range);
    }

    /// <summary>Concatena os valores de todos os argumentos, na ordem em que aparecem.</summary>
    public IEnumerable<CellValue> EnumerateAllValues()
    {
        for (int index = 0; index < ArgumentCount; index++)
        {
            foreach (CellValue value in EnumerateValues(index))
            {
                yield return value;
            }
        }
    }
}
