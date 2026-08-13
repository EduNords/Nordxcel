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

/// <summary>Produto dos valores numéricos dos argumentos. Sem nenhum número, devolve zero.</summary>
public sealed class ProductFunction() : FormulaFunction("MULT", 1, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call)
    {
        var numbers = new List<double>();

        if (!FunctionArguments.TryCollectNumbers(call, numbers, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        double total = numbers.Count == 0 ? 0d : 1d;

        foreach (double number in numbers)
        {
            total *= number;
        }

        return NumericResult.FromDouble(total);
    }
}

/// <summary>Mediana: o valor do meio depois de ordenar, ou a média dos dois do meio quando a contagem é par.</summary>
public sealed class MedianFunction() : FormulaFunction("MED", 1, int.MaxValue)
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
            return CellValue.Error(CellErrorType.Number);
        }

        numbers.Sort();

        int mid = numbers.Count / 2;
        double median = numbers.Count % 2 == 1
            ? numbers[mid]
            : (numbers[mid - 1] + numbers[mid]) / 2d;

        return NumericResult.FromDouble(median);
    }
}

/// <summary>
/// Percentil por interpolação linear entre os dois valores ordenados mais
/// próximos — a mesma técnica do Excel. <c>k</c> vai de 0 a 1 (0,5 é a mediana).
/// </summary>
public sealed class PercentileFunction() : FormulaFunction("PERCENTIL", 2, 2)
{
    public override CellValue Invoke(FunctionCall call)
    {
        var numbers = new List<double>();

        if (!FunctionArguments.TryCollectNumbers(call, 0, 1, numbers, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        if (!FunctionArguments.TryGetNumber(call, 1, out double k, out error))
        {
            return CellValue.Error(error);
        }

        if (numbers.Count == 0 || k < 0d || k > 1d)
        {
            return CellValue.Error(CellErrorType.Number);
        }

        numbers.Sort();

        double position = k * (numbers.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);

        if (lower == upper)
        {
            return NumericResult.FromDouble(numbers[lower]);
        }

        double fraction = position - lower;
        double interpolated = numbers[lower] + fraction * (numbers[upper] - numbers[lower]);

        return NumericResult.FromDouble(interpolated);
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
