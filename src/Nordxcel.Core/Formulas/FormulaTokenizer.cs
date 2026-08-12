using System.Globalization;
using System.Text;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Formulas;

/// <summary>
/// Primeira etapa do motor de fórmulas: quebra o texto em tokens.
/// Não valida a estrutura da expressão — <c>SOMA(;;)</c> passa por aqui sem reclamar
/// e é o parser que vai recusar. O que o tokenizer garante é que cada token
/// isoladamente é válido.
/// <para>
/// A entrada é a expressão <b>sem</b> o <c>=</c> inicial, do jeito que
/// <see cref="Cell.Formula"/> guarda.
/// </para>
/// </summary>
public sealed class FormulaTokenizer(FormulaSyntax? syntax = null)
{
    private readonly FormulaSyntax _syntax = syntax ?? FormulaSyntax.Default;

    /// <summary>Tokeniza usando a convenção brasileira.</summary>
    public static IReadOnlyList<FormulaToken> TokenizeDefault(string formula) =>
        new FormulaTokenizer().Tokenize(formula);

    /// <summary>
    /// Quebra a fórmula em tokens, sempre terminando com <see cref="TokenKind.EndOfFormula"/>.
    /// </summary>
    /// <exception cref="FormulaSyntaxException">Quando encontra um token que não sabe ler.</exception>
    public IReadOnlyList<FormulaToken> Tokenize(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);

        var tokens = new List<FormulaToken>();
        int position = 0;

        while (position < formula.Length)
        {
            char current = formula[position];

            if (char.IsWhiteSpace(current))
            {
                position++;
                continue;
            }

            if (char.IsAsciiDigit(current) || current == _syntax.DecimalSeparator)
            {
                tokens.Add(ReadNumber(formula, ref position));
            }
            else if (current == '"')
            {
                tokens.Add(ReadText(formula, ref position));
            }
            else if (current == '#')
            {
                tokens.Add(ReadError(formula, ref position));
            }
            else if (current == '\'' || current == '$' || IsNameChar(current))
            {
                tokens.Add(ReadNameOrReference(formula, ref position));
            }
            else
            {
                tokens.Add(ReadOperator(formula, ref position));
            }
        }

        tokens.Add(new FormulaToken(TokenKind.EndOfFormula, string.Empty, formula.Length));

