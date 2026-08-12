using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions.Builtins;

/// <summary>
/// Arredondamento. O Excel arredonda meio para longe do zero — 2,5 vira 3 e
/// -2,5 vira -3 — e não pelo arredondamento bancário que o .NET usa por padrão.
/// Casas negativas arredondam para dezena, centena e assim por diante.
/// </summary>
public sealed class RoundFunction() : FormulaFunction("ARRED", 2, 2)
{
    /// <summary>Além disso o <c>double</c> não tem precisão para distinguir nada.</summary>
    private const int MaxDigits = 15;

    public override CellValue Invoke(FunctionCall call)
    {
        if (!FunctionArguments.TryGetNumber(call, 0, out double number, out CellErrorType error) ||
            !FunctionArguments.TryGetNumber(call, 1, out double rawDigits, out error))
        {
            return CellValue.Error(error);
        }

        int digits = (int)Math.Truncate(Math.Clamp(rawDigits, -MaxDigits, MaxDigits));

        if (digits >= 0)
        {
            return NumericResult.FromDouble(Math.Round(number, digits, MidpointRounding.AwayFromZero));
        }

        double factor = Math.Pow(10d, -digits);

        return NumericResult.FromDouble(Math.Round(number / factor, MidpointRounding.AwayFromZero) * factor);
    }
}

/// <summary>Valor absoluto.</summary>
public sealed class AbsFunction() : FormulaFunction("ABS", 1, 1)
{
    public override CellValue Invoke(FunctionCall call) =>
        FunctionArguments.TryGetNumber(call, 0, out double number, out CellErrorType error)
            ? NumericResult.FromDouble(Math.Abs(number))
            : CellValue.Error(error);
}

/// <summary>Potência. Igual ao operador <c>^</c>, inclusive nos casos de borda.</summary>
public sealed class PowerFunction() : FormulaFunction("POTÊNCIA", 2, 2)
{
    public override CellValue Invoke(FunctionCall call)
    {
        if (!FunctionArguments.TryGetNumber(call, 0, out double baseValue, out CellErrorType error) ||
            !FunctionArguments.TryGetNumber(call, 1, out double exponent, out error))
        {
            return CellValue.Error(error);
        }

        return NumericResult.Power(baseValue, exponent);
    }
}

/// <summary>Raiz quadrada. Número negativo devolve <c>#NÚM!</c>.</summary>
public sealed class SqrtFunction() : FormulaFunction("RAIZ", 1, 1)
{
    public override CellValue Invoke(FunctionCall call)
    {
        if (!FunctionArguments.TryGetNumber(call, 0, out double number, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        return number < 0d
            ? CellValue.Error(CellErrorType.Number)
            : NumericResult.FromDouble(Math.Sqrt(number));
    }
}
