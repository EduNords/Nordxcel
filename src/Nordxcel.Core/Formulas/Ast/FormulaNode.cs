using System.Globalization;
using System.Text;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Formulas.Ast;

/// <summary>Operador de dois operandos.</summary>
public enum BinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Power,

    /// <summary>Concatenação de texto (<c>&amp;</c>).</summary>
    Concat,

    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
}

/// <summary>Operador de um operando.</summary>
public enum UnaryOperator
{
    /// <summary>Mais unário. Não muda o valor, mas força coerção numérica.</summary>
    Plus,

    Negate,

    /// <summary>Sufixo de porcentagem: divide por cem.</summary>
    Percent,
}

/// <summary>
/// Nó da árvore sintática de uma fórmula. A árvore é imutável: uma vez montada
/// pelo parser, pode ser guardada em cache e avaliada quantas vezes for preciso —
/// é isso que deixa o Data Table barato, já que ele reavalia o mesmo modelo
/// dezenas de vezes trocando só os inputs.
/// </summary>
public abstract record FormulaNode;

/// <summary>Literal numérico.</summary>
public sealed record NumberNode(double Value) : FormulaNode
{
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Literal de texto, já sem as aspas.</summary>
public sealed record TextNode(string Value) : FormulaNode
{
    public override string ToString() => $"\"{Value}\"";
}

/// <summary>Literal lógico (<c>VERDADEIRO</c> ou <c>FALSO</c>).</summary>
public sealed record LogicalNode(bool Value) : FormulaNode
{
    public override string ToString() => Value ? "VERDADEIRO" : "FALSO";
}

/// <summary>Literal de erro digitado na fórmula, como em <c>SEERRO(A1;#N/D)</c>.</summary>
public sealed record ErrorNode(CellErrorType Error) : FormulaNode
{
    public override string ToString() => Error.ToDisplayText();
}

/// <summary>Referência a uma única célula.</summary>
public sealed record ReferenceNode(CellReference Reference) : FormulaNode
{
    public override string ToString() => Reference.ToString();
}

/// <summary>
/// Intervalo retangular (<c>B2:B10</c>). Quando o intervalo é de outra aba, a aba
/// fica em <see cref="Start"/> e vale para os dois extremos.
/// </summary>
public sealed record RangeNode(CellReference Start, CellReference End) : FormulaNode
{
    /// <summary>Aba do intervalo, ou <c>null</c> quando é da própria aba da fórmula.</summary>
    public string? Sheet => Start.Sheet;

    /// <summary>Área coberta pelo intervalo, já normalizada.</summary>
    public CellRange ToRange() => new(Start.Address, End.Address);

    public override string ToString()
    {
        var end = new CellReference(End.Address, End.AbsoluteColumn, End.AbsoluteRow);
        return $"{Start}:{end}";
    }
}

/// <summary>
/// Nome que não é célula nem função conhecida — intervalo nomeado ou erro de
/// digitação. O avaliador decide o que fazer; hoje, devolve <c>#NOME?</c>.
/// </summary>
public sealed record NameNode(string Name) : FormulaNode
{
    public override string ToString() => $"nome:{Name}";
}

/// <summary>Argumento omitido, como o do meio em <c>SE(A1;;0)</c>.</summary>
public sealed record MissingArgumentNode : FormulaNode
{
    public static readonly MissingArgumentNode Instance = new();

    public override string ToString() => "<vazio>";
}

/// <summary>Operação de um operando.</summary>
public sealed record UnaryNode(UnaryOperator Operator, FormulaNode Operand) : FormulaNode
{
    public override string ToString()
    {
        string symbol = Operator switch
        {
            UnaryOperator.Plus => "+",
            UnaryOperator.Negate => "-",
            UnaryOperator.Percent => "%",
            _ => "?",
        };

        return $"({symbol} {Operand})";
    }
}

/// <summary>Operação de dois operandos.</summary>
public sealed record BinaryNode(BinaryOperator Operator, FormulaNode Left, FormulaNode Right) : FormulaNode
{
    public override string ToString() => $"({Symbol(Operator)} {Left} {Right})";

    internal static string Symbol(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Power => "^",
        BinaryOperator.Concat => "&",
        BinaryOperator.Equal => "=",
        BinaryOperator.NotEqual => "<>",
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        _ => "?",
    };
}

/// <summary>
/// Chamada de função. O nome vem sempre em maiúsculas, como o Excel faz ao
/// normalizar o que o usuário digitou.
/// </summary>
public sealed record FunctionNode : FormulaNode
{
    public FunctionNode(string name, IReadOnlyList<FormulaNode> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);

        Name = name.ToUpperInvariant();
        Arguments = arguments;
    }

    public string Name { get; }

    public IReadOnlyList<FormulaNode> Arguments { get; }

    /// <summary>
    /// Igualdade estrutural. O <c>record</c> compararia a lista de argumentos por
    /// referência, o que quebraria qualquer cache de fórmulas já compiladas.
    /// </summary>
    public bool Equals(FunctionNode? other) =>
        other is not null &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        Arguments.SequenceEqual(other.Arguments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);

        foreach (FormulaNode argument in Arguments)
        {
            hash.Add(argument);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        var builder = new StringBuilder(Name).Append('(');

        for (int i = 0; i < Arguments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Arguments[i]);
        }

        return builder.Append(')').ToString();
    }
}
