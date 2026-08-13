using System.Linq;
using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Formulas;

/// <summary>
/// Desloca toda referência relativa de uma árvore por um número de linhas e
/// colunas — exatamente o que acontece ao copiar e colar uma fórmula em outro
/// lugar. Referências absolutas (<c>$A$1</c>) não se movem.
/// <para>
/// Quando a translação faz uma referência ou intervalo sair da planilha, só
/// aquele pedaço da fórmula vira <c>#REF!</c> — o resto continua válido — igual
/// ao que o Excel mostra quando uma referência fica órfã.
/// </para>
/// </summary>
public static class FormulaTranslator
{
    public static FormulaNode Translate(FormulaNode node, int rowDelta, int columnDelta)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Sem deslocamento, a árvore não muda — devolver a mesma instância evita
        // reconstruir (e depois reescrever) uma fórmula que colou no lugar de onde saiu.
        if (rowDelta == 0 && columnDelta == 0)
        {
            return node;
        }

        return node switch
        {
            ReferenceNode reference => TranslateReference(reference, rowDelta, columnDelta),
            RangeNode range => TranslateRange(range, rowDelta, columnDelta),

            UnaryNode unary => unary with { Operand = Translate(unary.Operand, rowDelta, columnDelta) },

            BinaryNode binary => binary with
            {
                Left = Translate(binary.Left, rowDelta, columnDelta),
                Right = Translate(binary.Right, rowDelta, columnDelta),
            },

            FunctionNode function => new FunctionNode(
                function.Name,
                function.Arguments.Select(argument => Translate(argument, rowDelta, columnDelta)).ToList()),

            // Literais, nome e argumento omitido não têm referência para deslocar.
            _ => node,
        };
    }

    private static FormulaNode TranslateReference(ReferenceNode node, int rowDelta, int columnDelta) =>
        node.Reference.TryTranslate(rowDelta, columnDelta, out CellReference translated)
            ? new ReferenceNode(translated)
            : new ErrorNode(CellErrorType.Reference);

    private static FormulaNode TranslateRange(RangeNode node, int rowDelta, int columnDelta)
    {
        // Um intervalo não pode ficar "pela metade": se qualquer extremo sair da
        // planilha, o intervalo inteiro vira #REF!, como no Excel.
        if (!node.Start.TryTranslate(rowDelta, columnDelta, out CellReference start) ||
            !node.End.TryTranslate(rowDelta, columnDelta, out CellReference end))
        {
            return new ErrorNode(CellErrorType.Reference);
        }

        return new RangeNode(start, end);
    }
}
