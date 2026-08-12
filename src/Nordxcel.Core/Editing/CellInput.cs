using System.Globalization;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Editing;

/// <summary>
/// Traduz entre o que o usuário digita e o que a célula guarda, nos dois sentidos.
/// <para>
/// Digitar <c>12,5%</c> grava 0,125 e já aplica a máscara de porcentagem, como no
/// Excel — o formato vem junto com o conteúdo, e não de um passo separado.
/// </para>
/// </summary>
public static class CellInput
{
    /// <summary>Interpreta o texto digitado, preservando formato e estilo da célula.</summary>
    public static Cell Parse(string? text, Cell? existing = null, NumberFormatCulture? culture = null)
    {
        Cell baseline = existing ?? Cell.Empty;
        NumberFormatCulture format = culture ?? NumberFormatCulture.Default;

        if (string.IsNullOrEmpty(text))
        {
            return baseline with { Formula = null, Value = CellValue.Blank };
        }

        if (text.Length > 1 && text[0] == '=')
        {
            return Cell.FromFormula(text[1..]) with
            {
                NumberFormat = baseline.NumberFormat,
                Style = baseline.Style,
            };
        }

        // Apóstrofo à frente força texto, como no Excel: '001 continua sendo "001".
        if (text[0] == '\'')
        {
            return Literal(baseline, CellValue.Text(text[1..]));
        }

        string trimmed = text.Trim();

        if (string.Equals(trimmed, "VERDADEIRO", StringComparison.OrdinalIgnoreCase))
        {
            return Literal(baseline, CellValue.True);
        }

        if (string.Equals(trimmed, "FALSO", StringComparison.OrdinalIgnoreCase))
        {
            return Literal(baseline, CellValue.False);
        }

        if (CellErrors.TryParse(trimmed, out CellErrorType error))
        {
            return Literal(baseline, CellValue.Error(error));
        }

        if (TryParsePercent(trimmed, format, out double percent))
        {
            return Literal(baseline, CellValue.Number(percent), StandardNumberFormats.Percent);
        }

        if (TryParseCurrency(trimmed, format, out double money, out string currencyMask))
        {
            return Literal(baseline, CellValue.Number(money), currencyMask);
        }

        if (StandardNumberFormats.TryParseDate(trimmed, out double serial))
        {
            return Literal(baseline, CellValue.Number(serial), StandardNumberFormats.ShortDate);
        }

        if (TryParseNumber(trimmed, format, out double number))
        {
            return Literal(baseline, CellValue.Number(number));
        }

        return Literal(baseline, CellValue.Text(text));
    }

    /// <summary>
    /// Texto que a barra de fórmulas mostra ao selecionar a célula.
    /// Data e porcentagem aparecem legíveis, como no Excel; nenhum outro formato
    /// interfere, para o número bruto continuar visível.
    /// </summary>
    public static string ToEditText(Cell cell, NumberFormatCulture? culture = null)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (cell.HasFormula)
        {
            return "=" + cell.Formula;
        }

        NumberFormatCulture format = culture ?? NumberFormatCulture.Default;
        CellValue value = cell.Value;

        switch (value.Kind)
        {
            case CellValueKind.Blank:
                return string.Empty;

            case CellValueKind.Text:
                return value.AsText();

            case CellValueKind.Logical:
                return value.AsLogical() ? "VERDADEIRO" : "FALSO";

            case CellValueKind.Error:
                return value.AsError().ToDisplayText();

            default:
                return NumberToEditText(value.AsNumber(), cell.NumberFormat, format);
        }
    }

    private static string NumberToEditText(double number, string? mask, NumberFormatCulture culture)
    {
        if (!string.IsNullOrWhiteSpace(mask))
        {
            NumberFormat parsed = NumberFormat.Parse(mask);

            if (parsed.IsDate && ExcelDate.TryFromSerial(number, out DateTime date))
            {
                return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }

            if (parsed.IsPercent)
            {
                return Plain(number * 100d, culture) + "%";
            }
        }

        return Plain(number, culture);
    }

    private static string Plain(double number, NumberFormatCulture culture) =>
        number.ToString("G15", culture.General);

    private static Cell Literal(Cell baseline, CellValue value, string? mask = null) => baseline with
    {
        Formula = null,
        Value = value,
        NumberFormat = mask is null ? baseline.NumberFormat : baseline.NumberFormat ?? mask,
    };

    private static bool TryParsePercent(string text, NumberFormatCulture culture, out double value)
    {
        value = 0d;

        if (!text.EndsWith('%'))
        {
            return false;
        }

        if (!TryParseNumber(text[..^1].TrimEnd(), culture, out double raw))
        {
            return false;
        }

        value = raw / 100d;
        return true;
    }

    private static bool TryParseCurrency(
        string text,
        NumberFormatCulture culture,
        out double value,
        out string mask)
    {
        value = 0d;
        mask = StandardNumberFormats.CurrencyReal;

        foreach ((string symbol, string candidate) in CurrencySymbols)
        {
            if (!text.StartsWith(symbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseNumber(text[symbol.Length..].TrimStart(), culture, out value))
            {
                mask = candidate;
                return true;
            }
        }

        return false;
    }

    private static readonly (string Symbol, string Mask)[] CurrencySymbols =
    [
        ("R$", StandardNumberFormats.CurrencyReal),
        ("US$", StandardNumberFormats.CurrencyDollar),
        ("$", StandardNumberFormats.CurrencyDollar),
    ];

    /// <summary>
    /// Interpreta um número digitado, com ou sem separador de milhar.
    /// <para>
    /// O separador só é aceito quando os grupos têm exatamente três dígitos.
    /// Sem essa exigência, <c>1.5</c> em português viraria 15 silenciosamente — um
    /// erro de 10x que ninguém percebe até o valuation estar pronto.
    /// </para>
    /// </summary>
    private static bool TryParseNumber(string text, NumberFormatCulture culture, out double value)
    {
        value = 0d;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (double.TryParse(text, NumberStyles.Float, culture.General, out value))
        {
            return true;
        }

        return TryParseGrouped(text, culture, out value);
    }

    private static bool TryParseGrouped(string text, NumberFormatCulture culture, out double value)
    {
        value = 0d;

        string sign = string.Empty;
        string body = text;

        if (body.Length > 0 && (body[0] == '-' || body[0] == '+'))
        {
            sign = body[..1];
            body = body[1..];
        }

        int decimalIndex = body.IndexOf(culture.DecimalSeparator, StringComparison.Ordinal);
        string integerPart = decimalIndex < 0 ? body : body[..decimalIndex];
        string fraction = decimalIndex < 0 ? string.Empty : body[decimalIndex..];

        if (!integerPart.Contains(culture.GroupSeparator, StringComparison.Ordinal))
        {
            return false;
        }

        string[] groups = integerPart.Split(culture.GroupSeparator);

        if (groups.Length < 2 || groups[0].Length is 0 or > 3)
        {
            return false;
        }

        for (int i = 1; i < groups.Length; i++)
        {
            if (groups[i].Length != 3)
            {
                return false;
            }
        }

        foreach (string group in groups)
        {
            foreach (char c in group)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }
            }
        }

        return double.TryParse(
            sign + string.Concat(groups) + fraction,
            NumberStyles.Float,
            culture.General,
            out value);
    }
}
