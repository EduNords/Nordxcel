namespace Nordxcel.Core.Evaluation.Functions.Builtins;

/// <summary>
/// Encontra a raiz de uma função de uma variável. É o que resolve <c>TIR</c> e
/// <c>TAXA</c>, que não têm fórmula fechada.
/// <para>
/// Newton a partir do palpite resolve praticamente todos os casos reais. Quando
/// ele não converge — fluxo com mais de uma troca de sinal, palpite ruim,
/// derivada quase nula — cai numa varredura seguida de bisseção, que é lenta mas
/// só falha se não existir raiz mesmo. O Excel desiste depois de 20 iterações de
/// Newton e devolve #NÚM!; aqui a busca vai mais longe de propósito, porque para
/// quem está aprendendo modelagem uma TIR calculada vale mais que um erro.
/// </para>
/// </summary>
internal static class RootFinder
{
    private const int MaxNewtonIterations = 100;
    private const int MaxBisectionIterations = 200;

    /// <summary>
    /// Tolerância no valor da função, <b>relativa</b> à escala do problema. Uma
    /// tolerância absoluta não serviria: o mesmo fluxo de caixa em reais ou em
    /// milhares de reais tem resíduos de ordens de grandeza diferentes.
    /// </summary>
    private const double RelativeValueTolerance = 1e-9;

    /// <summary>Tolerância no passo para considerar que a busca estacionou.</summary>
    private const double StepTolerance = 1e-12;

    public static bool TrySolve(
        Func<double, double> function,
        double guess,
        double lowerBound,
        double upperBound,
        out double root)
    {
        // A escala do problema sai do primeiro ponto avaliado: é a ordem de
        // grandeza dos fluxos, que define o que conta como resíduo desprezível.
        double scale = Math.Abs(function(double.IsFinite(guess) ? guess : 0.1d));

        if (!double.IsFinite(scale) || scale < 1d)
        {
            scale = 1d;
        }

        double tolerance = RelativeValueTolerance * scale;

        if (TryNewton(function, guess, lowerBound, upperBound, tolerance, out root))
        {
            return true;
        }

        return TryScanAndBisect(function, lowerBound, upperBound, tolerance, out root);
    }

    /// <summary>
    /// Newton refinado até o passo parar de mudar, e não até o resíduo ficar
    /// pequeno. Parar no primeiro resíduo aceitável desperdiçaria a convergência
    /// quadrática e deixaria a raiz com muito menos casas do que ela poderia ter.
    /// </summary>
    private static bool TryNewton(
        Func<double, double> function,
        double guess,
        double lowerBound,
        double upperBound,
        double tolerance,
        out double root)
    {
        root = 0d;

        double current = double.IsFinite(guess) && guess > lowerBound && guess < upperBound
            ? guess
            : 0.1d;

        for (int iteration = 0; iteration < MaxNewtonIterations; iteration++)
        {
            double value = function(current);

            if (!double.IsFinite(value))
            {
                return false;
            }

            if (value == 0d)
            {
                root = current;
                return true;
            }

            if (!TryDerivative(function, current, lowerBound, upperBound, out double slope))
            {
                return false;
            }

            double next = current - value / slope;

            if (!double.IsFinite(next) || next <= lowerBound || next >= upperBound)
            {
                return false;
            }

            double step = Math.Abs(next - current);
            current = next;

            if (step < StepTolerance * Math.Max(1d, Math.Abs(current)))
            {
                break;
            }
        }

        double finalValue = function(current);

        if (double.IsFinite(finalValue) && Math.Abs(finalValue) < tolerance)
        {
            root = current;
            return true;
        }

        return false;
    }

    /// <summary>Derivada por diferença finita, escolhendo o lado que couber dentro dos limites.</summary>
    private static bool TryDerivative(
        Func<double, double> function,
        double at,
        double lowerBound,
        double upperBound,
        out double slope)
    {
        slope = 0d;

        double step = Math.Max(1e-7d, Math.Abs(at) * 1e-7d);

        double left = at - step;
        double right = at + step;

        double value = left > lowerBound && right < upperBound
            ? (function(right) - function(left)) / (2d * step)
            : right < upperBound
                ? (function(right) - function(at)) / step
                : (function(at) - function(left)) / step;

        if (!double.IsFinite(value) || Math.Abs(value) < 1e-14d)
        {
            return false;
        }

        slope = value;
        return true;
    }

    /// <summary>Varre o intervalo atrás de uma troca de sinal e refina por bisseção.</summary>
    private static bool TryScanAndBisect(
        Func<double, double> function,
        double lowerBound,
        double upperBound,
        double tolerance,
        out double root)
    {
        root = 0d;

        double previousPoint = double.NaN;
        double previousValue = double.NaN;

        foreach (double point in Samples(lowerBound, upperBound))
        {
            double value = function(point);

            if (!double.IsFinite(value))
            {
                previousPoint = double.NaN;
                continue;
            }

            if (Math.Abs(value) < tolerance)
            {
                root = point;
                return true;
            }

            if (double.IsFinite(previousPoint) && Math.Sign(value) != Math.Sign(previousValue))
            {
                root = Bisect(function, previousPoint, previousValue, point, tolerance);
                return true;
            }

            previousPoint = point;
            previousValue = value;
        }

        return false;
    }

    private static double Bisect(
        Func<double, double> function,
        double low,
        double lowValue,
        double high,
        double tolerance)
    {
        for (int iteration = 0; iteration < MaxBisectionIterations; iteration++)
        {
            double middle = (low + high) / 2d;
            double value = function(middle);

            if (!double.IsFinite(value) || Math.Abs(value) < tolerance || high - low < StepTolerance)
            {
                return middle;
            }

            if (Math.Sign(value) == Math.Sign(lowValue))
            {
                low = middle;
                lowValue = value;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2d;
    }

    /// <summary>
    /// Pontos de amostragem com passo crescente: fino perto de zero, onde ficam as
    /// taxas de mercado, e grosso lá em cima, onde só aparecem TIRs de venture.
    /// </summary>
    private static IEnumerable<double> Samples(double lowerBound, double upperBound)
    {
        double[] steps = [0.01d, 0.05d, 0.5d, 5d];
        double[] bandLimits = [1d, 10d, 100d, upperBound];

        double point = lowerBound + 1e-6d;
        int band = 0;

        while (point < upperBound && band < steps.Length)
        {
            yield return point;

            point += steps[band];

            while (band < steps.Length && point >= bandLimits[band])
            {
                band++;
            }
        }

        yield return upperBound - 1e-6d;
    }
}
