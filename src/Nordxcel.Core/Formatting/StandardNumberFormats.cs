using System.Globalization;
using System.Text;

namespace Nordxcel.Core.Formatting;

/// <summary>
/// Máscaras prontas para os botões da barra de formatação, todas na convenção de
/// Investment Banking: negativo entre parênteses, nunca com sinal de menos.
/// </summary>
public static class StandardNumberFormats
{
    /// <summary>Sem máscara: o número aparece como foi digitado.</summary>
    public const string General = "General";

    /// <summary>Número com separador de milhar: <c>1.234.567</c> e <c>(1.234.567)</c>.</summary>
    public const string Thousands = "#,##0;(#,##0)";

    /// <summary>Moeda em real: <c>R$ 1.234.567</c> e <c>(R$ 1.234.567)</c>.</summary>
    public const string CurrencyReal = "\"R$ \"#,##0;(\"R$ \"#,##0)";

    /// <summary>Moeda em dólar: <c>$1.234.567</c> e <c>($1.234.567)</c>.</summary>
    public const string CurrencyDollar = "\"$\"#,##0;(\"$\"#,##0)";

    /// <summary>Porcentagem com uma casa: <c>12,5%</c> e <c>(12,5%)</c>.</summary>
    public const string Percent = "0.0%;(0.0%)";

    /// <summary>
    /// Múltiplo com uma casa: <c>10,2x</c> e <c>(10,2x)</c>. O <c>x</c> entra como
    /// literal entre aspas, que é sintaxe nativa do Excel — a exportação para .xlsx
    /// sai sem nenhuma tradução.
    /// </summary>
    public const string Multiple = "0.0\"x\";(0.0\"x\")";

    /// <summary>Valores em milhares: <c>1.235</c> para 1.234.567.</summary>
    public const string InThousands = "#,##0,;(#,##0,)";

    /// <summary>Valores em milhões: <c>1,2</c> para 1.234.567.</summary>
    public const string InMillions = "#,##0.0,,;(#,##0.0,,)";

    /// <summary>Data no padrão brasileiro. A máscara é canônica, com <c>yyyy</c>.</summary>
    public const string ShortDate = "dd/mm/yyyy";

    /// <summary>Negativo em vermelho, comum em linha de variação.</summary>
    public const string ThousandsRedNegative = "#,##0;[Red](#,##0)";

    /// <summary>Máscara com uma moeda customizada, como € ou US$.</summary>
    public static string Currency(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        string escaped = symbol.Replace("\"", string.Empty, StringComparison.Ordinal);

        return $"\"{escaped} \"#,##0;(\"{escaped} \"#,##0)";
    }

    /// <summary>Quantidade de casas decimais da máscara, ou zero se ela não tiver.</summary>
    public static int GetDecimals(string? mask)
    {
        if (string.IsNullOrWhiteSpace(mask))
        {
            return 0;
        }

        int most = 0;

        foreach (string section in Sections(mask))
        {
            most = Math.Max(most, CountDecimals(section));
        }

        return most;
    }

    /// <summary>Botão de aumentar casas decimais.</summary>
    public static string IncreaseDecimals(string? mask) =>
        WithDecimals(mask, Math.Min(GetDecimals(mask) + 1, 15));

    /// <summary>Botão de diminuir casas decimais.</summary>
    public static string DecreaseDecimals(string? mask) =>
        WithDecimals(mask, Math.Max(GetDecimals(mask) - 1, 0));

    /// <summary>
    /// Reescreve a máscara com outra quantidade de casas decimais, preservando
    /// prefixo, sufixo, parênteses e cor de cada seção.
    /// </summary>
    public static string WithDecimals(string? mask, int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);

        string source = string.IsNullOrWhiteSpace(mask) || IsGeneral(mask)
            ? Thousands
            : mask!;

        var rewritten = new List<string>();

        foreach (string section in Sections(source))
        {
            rewritten.Add(RewriteSection(section, decimals));
        }

        return string.Join(';', rewritten);
    }

    private static bool IsGeneral(string? mask) =>
        string.Equals(mask, General, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mask, "Geral", StringComparison.OrdinalIgnoreCase);

    private static string RewriteSection(string section, int decimals)
    {
        var builder = new StringBuilder();
        bool inQuotes = false;
        bool written = false;
        int lastPlaceholder = -1;

        for (int i = 0; i < section.Length; i++)
        {
            char c = section[i];

            if (c == '\\' && i + 1 < section.Length)
            {
                builder.Append(c).Append(section[i + 1]);
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                builder.Append(c);
                continue;
            }

            if (inQuotes)
            {
                builder.Append(c);
                continue;
            }

            if (c == '.')
            {
                // Substitui a parte decimal inteira pela nova quantidade de casas.
                int end = i + 1;

                while (end < section.Length && section[end] is '0' or '#' or '?')
                {
                    end++;
                }

                AppendDecimals(builder, decimals);
                written = true;
                i = end - 1;
                continue;
            }

            if (c is '0' or '#' or '?')
            {
                lastPlaceholder = builder.Length;
            }

            builder.Append(c);
        }

        if (!written && decimals > 0 && lastPlaceholder >= 0)
        {
            var withDecimals = new StringBuilder();
            AppendDecimals(withDecimals, decimals);

            builder.Insert(lastPlaceholder + 1, withDecimals.ToString());
        }

        return builder.ToString();
    }

    private static void AppendDecimals(StringBuilder builder, int decimals)
    {
        if (decimals <= 0)
        {
            return;
        }

        builder.Append('.').Append('0', decimals);
    }

    private static int CountDecimals(string section)
    {
        bool inQuotes = false;

        for (int i = 0; i < section.Length; i++)
        {
            char c = section[i];

            if (c == '\\' && i + 1 < section.Length)
            {
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes || c != '.')
            {
                continue;
            }

            int count = 0;
            int position = i + 1;

            while (position < section.Length && section[position] is '0' or '#' or '?')
            {
                count++;
                position++;
            }

            return count;
        }

        return 0;
    }

    private static IEnumerable<string> Sections(string mask)
    {
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < mask.Length; i++)
        {
            char c = mask[i];

            if (c == '\\' && i + 1 < mask.Length)
            {
                current.Append(c).Append(mask[i + 1]);
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
                continue;
            }

            if (c == ';' && !inQuotes)
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        yield return current.ToString();
    }

    /// <summary>Converte uma data para o número serial que a célula guarda.</summary>
    public static double ToSerial(DateTime date) => ExcelDate.ToSerial(date);

    /// <summary>Interpreta uma data digitada no padrão brasileiro.</summary>
    public static bool TryParseDate(string text, out double serial)
    {
        serial = 0d;

        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd"];

        if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
        {
            return false;
        }

        serial = ExcelDate.ToSerial(date);
        return true;
    }
}
