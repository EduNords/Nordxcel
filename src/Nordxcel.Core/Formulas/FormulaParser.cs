using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Formulas;

/// <summary>
/// Segunda etapa do motor de fórmulas: monta a árvore sintática a partir dos tokens.
/// <para>
/// A precedência segue a do Excel, inclusive nos dois pontos em que ela diverge da
/// convenção matemática: o menos unário é mais forte que a potência
/// (<c>-2^2</c> vale 4, não -4) e a potência associa da esquerda para a direita
/// (<c>2^3^2</c> vale 64, não 512). Um modelo trazido do Excel precisa dar o mesmo
/// número aqui, então a compatibilidade vence a convenção.
/// </para>
/// </summary>
public sealed class FormulaParser(FormulaSyntax? syntax = null)
{
    private readonly FormulaTokenizer _tokenizer = new(syntax);
    private readonly FormulaSyntax _syntax = syntax ?? FormulaSyntax.Default;

    /// <summary>Interpreta uma fórmula com a convenção brasileira.</summary>
    public static FormulaNode ParseDefault(string formula) => new FormulaParser().Parse(formula);

    /// <summary>
    /// Interpreta a expressão, que deve vir <b>sem</b> o <c>=</c> inicial.
    /// </summary>
    /// <exception cref="FormulaSyntaxException">Quando a fórmula está malformada.</exception>
    public FormulaNode Parse(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);

