using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation;

/// <summary>
/// Empacota resultados numéricos como valor de célula. Centraliza duas garantias:
/// infinito e NaN nunca chegam à planilha, e a potência se comporta igual no
/// operador <c>^</c> e na função <c>POTÊNCIA</c>.
/// </summary>
public static class NumericResult
{
    /// <summary>Número, ou <c>#NÚM!</c> quando o cálculo estourou ou ficou indeterminado.</summary>
    public static CellValue FromDouble(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? CellValue.Error(CellErrorType.Number)
            : CellValue.Number(value);

    /// <summary>Potência com os casos de borda tratados como no Excel.</summary>
    public static CellValue Power(double baseValue, double exponent)
    {
        if (baseValue == 0d)
        {
            // 0^0 é indeterminado e 0^negativo é divisão por zero.
            if (exponent == 0d)
            {
                return CellValue.Error(CellErrorType.Number);
            }

            if (exponent < 0d)
            {
                return CellValue.Error(CellErrorType.DivideByZero);
            }
        }

        // Raiz de índice par sobre base negativa não tem resultado real.
        if (baseValue < 0d && exponent != Math.Truncate(exponent))
        {
            return CellValue.Error(CellErrorType.Number);
        }

        return FromDouble(Math.Pow(baseValue, exponent));
    }
}
