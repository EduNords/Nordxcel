using System.Globalization;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation;

/// <summary>
/// Conversões entre tipos de célula, seguindo as regras do Excel. São elas que
/// fazem <c>"10"+5</c> dar 15 e <c>VERDADEIRO+1</c> dar 2.
/// </summary>
public static class ValueCoercion
{
    /// <summary>
    /// Converte para número em contexto aritmético. Texto que parece número é
    /// aceito; texto que não é vira <c>#VALOR!</c>.
    /// </summary>
    public static bool TryToNumber(CellValue value, FormulaSyntax syntax, out double number, out CellErrorType error)
    {
        ArgumentNullException.ThrowIfNull(syntax);

        error = CellErrorType.None;

        if (value.IsError)
        {
            number = 0d;
            error = value.AsError();
            return false;
        }

        if (value.TryGetNumber(out number))
        {
            return true;
        }

        if (double.TryParse(value.AsText(), NumberStyles.Float, syntax.NumberFormat, out number))
        {
            return true;
        }

        number = 0d;
        error = CellErrorType.Value;
        return false;
    }

    /// <summary>Converte para texto em contexto de concatenação.</summary>
    public static bool TryToText(CellValue value, FormulaSyntax syntax, out string text, out CellErrorType error)
    {
        ArgumentNullException.ThrowIfNull(syntax);

        error = CellErrorType.None;

        switch (value.Kind)
        {
            case CellValueKind.Blank:
                text = string.Empty;
                return true;

            case CellValueKind.Text:
                text = value.AsText();
                return true;

            case CellValueKind.Number:
                text = FormatNumber(value.AsNumber(), syntax);
                return true;

            case CellValueKind.Logical:
                text = value.AsLogical() ? "VERDADEIRO" : "FALSO";
                return true;

            default:
                text = string.Empty;
                error = value.AsError();
                return false;
        }
    }

    /// <summary>
    /// Converte para lógico. Número diferente de zero é verdadeiro; texto só é
    /// aceito quando é literalmente VERDADEIRO ou FALSO, como no Excel.
    /// </summary>
    public static bool TryToLogical(CellValue value, out bool logical, out CellErrorType error)
    {
        error = CellErrorType.None;

        switch (value.Kind)
        {
            case CellValueKind.Blank:
            case CellValueKind.Number:
            case CellValueKind.Logical:
                logical = value.AsLogical();
                return true;

            case CellValueKind.Text:
                string text = value.AsText();

                if (string.Equals(text, "VERDADEIRO", StringComparison.OrdinalIgnoreCase))
                {
                    logical = true;
                    return true;
                }

                if (string.Equals(text, "FALSO", StringComparison.OrdinalIgnoreCase))
                {
                    logical = false;
                    return true;
                }

                logical = false;
                error = CellErrorType.Value;
                return false;

            default:
                logical = false;
                error = value.AsError();
                return false;
        }
    }

    /// <summary>Número no formato geral, como o Excel mostra uma célula sem máscara.</summary>
    public static string FormatNumber(double value, FormulaSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);

        // O Excel guarda 15 dígitos significativos; usar mais expõe ruído de ponto flutuante.
        return value.ToString("G15", syntax.NumberFormat);
    }

    /// <summary>
    /// Ordena dois valores pelas regras de comparação do Excel: vazio assume o tipo
    /// do outro lado, texto compara sem diferenciar maiúsculas e, entre tipos
    /// diferentes, número vem antes de texto, que vem antes de lógico.
    /// Não trata erros — o chamador precisa filtrá-los antes.
    /// </summary>
    public static int Compare(CellValue left, CellValue right)
    {
        if (left.IsBlank && right.IsBlank)
        {
            return 0;
        }

        if (left.IsBlank)
        {
            left = NeutralOf(right);
        }
        else if (right.IsBlank)
        {
            right = NeutralOf(left);
        }

        int byRank = Rank(left.Kind).CompareTo(Rank(right.Kind));

        if (byRank != 0)
        {
            return byRank;
        }

        return left.Kind switch
        {
            CellValueKind.Number => left.AsNumber().CompareTo(right.AsNumber()),
            CellValueKind.Text => Math.Sign(string.Compare(
                left.AsText(),
                right.AsText(),
                StringComparison.InvariantCultureIgnoreCase)),
            CellValueKind.Logical => left.AsLogical().CompareTo(right.AsLogical()),
            _ => 0,
        };
    }

    /// <summary>Valor neutro do tipo do outro operando, que é como o Excel enxerga uma célula vazia.</summary>
    private static CellValue NeutralOf(CellValue other) => other.Kind switch
    {
        CellValueKind.Text => CellValue.Text(string.Empty),
        CellValueKind.Logical => CellValue.False,
        _ => CellValue.Number(0d),
    };

    private static int Rank(CellValueKind kind) => kind switch
    {
        CellValueKind.Number => 0,
        CellValueKind.Text => 1,
        CellValueKind.Logical => 2,
        _ => 3,
    };
}
