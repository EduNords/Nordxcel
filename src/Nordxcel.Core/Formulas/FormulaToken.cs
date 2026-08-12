using Nordxcel.Core.Model;

namespace Nordxcel.Core.Formulas;

/// <summary>Categoria de um token de fórmula.</summary>
public enum TokenKind
{
    /// <summary>Literal numérico, já convertido em <see cref="FormulaToken.NumberValue"/>.</summary>
    Number,

    /// <summary>Literal de texto, já com as aspas removidas e os escapes resolvidos.</summary>
    Text,

    /// <summary>Referência de célula, em <see cref="FormulaToken.Reference"/>.</summary>
    Reference,

    /// <summary>Nome de função ou nome definido (<c>SOMA</c>, <c>VERDADEIRO</c>).</summary>
    Identifier,

    /// <summary>Literal de erro digitado na fórmula (<c>#N/D</c>).</summary>
    Error,

    Plus,
    Minus,
    Multiply,
    Divide,
    Power,

    /// <summary>Sufixo de porcentagem, como em <c>12%</c>.</summary>
    Percent,

    /// <summary>Concatenação de texto (<c>&amp;</c>).</summary>
    Ampersand,

    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,

    OpenParen,
    CloseParen,

    /// <summary>Separador de argumentos, <c>;</c> na convenção brasileira.</summary>
    ArgumentSeparator,

    /// <summary>Dois-pontos que forma um intervalo (<c>B2:B10</c>).</summary>
    Colon,

    /// <summary>Marca o fim da fórmula. Sempre é o último token da lista.</summary>
    EndOfFormula,
}

/// <summary>
/// Unidade léxica de uma fórmula, com a posição em que começa no texto original
/// para que a interface possa apontar o erro no ponto certo da barra de fórmulas.
/// </summary>
public readonly record struct FormulaToken(
    TokenKind Kind,
    string Lexeme,
    int Position,
    double NumberValue = 0d,
    CellReference Reference = default,
    CellErrorType Error = CellErrorType.None)
{
    public override string ToString() => Kind switch
    {
        TokenKind.EndOfFormula => "<fim>",
        TokenKind.Text => $"Text(\"{Lexeme}\")",
        _ => $"{Kind}({Lexeme})",
    };
}
