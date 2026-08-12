using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions.Builtins;

/// <summary>
/// Condicional. Só avalia o ramo escolhido — é por isso que
/// <c>SE(A1=0;0;1/A1)</c> não devolve <c>#DIV/0!</c> quando A1 é zero.
/// </summary>
public sealed class IfFunction() : FormulaFunction("SE", 2, 3)
{
    public override CellValue Invoke(FunctionCall call)
    {
        if (!FunctionArguments.TryGetLogical(call, 0, out bool condition, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        if (condition)
        {
            // SE(VERDADEIRO;;5) devolve zero, como no Excel.
            return call.IsMissing(1) ? CellValue.Number(0d) : call.GetScalar(1);
        }

        if (call.ArgumentCount < 3)
        {
            return CellValue.False;
        }

        return call.IsMissing(2) ? CellValue.Number(0d) : call.GetScalar(2);
    }
}

/// <summary>
/// Substitui o resultado por outro valor quando ele é um erro. Segundo argumento
/// omitido devolve zero.
/// </summary>
public sealed class IfErrorFunction() : FormulaFunction("SEERRO", 2, 2)
{
    public override CellValue Invoke(FunctionCall call)
    {
        CellValue value = call.GetScalar(0);

        if (!value.IsError)
        {
            return value;
        }

        return call.IsMissing(1) ? CellValue.Number(0d) : call.GetScalar(1);
    }
}

/// <summary>Verdadeiro quando todos os valores lógicos são verdadeiros.</summary>
public sealed class AndFunction() : FormulaFunction("E", 1, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call) => LogicalAggregation.Evaluate(call, requireAll: true);
}

/// <summary>Verdadeiro quando ao menos um valor lógico é verdadeiro.</summary>
public sealed class OrFunction() : FormulaFunction("OU", 1, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call) => LogicalAggregation.Evaluate(call, requireAll: false);
}

internal static class LogicalAggregation
{
    /// <summary>
    /// <c>E</c> e <c>OU</c> avaliam todos os argumentos: diferente do <c>&amp;&amp;</c>
    /// de uma linguagem de programação, um erro no meio da lista contamina o
    /// resultado mesmo quando o valor já estaria decidido.
    /// </summary>
    public static CellValue Evaluate(FunctionCall call, bool requireAll)
    {
        var logicals = new List<bool>();

        if (!FunctionArguments.TryCollectLogicals(call, logicals, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        if (logicals.Count == 0)
        {
            return CellValue.Error(CellErrorType.Value);
        }

        foreach (bool logical in logicals)
        {
            if (logical != requireAll)
            {
                return CellValue.Logical(!requireAll);
            }
        }

        return CellValue.Logical(requireAll);
    }
}
