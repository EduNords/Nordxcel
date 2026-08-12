using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions;

/// <summary>
/// Leitura de argumentos com as regras do Excel.
/// <para>
/// A regra que menos se espera é a distinção entre valor vindo de célula e valor
/// escrito direto na fórmula: <c>SOMA(VERDADEIRO)</c> dá 1, mas <c>SOMA(A1)</c>
/// com <c>A1</c> contendo VERDADEIRO dá 0. Dentro de referência e intervalo, texto
/// e lógico são ignorados; escritos na fórmula, são convertidos.
/// </para>
/// </summary>
public static class FunctionArguments
{
    /// <summary>Verdadeiro quando o argumento é uma referência ou intervalo, e não um valor calculado.</summary>
    public static bool IsReference(FunctionCall call, int index)
    {
        ArgumentNullException.ThrowIfNull(call);

        return call.GetNode(index) is ReferenceNode or RangeNode;
    }

    /// <summary>
    /// Junta todos os números dos argumentos, na ordem em que aparecem.
    /// Devolve <c>false</c> e o erro encontrado assim que topa com um.
    /// </summary>
    public static bool TryCollectNumbers(FunctionCall call, List<double> numbers, out CellErrorType error)
    {
        ArgumentNullException.ThrowIfNull(call);

        return TryCollectNumbers(call, 0, call.ArgumentCount, numbers, out error);
    }

    /// <summary>
    /// Igual ao anterior, mas restrito a uma faixa de argumentos. Serve para
    /// funções cujos primeiros argumentos não fazem parte da série, como a taxa
    /// em <c>VPL(taxa;valor1;valor2;...)</c>.
    /// </summary>
    public static bool TryCollectNumbers(
        FunctionCall call,
        int startIndex,
        int endExclusive,
        List<double> numbers,
        out CellErrorType error)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(numbers);

        error = CellErrorType.None;

        for (int index = startIndex; index < endExclusive; index++)
        {
            if (call.IsMissing(index))
            {
                continue;
            }

            bool fromReference = IsReference(call, index);

            foreach (CellValue value in call.EnumerateValues(index))
            {
                if (value.IsError)
                {
                    error = value.AsError();
                    return false;
                }

                if (value.IsBlank)
                {
                    continue;
                }

                if (fromReference)
                {
                    // Texto e lógico dentro de célula não entram na conta.
                    if (value.IsNumber)
                    {
                        numbers.Add(value.AsNumber());
                    }

                    continue;
                }

                if (!ValueCoercion.TryToNumber(value, call.Evaluator.Syntax, out double number, out error))
                {
                    return false;
                }

                numbers.Add(number);
            }
        }

        return true;
    }

    /// <summary>
    /// Junta todos os valores lógicos dos argumentos, aplicando a mesma distinção
    /// entre referência e literal usada nos números.
    /// </summary>
    public static bool TryCollectLogicals(FunctionCall call, List<bool> logicals, out CellErrorType error)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(logicals);

        error = CellErrorType.None;

        for (int index = 0; index < call.ArgumentCount; index++)
        {
            if (call.IsMissing(index))
            {
                continue;
            }

            bool fromReference = IsReference(call, index);

            foreach (CellValue value in call.EnumerateValues(index))
            {
                if (value.IsError)
                {
                    error = value.AsError();
                    return false;
                }

                if (value.IsBlank)
                {
                    continue;
                }

                if (fromReference && value.IsText)
                {
                    continue;
                }

                if (!ValueCoercion.TryToLogical(value, out bool logical, out error))
                {
                    return false;
                }

                logicals.Add(logical);
            }
        }

        return true;
    }

    /// <summary>Lê um argumento como número único.</summary>
    public static bool TryGetNumber(FunctionCall call, int index, out double number, out CellErrorType error)
    {
        ArgumentNullException.ThrowIfNull(call);

        CellValue value = call.GetScalar(index);

        if (value.IsError)
        {
            number = 0d;
            error = value.AsError();
            return false;
        }

        return ValueCoercion.TryToNumber(value, call.Evaluator.Syntax, out number, out error);
    }

    /// <summary>Lê um argumento como valor lógico único.</summary>
    public static bool TryGetLogical(FunctionCall call, int index, out bool logical, out CellErrorType error)
    {
        ArgumentNullException.ThrowIfNull(call);

        CellValue value = call.GetScalar(index);

        if (value.IsError)
        {
            logical = false;
            error = value.AsError();
            return false;
        }

        return ValueCoercion.TryToLogical(value, out logical, out error);
    }
}
