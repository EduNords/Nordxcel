using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Calculation;

/// <summary>
/// Descobre de quais células uma fórmula depende, percorrendo a árvore sintática.
/// Referência sem aba assume a aba de onde a fórmula está.
/// </summary>
public static class FormulaDependencyScanner
{
    /// <summary>
    /// Separa as dependências em células isoladas e intervalos. Os intervalos são
    /// mantidos inteiros de propósito: expandir <c>SOMA(B2:B1000)</c> em mil arestas
    /// encheria o grafo à toa.
    /// </summary>
    public static void Collect(
        FormulaNode node,
        string currentSheet,
        ICollection<CellLocation> cells,
        ICollection<RangeLocation> ranges)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSheet);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(ranges);

        switch (node)
        {
            case ReferenceNode reference:
                cells.Add(new CellLocation(
                    reference.Reference.Sheet ?? currentSheet,
                    reference.Reference.Address));
                break;

            case RangeNode range:
                ranges.Add(new RangeLocation(range.Sheet ?? currentSheet, range.ToRange()));
                break;

            case UnaryNode unary:
                Collect(unary.Operand, currentSheet, cells, ranges);
                break;

            case BinaryNode binary:
                Collect(binary.Left, currentSheet, cells, ranges);
                Collect(binary.Right, currentSheet, cells, ranges);
                break;

            case FunctionNode function:
                foreach (FormulaNode argument in function.Arguments)
                {
                    Collect(argument, currentSheet, cells, ranges);
                }

                break;

            // Literais, nomes e argumento omitido não dependem de nada.
            default:
                break;
        }
    }
}
