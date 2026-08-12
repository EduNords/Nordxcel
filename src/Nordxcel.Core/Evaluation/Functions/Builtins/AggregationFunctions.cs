using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions.Builtins;

/// <summary>Soma dos valores numéricos dos argumentos.</summary>
public sealed class SumFunction() : FormulaFunction("SOMA", 1, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call)
    {
        var numbers = new List<double>();

        if (!FunctionArguments.TryCollectNumbers(call, numbers, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        double total = 0d;

        foreach (double number in numbers)
        {
            total += number;
        }

        return NumericResult.FromDouble(total);
    }
}

/// <summary>Média aritmética. Sem nenhum número, devolve <c>#DIV/0!</c> como o Excel.</summary>
public sealed class AverageFunction() : FormulaFunction("MÉDIA", 1, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call)
    {
        var numbers = new List<double>();

        if (!FunctionArguments.TryCollectNumbers(call, numbers, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        if (numbers.Count == 0)
        {
            return CellValue.Error(CellErrorType.DivideByZero);
        }

        double total = 0d;

        foreach (double number in numbers)
        {
            total += number;
        }

        return NumericResult.FromDouble(total / numbers.Count);
    }
}

/// <summary>Menor valor. Sem nenhum número, devolve zero como o Excel.</summary>
public sealed class MinFunction() : FormulaFunction("MÍNIMO", 1, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call)
    {
        var numbers = new List<double>();

        if (!FunctionArguments.TryCollectNumbers(call, numbers, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        if (numbers.Count == 0)
        {
            return CellValue.Number(0d);
        }

        double result = double.MaxValue;

        foreach (double number in numbers)
        {
            result = Math.Min(result, number);
        }

        return CellValue.Number(result);
    }
}

/// <summary>Maior valor. Sem nenhum número, devolve zero como o Excel.</summary>
public sealed class MaxFunction() : FormulaFunction("MÁXIMO", 1, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call)
    {
        var numbers = new List<double>();

        if (!FunctionArguments.TryCollectNumbers(call, numbers, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        if (numbers.Count == 0)
        {
            return CellValue.Number(0d);
        }

        double result = double.MinValue;

        foreach (double number in numbers)
        {
            result = Math.Max(result, number);
        }

        return CellValue.Number(result);
    }
}

/// <summary>
/// Conta os valores não vazios. Diferente das outras agregações, conta também
/// texto, lógico e erro — só célula em branco fica de fora.
/// </summary>
public sealed class CountAFunction() : FormulaFunction("CONT.VALORES", 1, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call)
    {
        int count = 0;

        for (int index = 0; index < call.ArgumentCount; index++)
        {
            if (call.IsMissing(index))
            {
                continue;
            }

            foreach (CellValue value in call.EnumerateValues(index))
            {
                if (!value.IsBlank)
                {
                    count++;
                }
            }
        }

        return CellValue.Number(count);
    }
}
