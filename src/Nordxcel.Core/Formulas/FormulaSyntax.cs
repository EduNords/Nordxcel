using System.Globalization;

namespace Nordxcel.Core.Formulas;

/// <summary>
/// Convenções de escrita de fórmula: separador decimal e separador de argumentos.
/// <para>
/// O padrão é o do Excel em português — decimal com vírgula e argumentos com
/// ponto e vírgula (<c>SOMA(A1:A3;1,5)</c>) — coerente com os nomes de função em
/// português que o Nordxcel usa. Os dois separadores precisam ser diferentes, que é
/// justamente o motivo de o Excel pt-BR trocar a vírgula por ponto e vírgula.
/// </para>
/// </summary>
public sealed class FormulaSyntax
{
    private FormulaSyntax(char decimalSeparator, char argumentSeparator)
    {
        if (decimalSeparator == argumentSeparator)
        {
            throw new ArgumentException(
                "O separador decimal e o de argumentos precisam ser diferentes.",
                nameof(argumentSeparator));
        }

        DecimalSeparator = decimalSeparator;
        ArgumentSeparator = argumentSeparator;

        NumberFormat = new NumberFormatInfo
        {
            NumberDecimalSeparator = decimalSeparator.ToString(),
        };
    }

    /// <summary>Convenção brasileira: <c>1,5</c> e <c>SOMA(A1;B1)</c>.</summary>
    public static FormulaSyntax PtBr { get; } = new(',', ';');

    /// <summary>Convenção americana: <c>1.5</c> e <c>SUM(A1,B1)</c>. Reservada para uma versão futura.</summary>
    public static FormulaSyntax EnUs { get; } = new('.', ',');

    /// <summary>Convenção usada quando nenhuma é informada.</summary>
    public static FormulaSyntax Default => PtBr;

    public char DecimalSeparator { get; }

    public char ArgumentSeparator { get; }

    internal NumberFormatInfo NumberFormat { get; }
}
