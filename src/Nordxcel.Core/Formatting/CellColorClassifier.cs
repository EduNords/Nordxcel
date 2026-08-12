using Nordxcel.Core.Calculation;
using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Formatting;

/// <summary>Papel de uma célula no modelo, que define a cor automática da fonte.</summary>
public enum CellColorRole
{
    /// <summary>Premissa digitada à mão. Azul.</summary>
    Input,

    /// <summary>Fórmula que só usa a própria aba. Preto.</summary>
    Formula,

    /// <summary>Fórmula que puxa valor de outra aba. Verde.</summary>
    Link,
}

/// <summary>
/// Sistema de cores automático de modelagem financeira. É convenção de mercado em
/// IB e PE: azul para o que se pode mexer, preto para o que é calculado ali mesmo,
/// verde para o que vem de outra aba. Quem revisa o modelo lê a cor antes de ler a
/// fórmula, e uma premissa preta no meio do cálculo denuncia número chumbado.
/// </summary>
public static class CellColorClassifier
{
    public static readonly RgbColor InputColor = new(0, 0, 255);

    public static readonly RgbColor FormulaColor = new(0, 0, 0);

    public static readonly RgbColor LinkColor = new(0, 128, 0);

    /// <summary>Classifica pela árvore da fórmula. <c>null</c> significa entrada manual.</summary>
    public static CellColorRole Classify(FormulaNode? formula, string currentSheet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSheet);

        if (formula is null)
        {
            return CellColorRole.Input;
        }

        var cells = new List<CellLocation>();
        var ranges = new List<RangeLocation>();

        FormulaDependencyScanner.Collect(formula, currentSheet, cells, ranges);

        foreach (CellLocation cell in cells)
        {
            if (!string.Equals(cell.SheetName, currentSheet, StringComparison.OrdinalIgnoreCase))
            {
                return CellColorRole.Link;
            }
        }

        foreach (RangeLocation range in ranges)
        {
            if (!string.Equals(range.SheetName, currentSheet, StringComparison.OrdinalIgnoreCase))
            {
                return CellColorRole.Link;
            }
        }

        return CellColorRole.Formula;
    }

    public static RgbColor ColorOf(CellColorRole role) => role switch
    {
        CellColorRole.Input => InputColor,
        CellColorRole.Link => LinkColor,
        _ => FormulaColor,
    };

    /// <summary>
    /// Cor da fonte a usar de fato: a escolhida manualmente, quando existe, senão a
    /// automática. É o que permite sobrescrever a convenção em casos pontuais.
    /// </summary>
    public static RgbColor ResolveFontColor(CellStyle style, CellColorRole role)
    {
        ArgumentNullException.ThrowIfNull(style);

        return style.FontColor ?? ColorOf(role);
    }
}
