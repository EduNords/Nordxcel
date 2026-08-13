using System.Text;
using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Formulas;

/// <summary>
/// Converte a árvore sintática de volta para texto de fórmula, na sintaxe do
/// Excel. É o inverso do <see cref="FormulaParser"/> — necessário para colar uma
/// fórmula copiada com as referências já traduzidas (<see cref="FormulaTranslator"/>)
/// de volta como texto que a célula de destino vai guardar.
/// <para>
/// Os <c>ToString()</c> dos nós em <see cref="Ast"/> não servem para isso: eles
/// existem só para depuração, em notação prefixa (<c>(+ 1 2)</c>), e
/// <see cref="TextNode"/> nem escapa aspas embutidas. Este escritor produz texto
/// que o próprio <see cref="FormulaParser"/> consegue reler de volta.
/// </para>
/// </summary>
public static class FormulaWriter
{
    // Precedência de cada forma de nó quando aparece como filho de outro nó —
    // quanto maior, mais forte, e é isso que decide se precisa de parênteses.
    private const int PrecedenceComparison = 1;
    private const int PrecedenceConcat = 2;
    private const int PrecedenceAdditive = 3;
    private const int PrecedenceMultiplicative = 4;
    private const int PrecedencePower = 5;
    private const int PrecedenceUnaryPrefix = 6;
    private const int PrecedencePercent = 7;
    private const int PrecedencePrimary = 8;

    /// <summary>Escreve com a convenção brasileira (vírgula decimal, ponto e vírgula entre argumentos).</summary>
    public static string Write(FormulaNode node) => Write(node, FormulaSyntax.Default);

    public static string Write(FormulaNode node, FormulaSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(syntax);

        var builder = new StringBuilder();
        WriteNode(builder, node, syntax);

        return builder.ToString();
    }

    private static void WriteNode(StringBuilder builder, FormulaNode node, FormulaSyntax syntax)
    {
        switch (node)
        {
            case NumberNode number:
                // Não depende de Evaluation.ValueCoercion de propósito: o escritor de
                // fórmulas é uma preocupação só de sintaxe, a camada de baixo.
                builder.Append(number.Value.ToString("G15", syntax.NumberFormat));
                break;

            case TextNode text:
                WriteText(builder, text.Value);
                break;

            case LogicalNode logical:
                builder.Append(logical.Value ? "VERDADEIRO" : "FALSO");
                break;

            case ErrorNode error:
                builder.Append(error.Error.ToDisplayText());
                break;

            case ReferenceNode reference:
                builder.Append(reference.Reference.ToString());
                break;

            case RangeNode range:
                builder.Append(range.ToString());
                break;

            case NameNode name:
                builder.Append(name.Name);
                break;

            case MissingArgumentNode:
                // Nada: é o que produz o "buraco" entre dois separadores, como em SE(A1;;0).
                break;

            case UnaryNode unary:
                WriteUnary(builder, unary, syntax);
                break;

            case BinaryNode binary:
                WriteBinary(builder, binary, syntax);
                break;

            case FunctionNode function:
                WriteFunction(builder, function, syntax);
                break;

            default:
                throw new NotSupportedException($"Nó de fórmula não suportado: {node.GetType().Name}.");
        }
    }

    private static void WriteText(StringBuilder builder, string value)
    {
        // Inverso exato do tokenizer: aspas embutidas viram um par de aspas.
        builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
    }

    private static void WriteUnary(StringBuilder builder, UnaryNode node, FormulaSyntax syntax)
    {
        if (node.Operator is UnaryOperator.Percent)
        {
            WriteChild(builder, node.Operand, PrecedencePercent, syntax);
            builder.Append('%');
            return;
        }

        builder.Append(node.Operator is UnaryOperator.Negate ? '-' : '+');
        WriteChild(builder, node.Operand, PrecedenceUnaryPrefix, syntax);
    }

    private static void WriteBinary(StringBuilder builder, BinaryNode node, FormulaSyntax syntax)
    {
        int precedence = Precedence(node.Operator);

        // O lado direito exige precedência estritamente maior que a do próprio
        // operador — é isso que preserva a associação à esquerda ao reler o texto:
        // sem essa exigência, "10-3-2" e "10-(3-2)" ficariam indistinguíveis.
        WriteChild(builder, node.Left, precedence, syntax);
        builder.Append(BinaryNode.Symbol(node.Operator));
        WriteChild(builder, node.Right, precedence + 1, syntax);
    }

    private static void WriteFunction(StringBuilder builder, FunctionNode node, FormulaSyntax syntax)
    {
        builder.Append(node.Name).Append('(');

        for (int i = 0; i < node.Arguments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(syntax.ArgumentSeparator);
            }

            WriteNode(builder, node.Arguments[i], syntax);
        }

        builder.Append(')');
    }

    /// <summary>Escreve um nó filho, envolvendo em parênteses se a precedência dele for baixa demais para o contexto.</summary>
    private static void WriteChild(StringBuilder builder, FormulaNode child, int minimumPrecedence, FormulaSyntax syntax)
    {
        if (PrecedenceOf(child) < minimumPrecedence)
        {
            builder.Append('(');
            WriteNode(builder, child, syntax);
            builder.Append(')');
        }
        else
        {
            WriteNode(builder, child, syntax);
        }
    }

    private static int PrecedenceOf(FormulaNode node) => node switch
    {
        BinaryNode binary => Precedence(binary.Operator),
        UnaryNode { Operator: UnaryOperator.Percent } => PrecedencePercent,
        UnaryNode => PrecedenceUnaryPrefix,
        _ => PrecedencePrimary,
    };

    private static int Precedence(BinaryOperator op) => op switch
    {
        BinaryOperator.Equal or BinaryOperator.NotEqual or
        BinaryOperator.Less or BinaryOperator.LessOrEqual or
        BinaryOperator.Greater or BinaryOperator.GreaterOrEqual => PrecedenceComparison,

        BinaryOperator.Concat => PrecedenceConcat,
        BinaryOperator.Add or BinaryOperator.Subtract => PrecedenceAdditive,
        BinaryOperator.Multiply or BinaryOperator.Divide => PrecedenceMultiplicative,
        BinaryOperator.Power => PrecedencePower,

        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Operador binário não mapeado."),
    };
}
