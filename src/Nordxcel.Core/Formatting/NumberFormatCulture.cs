using System.Globalization;

namespace Nordxcel.Core.Formatting;

/// <summary>
/// Separadores usados na <b>exibição</b>. Não confundir com os da máscara: a
/// máscara é sempre escrita na forma canônica do Excel, com ponto decimal e
/// vírgula de milhar (<c>#,##0.00</c>), e a cultura decide como isso aparece na
/// tela. É essa separação que deixa a exportação para .xlsx ser cópia direta.
/// </summary>
public sealed class NumberFormatCulture
{
    private NumberFormatCulture(string decimalSeparator, string groupSeparator)
    {
        DecimalSeparator = decimalSeparator;
        GroupSeparator = groupSeparator;

        General = new NumberFormatInfo
        {
            NumberDecimalSeparator = decimalSeparator,
            NumberGroupSeparator = groupSeparator,
        };
    }

    /// <summary>Brasil: <c>1.234.567,89</c>.</summary>
    public static NumberFormatCulture PtBr { get; } = new(",", ".");

    /// <summary>Estados Unidos: <c>1,234,567.89</c>.</summary>
    public static NumberFormatCulture EnUs { get; } = new(".", ",");

    public static NumberFormatCulture Default => PtBr;

    public string DecimalSeparator { get; }

    public string GroupSeparator { get; }

    /// <summary>Usado no formato geral, quando a célula não tem máscara.</summary>
    internal NumberFormatInfo General { get; }
}