        return tokens;
    }

    private FormulaToken ReadNumber(string formula, ref int position)
    {
        int start = position;
        bool seenDecimalSeparator = false;

        while (position < formula.Length)
        {
            char current = formula[position];

            if (char.IsAsciiDigit(current))
            {
                position++;
            }
            else if (current == _syntax.DecimalSeparator && !seenDecimalSeparator)
            {
                seenDecimalSeparator = true;
                position++;
            }
            else
            {
                break;
            }
        }

        ReadExponent(formula, ref position);

        string lexeme = formula[start..position];

        if (!double.TryParse(lexeme, NumberStyles.Float, _syntax.NumberFormat, out double value))
        {
            throw new FormulaSyntaxException($"'{lexeme}' não é um número válido.", start);
        }

        return new FormulaToken(TokenKind.Number, lexeme, start, NumberValue: value);
    }

    /// <summary>
    /// Consome o expoente de uma notação científica (<c>1E+10</c>). Se o que vem
    /// depois do <c>E</c> não for um expoente, deixa a posição intacta para que o
    /// <c>E</c> seja lido como início de outro token.
    /// </summary>
    private static void ReadExponent(string formula, ref int position)
    {
        if (position >= formula.Length || (formula[position] != 'E' && formula[position] != 'e'))
        {
            return;
        }

        int lookahead = position + 1;

        if (lookahead < formula.Length && (formula[lookahead] == '+' || formula[lookahead] == '-'))
        {
            lookahead++;
        }

        if (lookahead >= formula.Length || !char.IsAsciiDigit(formula[lookahead]))
        {
            return;
        }

        while (lookahead < formula.Length && char.IsAsciiDigit(formula[lookahead]))
        {
            lookahead++;
        }

        position = lookahead;
    }

    private static FormulaToken ReadText(string formula, ref int position)
    {
        int start = position;
        position++;

        var builder = new StringBuilder();

        while (true)
        {
            if (position >= formula.Length)
            {
                throw new FormulaSyntaxException("Texto sem aspas de fechamento.", start);
            }

            char current = formula[position];

            if (current != '"')
            {
                builder.Append(current);
                position++;
                continue;
            }

            // Aspas duplicadas dentro do texto representam uma aspa literal.
            if (position + 1 < formula.Length && formula[position + 1] == '"')
            {
                builder.Append('"');
                position += 2;
                continue;
            }

            position++;
            break;
        }

        return new FormulaToken(TokenKind.Text, builder.ToString(), start);
    }

    private static FormulaToken ReadError(string formula, ref int position)
    {
        int start = position;

        if (!CellErrors.TryMatchPrefix(formula.AsSpan(position), out CellErrorType error, out int length))
        {
            throw new FormulaSyntaxException("Literal de erro desconhecido.", start);
        }

        position += length;

        return new FormulaToken(TokenKind.Error, error.ToDisplayText(), start, Error: error);
    }

    private static FormulaToken ReadNameOrReference(string formula, ref int position)
    {
        int start = position;
        string? sheet = null;

        if (formula[position] == '\'')
        {
            sheet = ReadQuotedSheetName(formula, ref position);

            if (position >= formula.Length || formula[position] != '!')
            {
                throw new FormulaSyntaxException("Esperado '!' depois do nome da aba entre aspas.", position);
            }

            position++;
        }

        int wordStart = position;
        string word = ReadWord(formula, ref position);

        // Nome de aba sem aspas, como em Premissas!B5.
        if (sheet is null && word.Length > 0 && position < formula.Length && formula[position] == '!')
        {
            sheet = word;
            position++;
            wordStart = position;
            word = ReadWord(formula, ref position);
        }

        if (word.Length == 0)
        {
            throw new FormulaSyntaxException("Referência de célula incompleta.", wordStart);
        }

        // Um nome seguido de parêntese é chamada de função. Sem essa checagem,
        // nomes como LOG10 seriam confundidos com a célula LOG10.
        if (sheet is null && NextNonSpaceIs(formula, position, '('))
        {
            return new FormulaToken(TokenKind.Identifier, word, start);
        }

        if (CellReference.TryParse(word, out CellReference reference))
        {
            var qualified = new CellReference(
                reference.Address,
                reference.AbsoluteColumn,
                reference.AbsoluteRow,
                sheet);

            return new FormulaToken(TokenKind.Reference, qualified.ToString(), start, Reference: qualified);
        }

        if (sheet is not null)
        {
            throw new FormulaSyntaxException($"'{word}' não é uma referência de célula válida.", wordStart);
        }

        return new FormulaToken(TokenKind.Identifier, word, start);
    }

    private static string ReadQuotedSheetName(string formula, ref int position)
    {
        int start = position;
        position++;

        var builder = new StringBuilder();

        while (true)
        {
            if (position >= formula.Length)
            {
                throw new FormulaSyntaxException("Nome de aba sem aspas de fechamento.", start);
            }

            char current = formula[position];

            if (current != '\'')
            {
                builder.Append(current);
                position++;
                continue;
            }

            if (position + 1 < formula.Length && formula[position + 1] == '\'')
            {
                builder.Append('\'');
                position += 2;
                continue;
            }

            position++;
            break;
        }

        if (builder.Length == 0)
        {
            throw new FormulaSyntaxException("Nome de aba vazio.", start);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Lê um nome de função, nome definido ou parte de célula de uma referência.
    /// O ponto só entra quando vem seguido de letra, para aceitar <c>CONT.VALORES</c>
    /// sem engolir o separador decimal em convenções que usam ponto.
    /// </summary>
    private static string ReadWord(string formula, ref int position)
    {
        int start = position;

        while (position < formula.Length)
        {
            char current = formula[position];

            if (IsNameChar(current) || current == '$')
            {
                position++;
                continue;
            }

            if (current == '.' && position + 1 < formula.Length && char.IsLetter(formula[position + 1]))
            {
                position++;
                continue;
            }

            break;
        }

        return formula[start..position];
    }

    private FormulaToken ReadOperator(string formula, ref int position)
    {
        int start = position;
        char current = formula[position];

        if (current == _syntax.ArgumentSeparator)
        {
            position++;
            return new FormulaToken(TokenKind.ArgumentSeparator, current.ToString(), start);
        }

        switch (current)
        {
            case '+':
            case '-':
            case '*':
            case '/':
            case '^':
            case '%':
            case '&':
            case '(':
            case ')':
            case ':':
            case '=':
                position++;
                return new FormulaToken(SingleCharKind(current), current.ToString(), start);

            case '<':
                if (Peek(formula, position + 1) == '=')
                {
                    position += 2;
                    return new FormulaToken(TokenKind.LessOrEqual, "<=", start);
                }

                if (Peek(formula, position + 1) == '>')
                {
                    position += 2;
                    return new FormulaToken(TokenKind.NotEqual, "<>", start);
                }

                position++;
                return new FormulaToken(TokenKind.Less, "<", start);

            case '>':
                if (Peek(formula, position + 1) == '=')
                {
                    position += 2;
                    return new FormulaToken(TokenKind.GreaterOrEqual, ">=", start);
                }

                position++;
                return new FormulaToken(TokenKind.Greater, ">", start);

            default:
                throw new FormulaSyntaxException($"Caractere inesperado '{current}'.", start);
        }
    }

    private static TokenKind SingleCharKind(char c) => c switch
    {
        '+' => TokenKind.Plus,
        '-' => TokenKind.Minus,
        '*' => TokenKind.Multiply,
        '/' => TokenKind.Divide,
        '^' => TokenKind.Power,
        '%' => TokenKind.Percent,
        '&' => TokenKind.Ampersand,
        '(' => TokenKind.OpenParen,
        ')' => TokenKind.CloseParen,
        ':' => TokenKind.Colon,
        '=' => TokenKind.Equal,
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "Operador não mapeado."),
    };

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static char Peek(string formula, int position) =>
        position < formula.Length ? formula[position] : '\0';

    private static bool NextNonSpaceIs(string formula, int position, char expected)
    {
        while (position < formula.Length && char.IsWhiteSpace(formula[position]))
        {
            position++;
        }

        return position < formula.Length && formula[position] == expected;
    }
}
