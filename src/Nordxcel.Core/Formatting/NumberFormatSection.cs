using System.Text;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Formatting;

internal enum DateTokenKind
{
    Literal,
    Day,
    DayPadded,
    Month,
    MonthPadded,
    YearShort,
    YearLong,
    Hour,
    HourPadded,
    Minute,
    MinutePadded,
    Second,
    SecondPadded,
}

internal readonly record struct DatePart(DateTokenKind Kind, string Literal);

/// <summary>
/// Uma das seções de uma máscara, já interpretada. Uma máscara tem até quatro,
/// separadas por ponto e vírgula: positivo, negativo, zero e texto.
/// </summary>
internal sealed class NumberFormatSection
{
    public static readonly NumberFormatSection Empty = new();

    /// <summary>Cor declarada entre colchetes, como em <c>[Red](#,##0)</c>.</summary>
    public RgbColor? Color { get; init; }

    /// <summary>Texto fixo antes dos dígitos, incluindo símbolo de moeda.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Texto fixo depois dos dígitos, incluindo o <c>%</c> e o <c>x</c> de múltiplo.</summary>
    public string Suffix { get; init; } = string.Empty;

    /// <summary>Quantidade de <c>0</c> na parte inteira, que força zeros à esquerda.</summary>
    public int MinIntegerDigits { get; init; }

    /// <summary>Quantidade de <c>0</c> na parte decimal, que força zeros à direita.</summary>
    public int MinDecimals { get; init; }

    /// <summary>Quantidade total de marcadores decimais, que define o arredondamento.</summary>
    public int MaxDecimals { get; init; }

    public bool UseGrouping { get; init; }

    /// <summary>Vírgulas no fim da máscara, cada uma dividindo por mil.</summary>
    public int ThousandScale { get; init; }

    /// <summary>Sinais de porcentagem, cada um multiplicando por cem.</summary>
    public int PercentScale { get; init; }

    /// <summary>Falso quando a seção só tem texto fixo, como em <c>"-"</c> para o zero.</summary>
    public bool HasNumberPlaceholders { get; init; }

    public bool IsDate { get; init; }

    public IReadOnlyList<DatePart> DateParts { get; init; } = [];

    /// <summary>Posição do <c>@</c> na seção de texto, ou -1 quando não há.</summary>
    public int TextPlaceholderIndex { get; init; } = -1;

    public static NumberFormatSection Parse(string section)
    {
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();
        var dateParts = new List<DatePart>();
        var literalRun = new StringBuilder();

        RgbColor? color = null;
        int minIntegerDigits = 0;
        int minDecimals = 0;
        int maxDecimals = 0;
        bool useGrouping = false;
        int thousandScale = 0;
        int percentScale = 0;
        bool seenPlaceholder = false;
        bool inDecimals = false;
        bool isDate = false;
        int textPlaceholder = -1;

        // Antes do primeiro marcador de dígito o texto fixo é prefixo; depois, sufixo.
        StringBuilder Literals() => seenPlaceholder ? suffix : prefix;

        void FlushLiterals()
        {
            if (literalRun.Length == 0)
            {
                return;
            }

            dateParts.Add(new DatePart(DateTokenKind.Literal, literalRun.ToString()));
            literalRun.Clear();
        }

        void AddLiteral(string text)
        {
            Literals().Append(text);
            literalRun.Append(text);
        }

        for (int i = 0; i < section.Length; i++)
        {
            char current = section[i];

            switch (current)
            {
                case '[':
                {
                    int close = section.IndexOf(']', i);

                    if (close < 0)
                    {
                        AddLiteral(section[i..]);
                        i = section.Length;
                        break;
                    }

                    string content = section[(i + 1)..close];

                    if (TryParseColorName(content, out RgbColor named))
                    {
                        color = named;
                    }

                    i = close;
                    break;
                }

                case '"':
                {
                    int close = section.IndexOf('"', i + 1);

                    if (close < 0)
                    {
                        AddLiteral(section[(i + 1)..]);
                        i = section.Length;
                        break;
                    }

                    AddLiteral(section[(i + 1)..close]);
                    i = close;
                    break;
                }

                case '\\':
                    if (i + 1 < section.Length)
                    {
                        AddLiteral(section[i + 1].ToString());
                        i++;
                    }

                    break;

                // Reserva de largura e preenchimento do Excel: aqui viram um espaço e nada.
                case '_':
                    AddLiteral(" ");
                    i++;
                    break;

                case '*':
                    i++;
                    break;

                case '0':
                case '#':
                case '?':
                    seenPlaceholder = true;
                    FlushLiterals();

                    if (inDecimals)
                    {
                        maxDecimals++;

                        if (current == '0')
                        {
                            minDecimals = maxDecimals;
                        }
                    }
                    else if (current == '0')
                    {
                        minIntegerDigits++;
                    }

                    break;

                case '.':
                    inDecimals = true;
                    break;

                case ',':
                {
                    int lookahead = i;

                    while (lookahead < section.Length && section[lookahead] == ',')
                    {
                        lookahead++;
                    }

                    bool followedByDigits = lookahead < section.Length && IsPlaceholder(section[lookahead]);

                    if (followedByDigits)
                    {
                        useGrouping = true;
                        break;
                    }

                    // Vírgula sem dígito depois é escala: cada uma divide por mil.
                    thousandScale += lookahead - i;
                    i = lookahead - 1;
                    break;
                }

                case '%':
                    percentScale++;
                    AddLiteral("%");
                    break;

                case '@':
                    textPlaceholder = Literals().Length;
                    seenPlaceholder = true;
                    break;

                case 'd':
                case 'D':
                case 'y':
                case 'Y':
                case 'a':
                case 'A':
                case 'm':
                case 'M':
                case 'h':
                case 'H':
                case 's':
                case 'S':
                {
                    int run = 1;

                    while (i + run < section.Length && char.ToLowerInvariant(section[i + run]) == char.ToLowerInvariant(current))
                    {
                        run++;
                    }

                    FlushLiterals();
                    dateParts.Add(new DatePart(ResolveDateToken(section, i, current, run, dateParts), string.Empty));

                    isDate = true;
                    i += run - 1;
                    break;
                }

                default:
                    AddLiteral(current.ToString());
                    break;
            }
        }

        FlushLiterals();

        return new NumberFormatSection
        {
            Color = color,
            Prefix = prefix.ToString(),
            Suffix = suffix.ToString(),
            MinIntegerDigits = minIntegerDigits,
            MinDecimals = minDecimals,
            MaxDecimals = maxDecimals,
            UseGrouping = useGrouping,
            ThousandScale = thousandScale,
            PercentScale = percentScale,
            HasNumberPlaceholders = seenPlaceholder && textPlaceholder < 0 && !isDate,
            IsDate = isDate,
            DateParts = dateParts,
            TextPlaceholderIndex = textPlaceholder,
        };
    }

