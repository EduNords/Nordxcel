using System.Globalization;
using System.Text;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Formatting;

/// <summary>Texto pronto para desenhar, com a cor que a máscara eventualmente impôs.</summary>
public readonly record struct FormattedValue(string Text, RgbColor? Color)
{
    public static readonly FormattedValue Empty = new(string.Empty, null);

    public override string ToString() => Text;
}

/// <summary>
/// Transforma o valor de uma célula no texto que aparece na tela, aplicando a
/// máscara de formato.
/// </summary>
public sealed class CellFormatter
{
    /// <summary>Além disso o <c>double</c> não distingue mais nada.</summary>
    private const int MaxSupportedDecimals = 15;

    private readonly NumberFormatCulture _culture;

    public CellFormatter(NumberFormatCulture? culture = null) =>
        _culture = culture ?? NumberFormatCulture.Default;

    public NumberFormatCulture Culture => _culture;

    /// <summary>Formata o conteúdo de uma célula com a máscara dela.</summary>
    public FormattedValue Format(Cell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        return Format(cell.Value, cell.NumberFormat);
    }

    /// <summary>Formata um valor com a máscara informada. Máscara nula significa formato geral.</summary>
    public FormattedValue Format(CellValue value, string? mask)
    {
        switch (value.Kind)
        {
            case CellValueKind.Blank:
                return FormattedValue.Empty;

            // Erro sempre aparece como erro: nenhuma máscara o disfarça.
            case CellValueKind.Error:
                return new FormattedValue(value.AsError().ToDisplayText(), null);

            case CellValueKind.Logical:
                return new FormattedValue(value.AsLogical() ? "VERDADEIRO" : "FALSO", null);

            case CellValueKind.Text:
                return FormatText(value.AsText(), mask);

            default:
                return FormatNumber(value.AsNumber(), mask);
        }
    }

    /// <summary>Atalho para quem só quer o texto.</summary>
    public string FormatToText(CellValue value, string? mask) => Format(value, mask).Text;

    private FormattedValue FormatText(string text, string? mask)
    {
        if (!HasMask(mask))
        {
            return new FormattedValue(text, null);
        }

        NumberFormatSection? section = NumberFormat.Parse(mask!).TextSection;

        if (section is null)
        {
            // Sem seção de texto, o Excel mostra o texto como está.
            return new FormattedValue(text, null);
        }

        string prefix = section.Prefix;
        string rendered = section.TextPlaceholderIndex >= 0
            ? prefix.Insert(Math.Min(section.TextPlaceholderIndex, prefix.Length), text) + section.Suffix
            : prefix + section.Suffix;

        return new FormattedValue(rendered, section.Color);
    }

    private FormattedValue FormatNumber(double number, string? mask)
    {
        if (!HasMask(mask))
        {
            return new FormattedValue(FormatGeneral(number), null);
        }

        NumberFormatSection section = NumberFormat.Parse(mask!).SectionFor(number, out bool needsSign);

        if (section.IsDate)
        {
            return FormatDate(number, section);
        }

        if (!section.HasNumberPlaceholders)
        {
            // Seção só de texto, como o ";-;" que mostra um traço no lugar do zero.
            return new FormattedValue(section.Prefix + section.Suffix, section.Color);
        }

        double scaled = Math.Abs(number);

        for (int i = 0; i < section.PercentScale; i++)
        {
            scaled *= 100d;
        }

        for (int i = 0; i < section.ThousandScale; i++)
        {
            scaled /= 1000d;
        }

        if (!double.IsFinite(scaled))
        {
            return new FormattedValue(CellErrorType.Number.ToDisplayText(), null);
        }

        int decimals = Math.Min(section.MaxDecimals, MaxSupportedDecimals);
        double rounded = Math.Round(scaled, decimals, MidpointRounding.AwayFromZero);

        string fixedPoint = rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        int dot = fixedPoint.IndexOf('.', StringComparison.Ordinal);
        string integerDigits = dot < 0 ? fixedPoint : fixedPoint[..dot];
        string decimalDigits = dot < 0 ? string.Empty : fixedPoint[(dot + 1)..];

        integerDigits = ApplyIntegerPlaceholders(integerDigits, section.MinIntegerDigits);
        decimalDigits = TrimOptionalDecimals(decimalDigits, section.MinDecimals);

        var builder = new StringBuilder();

        // O sinal vem antes do prefixo para que "-R$ 100" não vire "R$ -100".
        if (needsSign)
        {
            builder.Append('-');
        }

        builder.Append(section.Prefix);

        builder.Append(section.UseGrouping ? Group(integerDigits) : integerDigits);

        if (decimalDigits.Length > 0)
        {
            builder.Append(_culture.DecimalSeparator).Append(decimalDigits);
        }

        builder.Append(section.Suffix);

        return new FormattedValue(builder.ToString(), section.Color);
    }

