using System.Linq;
using Nordxcel.Core.Formulas.Ast;

namespace Nordxcel.Core.Formulas;

/// <summary>
/// Troca o nome de cada função de uma árvore já interpretada pela convenção
/// padrão do Nordxcel (português) — o passo que fecha a importação de um
/// <c>.xlsx</c>: a fórmula chega em inglês (<c>SUM</c>), é interpretada com
/// <see cref="FormulaSyntax.EnUs"/>, e esta classe troca cada nó de função
/// conhecido para o nome em português antes de virar texto de novo com
/// <see cref="FormulaWriter"/>.
/// <para>
/// Função sem tradução (<c>OFFSET</c>, <c>TRANSPOSE</c>...) fica com o nome
/// como veio — de propósito: sem correspondência no
/// <see cref="Evaluation.Functions.FunctionRegistry"/>, vira <c>#NOME?</c>
/// sozinha ao avaliar, o mesmo aviso honesto que qualquer fórmula inválida já
/// dá, em vez de fingir suporte ou travar a importação inteira.
/// </para>
/// </summary>
public static class FormulaSyntaxTranslator
{
    /// <summary>
    /// Reescreve a árvore, trocando nome de função pela tradução em
    /// <paramref name="syntax"/> quando existe. Devolve, junto, o conjunto dos
    /// nomes que apareceram sem tradução — o que popula o relatório de
    /// importação.
    /// </summary>
    public static FormulaNode ToDefault(FormulaNode node, FormulaSyntax syntax, ISet<string> unknownFunctions)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(unknownFunctions);

        if (syntax.FunctionNamesReverse is null)
        {
            return node;
        }

        return node switch
        {
            UnaryNode unary => unary with { Operand = ToDefault(unary.Operand, syntax, unknownFunctions) },

            BinaryNode binary => binary with
            {
                Left = ToDefault(binary.Left, syntax, unknownFunctions),
                Right = ToDefault(binary.Right, syntax, unknownFunctions),
            },

            FunctionNode function => new FunctionNode(
                TranslateName(function.Name, syntax, unknownFunctions),
                function.Arguments.Select(argument => ToDefault(argument, syntax, unknownFunctions)).ToList()),

            // Literais, referência, intervalo e nome não têm função para traduzir.
            _ => node,
        };
    }

    private static string TranslateName(string name, FormulaSyntax syntax, ISet<string> unknownFunctions)
    {
        if (syntax.FunctionNamesReverse!.TryGetValue(name, out string? translated))
        {
            return translated;
        }

        unknownFunctions.Add(name);
        return name;
    }
}