    private static bool IsPlaceholder(char c) => c is '0' or '#' or '?';

    /// <summary>
    /// Decide se <c>m</c> é mês ou minuto. A regra do Excel é o contexto: logo
    /// depois de hora ou logo antes de segundo, é minuto.
    /// </summary>
    private static DateTokenKind ResolveDateToken(
        string section,
        int index,
        char letter,
        int run,
        List<DatePart> previous)
    {
        switch (char.ToLowerInvariant(letter))
        {
            case 'd':
                return run >= 2 ? DateTokenKind.DayPadded : DateTokenKind.Day;

            case 'y':
            case 'a':
                return run >= 3 ? DateTokenKind.YearLong : DateTokenKind.YearShort;

            case 'h':
                return run >= 2 ? DateTokenKind.HourPadded : DateTokenKind.Hour;

            case 's':
                return run >= 2 ? DateTokenKind.SecondPadded : DateTokenKind.Second;

            default:
            {
                if (IsAfterHour(previous) || IsBeforeSecond(section, index + run))
                {
                    return run >= 2 ? DateTokenKind.MinutePadded : DateTokenKind.Minute;
                }

                return run >= 2 ? DateTokenKind.MonthPadded : DateTokenKind.Month;
            }
        }
    }

    /// <summary>Último token de data anterior, ignorando os separadores como <c>:</c> e espaço.</summary>
    private static bool IsAfterHour(List<DatePart> previous)
    {
        for (int i = previous.Count - 1; i >= 0; i--)
        {
            if (previous[i].Kind is DateTokenKind.Literal)
            {
                continue;
            }

            return previous[i].Kind is DateTokenKind.Hour or DateTokenKind.HourPadded;
        }

        return false;
    }

    /// <summary>Próximo token de data, ignorando os separadores.</summary>
    private static bool IsBeforeSecond(string section, int from)
    {
        for (int i = from; i < section.Length; i++)
        {
            if (!char.IsLetter(section[i]))
            {
                continue;
            }

            return char.ToLowerInvariant(section[i]) == 's';
        }

        return false;
    }

    private static bool TryParseColorName(string name, out RgbColor color)
    {
        color = default;

        switch (name.Trim().ToUpperInvariant())
        {
            case "BLACK":
            case "PRETO":
                color = new RgbColor(0, 0, 0);
                return true;
            case "RED":
            case "VERMELHO":
                color = new RgbColor(255, 0, 0);
                return true;
            case "GREEN":
            case "VERDE":
                color = new RgbColor(0, 128, 0);
                return true;
            case "BLUE":
            case "AZUL":
                color = new RgbColor(0, 0, 255);
                return true;
            case "YELLOW":
            case "AMARELO":
                color = new RgbColor(255, 255, 0);
                return true;
            case "MAGENTA":
                color = new RgbColor(255, 0, 255);
                return true;
            case "CYAN":
            case "CIANO":
                color = new RgbColor(0, 255, 255);
                return true;
            case "WHITE":
            case "BRANCO":
                color = new RgbColor(255, 255, 255);
                return true;
            default:
                return false;
        }
    }
}
