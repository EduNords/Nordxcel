using System.Collections.Concurrent;
using System.Text;

namespace Nordxcel.Core.Formatting;

/// <summary>
/// Máscara de formato de número na sintaxe do Excel, já interpretada.
/// <para>
/// Uma máscara tem até quatro seções separadas por ponto e vírgula:
/// <c>positivo;negativo;zero;texto</c>. Com uma só, o negativo ganha sinal de menos.
/// Com duas, <c>#,##0;(#,##0)</c> dá o comportamento de parênteses que é padrão em
/// modelagem financeira.
/// </para>
/// </summary>
public sealed class NumberFormat
{
    /// <summary>Máscaras se repetem por milhares de células; interpretar de novo a cada render seria desperdício.</summary>
    private static readonly ConcurrentDictionary<string, NumberFormat> Cache = new(StringComparer.Ordinal);

    private readonly NumberFormatSection[] _sections;

    private NumberFormat(string mask, NumberFormatSection[] sections)
    {
        Mask = mask;
        _sections = sections;
    }

    /// <summary>Texto original da máscara.</summary>
    public string Mask { get; }

    /// <summary>Quantidade de seções declaradas, de 1 a 4.</summary>
    public int SectionCount => _sections.Length;

    /// <summary>Verdadeiro quando a máscara representa uma data.</summary>
    public bool IsDate => _sections[0].IsDate;

    /// <summary>Verdadeiro quando a máscara mostra o número como porcentagem.</summary>
    public bool IsPercent => _sections[0].PercentScale > 0;

    public static NumberFormat Parse(string mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        return Cache.GetOrAdd(mask, static text =>
        {
            List<string> parts = SplitSections(text);
            var sections = new NumberFormatSection[Math.Min(parts.Count, 4)];

            for (int i = 0; i < sections.Length; i++)
            {
                sections[i] = NumberFormatSection.Parse(parts[i]);
            }

            return new NumberFormat(text, sections);
        });
    }

    /// <summary>Seção que se aplica a um número, seguindo a regra de contagem do Excel.</summary>
    internal NumberFormatSection SectionFor(double value, out bool negativeSignNeeded)
    {
        negativeSignNeeded = false;

        switch (_sections.Length)
        {
            case 1:
                // Com uma seção só, o próprio formatador acrescenta o sinal.
                negativeSignNeeded = value < 0d;
                return _sections[0];

            case 2:
                if (value < 0d)
                {
                    return _sections[1];
                }

                return _sections[0];

            default:
                if (value > 0d)
                {
                    return _sections[0];
                }

                if (value < 0d)
                {
                    return _sections[1];
                }

                return _sections[2];
        }
    }

    /// <summary>Seção de texto, presente só em máscaras de quatro seções.</summary>
    internal NumberFormatSection? TextSection => _sections.Length >= 4 ? _sections[3] : null;

    /// <summary>Separa em seções, ignorando o ponto e vírgula dentro de aspas ou escapado.</summary>
    private static List<string> SplitSections(string mask)
    {
        var sections = new List<string>();
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
                sections.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        sections.Add(current.ToString());

        return sections;
    }
}