        return Parse(_tokenizer.Tokenize(formula));
    }

    /// <inheritdoc cref="Parse(string)"/>
    public FormulaNode Parse(IReadOnlyList<FormulaToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        if (tokens.Count == 0)
        {
            throw new FormulaSyntaxException("A fórmula está vazia.", 0);
        }

        var cursor = new Cursor(tokens);

        if (cursor.Current.Kind is TokenKind.EndOfFormula)
        {
            throw new FormulaSyntaxException("A fórmula está vazia.", cursor.Current.Position);
        }

        FormulaNode node = ParseComparison(cursor);

        if (cursor.Current.Kind is not TokenKind.EndOfFormula)
        {
            throw new FormulaSyntaxException(
                $"Não era esperado '{cursor.Current.Lexeme}' neste ponto.",
                cursor.Current.Position);
        }

        return node;
    }

    // A cadeia abaixo vai da menor para a maior precedência.

    private FormulaNode ParseComparison(Cursor cursor)
    {
        cursor.EnterNesting();

        try
        {
            FormulaNode left = ParseConcat(cursor);

            while (TryReadBinaryOperator(cursor.Current.Kind, ComparisonOperators, out BinaryOperator op))
            {
                cursor.Advance();
                left = new BinaryNode(op, left, ParseConcat(cursor));
            }

            return left;
        }
        finally
        {
            cursor.ExitNesting();
        }
    }

    private FormulaNode ParseConcat(Cursor cursor)
    {
        FormulaNode left = ParseAdditive(cursor);

        while (cursor.Current.Kind is TokenKind.Ampersand)
        {
            cursor.Advance();
            left = new BinaryNode(BinaryOperator.Concat, left, ParseAdditive(cursor));
        }

        return left;
    }

    private FormulaNode ParseAdditive(Cursor cursor)
    {
        FormulaNode left = ParseMultiplicative(cursor);

        while (cursor.Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            BinaryOperator op = cursor.Advance().Kind is TokenKind.Plus
                ? BinaryOperator.Add
                : BinaryOperator.Subtract;

            left = new BinaryNode(op, left, ParseMultiplicative(cursor));
        }

        return left;
    }

    private FormulaNode ParseMultiplicative(Cursor cursor)
    {
        FormulaNode left = ParsePower(cursor);

        while (cursor.Current.Kind is TokenKind.Multiply or TokenKind.Divide)
        {
            BinaryOperator op = cursor.Advance().Kind is TokenKind.Multiply
                ? BinaryOperator.Multiply
                : BinaryOperator.Divide;

            left = new BinaryNode(op, left, ParsePower(cursor));
        }

        return left;
    }

    /// <summary>Potência, associativa à esquerda como no Excel.</summary>
    private FormulaNode ParsePower(Cursor cursor)
    {
        FormulaNode left = ParseUnary(cursor);

        while (cursor.Current.Kind is TokenKind.Power)
        {
            cursor.Advance();
            left = new BinaryNode(BinaryOperator.Power, left, ParseUnary(cursor));
        }

        return left;
    }

    /// <summary>Sinal unário, mais forte que a potência.</summary>
    private FormulaNode ParseUnary(Cursor cursor)
    {
        if (cursor.Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            UnaryOperator op = cursor.Advance().Kind is TokenKind.Plus
                ? UnaryOperator.Plus
                : UnaryOperator.Negate;

            cursor.EnterNesting();

            try
            {
                return new UnaryNode(op, ParseUnary(cursor));
            }
            finally
            {
                cursor.ExitNesting();
            }
        }

        return ParsePostfix(cursor);
    }

    /// <summary>Sufixo de porcentagem, o operador mais forte de todos.</summary>
    private FormulaNode ParsePostfix(Cursor cursor)
    {
        FormulaNode node = ParsePrimary(cursor);

        while (cursor.Current.Kind is TokenKind.Percent)
        {
            cursor.Advance();
            node = new UnaryNode(UnaryOperator.Percent, node);
        }

        return node;
    }

    private FormulaNode ParsePrimary(Cursor cursor)
    {
        FormulaToken token = cursor.Current;

        switch (token.Kind)
        {
            case TokenKind.Number:
                cursor.Advance();
                return new NumberNode(token.NumberValue);

            case TokenKind.Text:
                cursor.Advance();
                return new TextNode(token.Lexeme);

            case TokenKind.Error:
                cursor.Advance();
                return new ErrorNode(token.Error);

            case TokenKind.Reference:
                cursor.Advance();
                return ParseReferenceOrRange(cursor, token);

            case TokenKind.Identifier:
                cursor.Advance();
                return ParseIdentifier(cursor, token);

            case TokenKind.OpenParen:
                cursor.Advance();
                FormulaNode inner = ParseComparison(cursor);
                Expect(cursor, TokenKind.CloseParen, "Esperado ')' para fechar o parêntese.");
                return inner;

            case TokenKind.EndOfFormula:
                throw new FormulaSyntaxException("A fórmula terminou antes do esperado.", token.Position);

            default:
                throw new FormulaSyntaxException(
                    $"Não era esperado '{token.Lexeme}' neste ponto.",
                    token.Position);
        }
    }

    private static FormulaNode ParseReferenceOrRange(Cursor cursor, FormulaToken start)
    {
        if (cursor.Current.Kind is not TokenKind.Colon)
        {
            return new ReferenceNode(start.Reference);
        }

        cursor.Advance();

        if (cursor.Current.Kind is not TokenKind.Reference)
        {
            throw new FormulaSyntaxException(
                "Esperado uma referência de célula depois de ':'.",
                cursor.Current.Position);
        }

        FormulaToken end = cursor.Advance();
        CellReference endReference = end.Reference;

        if (endReference.Sheet is not null &&
            !string.Equals(endReference.Sheet, start.Reference.Sheet, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormulaSyntaxException(
                "Um intervalo não pode atravessar duas abas diferentes.",
                end.Position);
        }

        // A aba do início vale para o intervalo todo: Premissas!B2:B10.
        var normalizedEnd = new CellReference(
            endReference.Address,
            endReference.AbsoluteColumn,
            endReference.AbsoluteRow,
            start.Reference.Sheet);

        return new RangeNode(start.Reference, normalizedEnd);
    }

    private FormulaNode ParseIdentifier(Cursor cursor, FormulaToken token)
    {
        if (cursor.Current.Kind is TokenKind.OpenParen)
        {
            return ParseFunctionCall(cursor, token);
        }

        if (string.Equals(token.Lexeme, _syntax.TrueLiteral, StringComparison.OrdinalIgnoreCase))
        {
            return new LogicalNode(true);
        }

        if (string.Equals(token.Lexeme, _syntax.FalseLiteral, StringComparison.OrdinalIgnoreCase))
        {
            return new LogicalNode(false);
        }

        return new NameNode(token.Lexeme);
    }

    private FormulaNode ParseFunctionCall(Cursor cursor, FormulaToken name)
    {
        cursor.Advance(); // '('

        var arguments = new List<FormulaNode>();

        if (cursor.Current.Kind is not TokenKind.CloseParen)
        {
            while (true)
            {
                // Argumento omitido, como o do meio em SE(A1;;0).
                arguments.Add(cursor.Current.Kind is TokenKind.ArgumentSeparator or TokenKind.CloseParen
                    ? MissingArgumentNode.Instance
                    : ParseComparison(cursor));

                if (cursor.Current.Kind is not TokenKind.ArgumentSeparator)
                {
                    break;
                }

                cursor.Advance();
            }
        }

        Expect(cursor, TokenKind.CloseParen, $"Esperado ')' para fechar a chamada de {name.Lexeme}.");

        return new FunctionNode(name.Lexeme, arguments);
    }

    private static void Expect(Cursor cursor, TokenKind kind, string message)
    {
        if (cursor.Current.Kind != kind)
        {
            throw new FormulaSyntaxException(message, cursor.Current.Position);
        }

        cursor.Advance();
    }

    private static readonly (TokenKind Token, BinaryOperator Operator)[] ComparisonOperators =
    [
        (TokenKind.Equal, BinaryOperator.Equal),
        (TokenKind.NotEqual, BinaryOperator.NotEqual),
        (TokenKind.Less, BinaryOperator.Less),
        (TokenKind.LessOrEqual, BinaryOperator.LessOrEqual),
        (TokenKind.Greater, BinaryOperator.Greater),
        (TokenKind.GreaterOrEqual, BinaryOperator.GreaterOrEqual),
    ];

    private static bool TryReadBinaryOperator(
        TokenKind kind,
        (TokenKind Token, BinaryOperator Operator)[] table,
        out BinaryOperator op)
    {
        foreach ((TokenKind token, BinaryOperator candidate) in table)
        {
            if (token == kind)
            {
                op = candidate;
                return true;
            }
        }

        op = default;
        return false;
    }

    /// <summary>
    /// Limite de aninhamento, o mesmo do Excel. Existe para que uma fórmula
    /// absurdamente aninhada devolva um erro em vez de estourar a pilha — um
    /// <c>StackOverflowException</c> não é capturável e derrubaria o aplicativo.
    /// </summary>
    public const int MaxNestingDepth = 64;

    /// <summary>Posição de leitura sobre a lista de tokens.</summary>
    private sealed class Cursor(IReadOnlyList<FormulaToken> tokens)
    {
        private int _index;
        private int _depth;

        public FormulaToken Current => tokens[_index];

        public FormulaToken Advance() => tokens[_index++];

        public void EnterNesting()
        {
            if (++_depth > MaxNestingDepth)
            {
                throw new FormulaSyntaxException(
                    $"A fórmula passa do limite de {MaxNestingDepth} níveis de aninhamento.",
                    Current.Position);
            }
        }

        public void ExitNesting() => _depth--;
    }
}