    private FormattedValue FormatDate(double serial, NumberFormatSection section)
    {
        if (!ExcelDate.TryFromSerial(serial, out DateTime date))
        {
            return new FormattedValue(CellErrorType.Number.ToDisplayText(), section.Color);
        }

        var builder = new StringBuilder();

        foreach (DatePart part in section.DateParts)
        {
            builder.Append(part.Kind switch
            {
                DateTokenKind.Literal => part.Literal,
                DateTokenKind.Day => date.Day.ToString(CultureInfo.InvariantCulture),
                DateTokenKind.DayPadded => date.Day.ToString("00", CultureInfo.InvariantCulture),
                DateTokenKind.Month => date.Month.ToString(CultureInfo.InvariantCulture),
                DateTokenKind.MonthPadded => date.Month.ToString("00", CultureInfo.InvariantCulture),
                DateTokenKind.YearShort => (date.Year % 100).ToString("00", CultureInfo.InvariantCulture),
                DateTokenKind.YearLong => date.Year.ToString("0000", CultureInfo.InvariantCulture),
                DateTokenKind.Hour => date.Hour.ToString(CultureInfo.InvariantCulture),
                DateTokenKind.HourPadded => date.Hour.ToString("00", CultureInfo.InvariantCulture),
                DateTokenKind.Minute => date.Minute.ToString(CultureInfo.InvariantCulture),
                DateTokenKind.MinutePadded => date.Minute.ToString("00", CultureInfo.InvariantCulture),
                DateTokenKind.Second => date.Second.ToString(CultureInfo.InvariantCulture),
                DateTokenKind.SecondPadded => date.Second.ToString("00", CultureInfo.InvariantCulture),
                _ => string.Empty,
            });
        }

        return new FormattedValue(builder.ToString(), section.Color);
    }

    /// <summary>Formato geral: até 15 dígitos significativos, sem separador de milhar.</summary>
    private string FormatGeneral(double number) => number.ToString("G15", _culture.General);

    private static string ApplyIntegerPlaceholders(string digits, int minimumDigits)
    {
        if (digits.Length < minimumDigits)
        {
            return digits.PadLeft(minimumDigits, '0');
        }

        // Máscara sem nenhum zero na parte inteira esconde o zero de "0,5".
        if (minimumDigits == 0 && digits == "0")
        {
            return string.Empty;
        }

        return digits;
    }

    private static string TrimOptionalDecimals(string digits, int minimumDigits)
    {
        int length = digits.Length;

        while (length > minimumDigits && digits[length - 1] == '0')
        {
            length--;
        }

        return digits[..length];
    }

    private string Group(string digits)
    {
        if (digits.Length <= 3)
        {
            return digits;
        }

        var builder = new StringBuilder(digits.Length + (digits.Length / 3 * _culture.GroupSeparator.Length));
        int firstGroup = digits.Length % 3;

        if (firstGroup == 0)
        {
            firstGroup = 3;
        }

        builder.Append(digits, 0, firstGroup);

        for (int i = firstGroup; i < digits.Length; i += 3)
        {
            builder.Append(_culture.GroupSeparator).Append(digits, i, 3);
        }

        return builder.ToString();
    }

    private static bool HasMask(string? mask) =>
        !string.IsNullOrWhiteSpace(mask) &&
        !string.Equals(mask, "General", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(mask, "Geral", StringComparison.OrdinalIgnoreCase);
}
