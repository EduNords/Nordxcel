using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions.Builtins;

/// <summary>
/// Valor presente líquido de uma série de fluxos.
/// <para>
/// Atenção à convenção do Excel, que é a maior pegadinha de DCF: o
/// <b>primeiro fluxo já é descontado por um período</b>. O investimento inicial,
/// que acontece em t=0, precisa ficar de fora e ser somado depois —
/// <c>VPL(WACC;C12:H12)+B12</c>, e não <c>VPL(WACC;B12:H12)</c>.
/// </para>
/// </summary>
public sealed class NpvFunction() : FormulaFunction("VPL", 2, int.MaxValue)
{
    public override CellValue Invoke(FunctionCall call)
    {
        if (!FunctionArguments.TryGetNumber(call, 0, out double rate, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        double discountBase = 1d + rate;

        if (discountBase == 0d)
        {
            return CellValue.Error(CellErrorType.DivideByZero);
        }

        var flows = new List<double>();

        if (!FunctionArguments.TryCollectNumbers(call, 1, call.ArgumentCount, flows, out error))
        {
            return CellValue.Error(error);
        }

        double total = 0d;

        for (int period = 0; period < flows.Count; period++)
        {
            total += flows[period] / Math.Pow(discountBase, period + 1);
        }

        return NumericResult.FromDouble(total);
    }
}

/// <summary>
/// Taxa interna de retorno: a taxa que zera o valor presente da série.
/// <para>
/// Diferente do <c>VPL</c>, aqui o <b>primeiro fluxo fica em t=0</b>, sem
/// desconto — é a inconsistência do próprio Excel entre as duas funções, e ela é
/// reproduzida de propósito.
/// </para>
/// </summary>
public sealed class IrrFunction() : FormulaFunction("TIR", 1, 2)
{
    private const double DefaultGuess = 0.1d;

    /// <summary>Abaixo de -100% o fator de desconto muda de sinal e a série perde sentido.</summary>
    private const double LowerBound = -0.9999999d;

    /// <summary>100.000% ao período. Acima disso não existe negócio, existe erro de digitação.</summary>
    private const double UpperBound = 1000d;

    public override CellValue Invoke(FunctionCall call)
    {
        var flows = new List<double>();

        if (!FunctionArguments.TryCollectNumbers(call, 0, 1, flows, out CellErrorType error))
        {
            return CellValue.Error(error);
        }

        double guess = DefaultGuess;

        if (call.ArgumentCount > 1 && !call.IsMissing(1))
        {
            if (!FunctionArguments.TryGetNumber(call, 1, out guess, out error))
            {
                return CellValue.Error(error);
            }
        }

        if (flows.Count < 2 || !HasSignChange(flows))
        {
            // Sem ao menos uma saída e uma entrada de caixa não existe retorno a calcular.
            return CellValue.Error(CellErrorType.Number);
        }

        bool solved = RootFinder.TrySolve(
            rate => PresentValue(flows, rate),
            guess,
            LowerBound,
            UpperBound,
            out double result);

        return solved ? NumericResult.FromDouble(result) : CellValue.Error(CellErrorType.Number);
    }

    /// <summary>Valor presente da série com o primeiro fluxo em t=0.</summary>
    private static double PresentValue(List<double> flows, double rate)
    {
        double discountBase = 1d + rate;
        double total = 0d;

        for (int period = 0; period < flows.Count; period++)
        {
            total += flows[period] / Math.Pow(discountBase, period);
        }

        return total;
    }

    private static bool HasSignChange(List<double> flows)
    {
        bool hasPositive = false;
        bool hasNegative = false;

        foreach (double flow in flows)
        {
            hasPositive |= flow > 0d;
            hasNegative |= flow < 0d;
        }

        return hasPositive && hasNegative;
    }
}

/// <summary>
/// Valor futuro de um investimento com aporte periódico constante.
/// <c>VF(taxa; nper; pgto; [vp]; [tipo])</c>, com <c>tipo</c> 0 para pagamento no
/// fim do período e 1 para o começo.
/// <para>
/// Segue a convenção de sinal do Excel: dinheiro que sai é negativo, então
/// <c>VF(5%;10;0;-1000)</c> devolve 1.628,89 positivo.
/// </para>
/// </summary>
public sealed class FvFunction() : FormulaFunction("VF", 3, 5)
{
    public override CellValue Invoke(FunctionCall call)
    {
        if (!FunctionArguments.TryGetNumber(call, 0, out double rate, out CellErrorType error) ||
            !FunctionArguments.TryGetNumber(call, 1, out double periods, out error) ||
            !FunctionArguments.TryGetNumber(call, 2, out double payment, out error))
        {
            return CellValue.Error(error);
        }

        double presentValue = 0d;

        if (call.ArgumentCount > 3 && !call.IsMissing(3) &&
            !FunctionArguments.TryGetNumber(call, 3, out presentValue, out error))
        {
            return CellValue.Error(error);
        }

        if (!AnnuityMath.TryReadPaymentTiming(call, 4, out int timing, out error))
        {
            return CellValue.Error(error);
        }

        return NumericResult.FromDouble(
            AnnuityMath.FutureValue(rate, periods, payment, presentValue, timing));
    }
}

/// <summary>
/// Taxa por período de uma série de pagamentos constantes.
/// <c>TAXA(nper; pgto; vp; [vf]; [tipo]; [estimativa])</c>.
/// Não tem fórmula fechada: é resolvida numericamente.
/// </summary>
public sealed class RateFunction() : FormulaFunction("TAXA", 3, 6)
{
    private const double DefaultGuess = 0.1d;
    private const double LowerBound = -0.9999999d;
    private const double UpperBound = 1000d;

    public override CellValue Invoke(FunctionCall call)
    {
        if (!FunctionArguments.TryGetNumber(call, 0, out double periods, out CellErrorType error) ||
            !FunctionArguments.TryGetNumber(call, 1, out double payment, out error) ||
            !FunctionArguments.TryGetNumber(call, 2, out double presentValue, out error))
        {
            return CellValue.Error(error);
        }

        if (periods <= 0d)
        {
            return CellValue.Error(CellErrorType.Number);
        }

        double futureValue = 0d;

        if (call.ArgumentCount > 3 && !call.IsMissing(3) &&
            !FunctionArguments.TryGetNumber(call, 3, out futureValue, out error))
        {
            return CellValue.Error(error);
        }

        if (!AnnuityMath.TryReadPaymentTiming(call, 4, out int timing, out error))
        {
            return CellValue.Error(error);
        }

        double guess = DefaultGuess;

        if (call.ArgumentCount > 5 && !call.IsMissing(5) &&
            !FunctionArguments.TryGetNumber(call, 5, out guess, out error))
        {
            return CellValue.Error(error);
        }

        bool solved = RootFinder.TrySolve(
            rate => AnnuityMath.FutureValue(rate, periods, payment, presentValue, timing) - futureValue,
            guess,
            LowerBound,
            UpperBound,
            out double result);

        return solved ? NumericResult.FromDouble(result) : CellValue.Error(CellErrorType.Number);
    }
}

/// <summary>Matemática de anuidade compartilhada entre <c>VF</c> e <c>TAXA</c>.</summary>
internal static class AnnuityMath
{
    /// <summary>
    /// Valor futuro na convenção do Excel, em que o resultado tem sinal oposto ao
    /// do dinheiro investido.
    /// </summary>
    public static double FutureValue(double rate, double periods, double payment, double presentValue, int timing)
    {
        if (rate == 0d)
        {
            return -(presentValue + payment * periods);
        }

        double growth = Math.Pow(1d + rate, periods);

        return -(presentValue * growth + payment * (1d + rate * timing) * (growth - 1d) / rate);
    }

    /// <summary>Lê o argumento de momento do pagamento: 0 no fim do período, 1 no começo.</summary>
    public static bool TryReadPaymentTiming(FunctionCall call, int index, out int timing, out CellErrorType error)
    {
        timing = 0;
        error = CellErrorType.None;

        if (call.ArgumentCount <= index || call.IsMissing(index))
        {
            return true;
        }

        if (!FunctionArguments.TryGetNumber(call, index, out double raw, out error))
        {
            return false;
        }

        double truncated = Math.Truncate(raw);

        if (truncated is not (0d or 1d))
        {
            error = CellErrorType.Number;
            return false;
        }

        timing = (int)truncated;
        return true;
    }
}
