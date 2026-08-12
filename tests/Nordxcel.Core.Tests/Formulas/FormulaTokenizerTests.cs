using Nordxcel.Core.Formulas;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Formulas;

public class FormulaTokenizerTests
{
    private static IReadOnlyList<FormulaToken> Tokenize(string formula) =>
        FormulaTokenizer.TokenizeDefault(formula);

    /// <summary>Tokens sem o <see cref="TokenKind.EndOfFormula"/>, que toda fórmula tem.</summary>
    private static FormulaToken[] Body(string formula) => Tokenize(formula).ToArray()[..^1];

    private static TokenKind[] Kinds(string formula) => Body(formula).Select(t => t.Kind).ToArray();

    [Fact]
    public void Tokenize_SempreTerminaComFimDeFormula()
    {
        IReadOnlyList<FormulaToken> tokens = Tokenize("A1");

        Assert.Equal(TokenKind.EndOfFormula, tokens[^1].Kind);
        Assert.Equal(2, tokens[^1].Position);
    }

    [Fact]
    public void Tokenize_FormulaVazia_SoTemOFim() =>
        Assert.Equal(TokenKind.EndOfFormula, Assert.Single(Tokenize("")).Kind);

    // ---------------------------------------------------------------- números

    [Theory]
    [InlineData("0", 0d)]
    [InlineData("42", 42d)]
    [InlineData("1,5", 1.5d)]
    [InlineData(",5", 0.5d)]
    [InlineData("1E5", 100_000d)]
    [InlineData("1E+10", 1e10)]
    [InlineData("2,5e-3", 0.0025d)]
    public void Tokenize_LeNumerosComVirgulaDecimal(string formula, double expected)
    {
        FormulaToken token = Assert.Single(Body(formula));

        Assert.Equal(TokenKind.Number, token.Kind);
        Assert.Equal(expected, token.NumberValue);
    }

    [Fact]
    public void Tokenize_PontoNaoEhSeparadorDecimalNaConvencaoBrasileira()
    {
        // "1.5" não é número: o 1 é lido sozinho e o ponto não abre nenhum token.
        FormulaSyntaxException exception = Assert.Throws<FormulaSyntaxException>(() => Tokenize("1.5"));

        Assert.Equal(1, exception.Position);
    }

    [Fact]
    public void Tokenize_ELetraSemExpoenteNaoEhConsumidoPeloNumero()
    {
        // Em "2E" o E não forma expoente, então sobra como nome.
        Assert.Equal([TokenKind.Number, TokenKind.Identifier], Kinds("2E"));
    }

    [Fact]
    public void Tokenize_PorcentagemEhSufixoSeparado()
    {
        FormulaToken[] tokens = Body("12,5%");

        Assert.Equal([TokenKind.Number, TokenKind.Percent], tokens.Select(t => t.Kind));
        Assert.Equal(12.5d, tokens[0].NumberValue);
    }

    // ----------------------------------------------------------------- textos

    [Fact]
    public void Tokenize_LeTextoRemovendoAsAspas()
    {
        FormulaToken token = Assert.Single(Body("\"Receita líquida\""));

        Assert.Equal(TokenKind.Text, token.Kind);
        Assert.Equal("Receita líquida", token.Lexeme);
    }

    [Fact]
    public void Tokenize_AspasDuplicadasViramUmaAspaLiteral() =>
        Assert.Equal(@"diz ""oi""", Assert.Single(Body("\"diz \"\"oi\"\"\"")).Lexeme);

    [Fact]
    public void Tokenize_TextoVazioEhValido() =>
        Assert.Equal(string.Empty, Assert.Single(Body("\"\"")).Lexeme);

    [Fact]
    public void Tokenize_TextoSemFechamento_Lanca()
    {
        FormulaSyntaxException exception = Assert.Throws<FormulaSyntaxException>(() => Tokenize("1+\"abc"));

        Assert.Equal(2, exception.Position);
    }

    // ------------------------------------------------------------ referências

    [Theory]
    [InlineData("A1")]
    [InlineData("$A$1")]
    [InlineData("A$1")]
    [InlineData("$A1")]
    public void Tokenize_LeReferenciasComMarcacaoDeAbsoluto(string formula)
    {
        FormulaToken token = Assert.Single(Body(formula));

        Assert.Equal(TokenKind.Reference, token.Kind);
        Assert.Equal(CellAddress.Origin, token.Reference.Address);
        Assert.Equal(formula.ToUpperInvariant(), token.Lexeme);
    }

    [Fact]
    public void Tokenize_LeReferenciaEntreAbas()
    {
        FormulaToken token = Assert.Single(Body("Premissas!B5"));

        Assert.Equal(TokenKind.Reference, token.Kind);
        Assert.Equal("Premissas", token.Reference.Sheet);
        Assert.Equal(CellAddress.Parse("B5"), token.Reference.Address);
    }

    [Fact]
    public void Tokenize_LeAbaComEspacoEntreAspas()
    {
        FormulaToken token = Assert.Single(Body("'Fluxo de Caixa'!$B$5"));

        Assert.Equal("Fluxo de Caixa", token.Reference.Sheet);
        Assert.True(token.Reference.AbsoluteColumn);
        Assert.Equal("'Fluxo de Caixa'!$B$5", token.Lexeme);
    }

    [Fact]
    public void Tokenize_IntervaloVemComoReferenciaDoisPontosReferencia() =>
        Assert.Equal([TokenKind.Reference, TokenKind.Colon, TokenKind.Reference], Kinds("B2:B10"));

    [Fact]
    public void Tokenize_AbaSemFecharAspas_Lanca() =>
        Assert.Throws<FormulaSyntaxException>(() => Tokenize("'Premissas!B5"));

    [Fact]
    public void Tokenize_AbaSeguidaDeCelulaInvalida_Lanca()
    {
        FormulaSyntaxException exception = Assert.Throws<FormulaSyntaxException>(() => Tokenize("Premissas!XPTO"));

        Assert.Equal(10, exception.Position);
    }

    // --------------------------------------------------- nomes contra células

    [Fact]
    public void Tokenize_NomeSeguidoDeParenteseEhFuncao()
    {
        // LOG10 também é um endereço de célula válido; o parêntese é o que decide.
        Assert.Equal(TokenKind.Identifier, Body("LOG10(2)")[0].Kind);
        Assert.Equal(TokenKind.Reference, Assert.Single(Body("LOG10")).Kind);
    }

    [Fact]
    public void Tokenize_EspacoAntesDoParenteseNaoAtrapalha() =>
        Assert.Equal(TokenKind.Identifier, Body("SOMA (A1)")[0].Kind);

    [Theory]
    [InlineData("MÉDIA")]
    [InlineData("MÍNIMO")]
    [InlineData("ÍNDICE")]
    [InlineData("CONT.VALORES")]
    [InlineData("SEERRO")]
    public void Tokenize_AceitaNomesDeFuncaoComAcentoEPonto(string name)
    {
        FormulaToken token = Body($"{name}(A1)")[0];

        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal(name, token.Lexeme);
    }

    [Fact]
    public void Tokenize_NomeSemParenteseQueNaoEhCelulaViraIdentificador()
    {
        Assert.Equal(TokenKind.Identifier, Assert.Single(Body("VERDADEIRO")).Kind);
        Assert.Equal(TokenKind.Identifier, Assert.Single(Body("Taxa_Imposto")).Kind);
    }

    // -------------------------------------------------------------- operadores

    [Fact]
    public void Tokenize_LeOperadoresAritmeticosEDeConcatenacao() =>
        Assert.Equal(
            [
                TokenKind.Number, TokenKind.Plus,
                TokenKind.Number, TokenKind.Minus,
                TokenKind.Number, TokenKind.Multiply,
                TokenKind.Number, TokenKind.Divide,
                TokenKind.Number, TokenKind.Power,
                TokenKind.Number, TokenKind.Ampersand,
                TokenKind.Number,
            ],
            Kinds("1+2-3*4/5^6&7"));

    [Fact]
    public void Tokenize_LeOperadoresDeComparacaoDeDoisCaracteres() =>
        Assert.Equal(
            [
                TokenKind.Reference, TokenKind.LessOrEqual,
                TokenKind.Reference, TokenKind.NotEqual,
                TokenKind.Reference, TokenKind.GreaterOrEqual,
                TokenKind.Reference, TokenKind.Less,
                TokenKind.Reference, TokenKind.Greater,
                TokenKind.Reference, TokenKind.Equal,
                TokenKind.Reference,
            ],
            Kinds("A1<=A2<>A3>=A4<A5>A6=A7"));

    [Fact]
    public void Tokenize_LeParentesesESeparadorDeArgumentos() =>
        Assert.Equal(
            [
                TokenKind.Identifier, TokenKind.OpenParen,
                TokenKind.Reference, TokenKind.ArgumentSeparator,
                TokenKind.Number, TokenKind.CloseParen,
            ],
            Kinds("ARRED(A1;2)"));

    [Fact]
    public void Tokenize_CaractereDesconhecido_Lanca()
    {
        FormulaSyntaxException exception = Assert.Throws<FormulaSyntaxException>(() => Tokenize("A1 @ B2"));

        Assert.Equal(3, exception.Position);
    }

    // ------------------------------------------------------------ literais de erro

    [Theory]
    [InlineData("#N/D", CellErrorType.NotAvailable)]
    [InlineData("#DIV/0!", CellErrorType.DivideByZero)]
    [InlineData("#NÚM!", CellErrorType.Number)]
    [InlineData("#VALOR!", CellErrorType.Value)]
    public void Tokenize_LeLiteraisDeErro(string formula, CellErrorType expected)
    {
        FormulaToken token = Assert.Single(Body(formula));

        Assert.Equal(TokenKind.Error, token.Kind);
        Assert.Equal(expected, token.Error);
    }

    [Fact]
    public void Tokenize_ErroDesconhecido_Lanca() =>
        Assert.Throws<FormulaSyntaxException>(() => Tokenize("#QUALQUER"));

    // --------------------------------------------------------- espaços e posições

    [Fact]
    public void Tokenize_IgnoraEspacosEmBranco() =>
        Assert.Equal(Kinds("1+2"), Kinds("  1  +\t2 \n"));

    [Fact]
    public void Tokenize_GuardaAPosicaoDeCadaToken()
    {
        FormulaToken[] tokens = Body("SOMA(A1;B2)");

        Assert.Equal([0, 4, 5, 7, 8, 10], tokens.Select(t => t.Position));
    }

    // ------------------------------------------------------------- fórmula real

    [Fact]
    public void Tokenize_FormulaDeDcfCompleta()
    {
        // Fluxo de caixa livre descontado, com premissa travada em outra aba.
        FormulaToken[] tokens = Body("SEERRO(D12/(1+Premissas!$B$3)^D$4;0)");

        Assert.Equal(
            [
                TokenKind.Identifier, TokenKind.OpenParen,
                TokenKind.Reference, TokenKind.Divide, TokenKind.OpenParen,
                TokenKind.Number, TokenKind.Plus, TokenKind.Reference, TokenKind.CloseParen,
                TokenKind.Power, TokenKind.Reference,
                TokenKind.ArgumentSeparator, TokenKind.Number, TokenKind.CloseParen,
            ],
            tokens.Select(t => t.Kind));

        Assert.Equal("SEERRO", tokens[0].Lexeme);
        Assert.Equal("Premissas!$B$3", tokens[7].Lexeme);
        Assert.Equal("D$4", tokens[10].Lexeme);
    }

    // ------------------------------------------------------- convenção alternativa

    [Fact]
    public void Tokenize_ConvencaoAmericanaUsaPontoEVirgula()
    {
        var tokenizer = new FormulaTokenizer(FormulaSyntax.EnUs);

        IReadOnlyList<FormulaToken> tokens = tokenizer.Tokenize("SUM(A1,1.5)");

        Assert.Equal(TokenKind.ArgumentSeparator, tokens[3].Kind);
        Assert.Equal(TokenKind.Number, tokens[4].Kind);
        Assert.Equal(1.5d, tokens[4].NumberValue);
    }

    [Fact]
    public void FormulaSyntax_ExigeSeparadoresDiferentes()
    {
        Assert.NotEqual(FormulaSyntax.Default.DecimalSeparator, FormulaSyntax.Default.ArgumentSeparator);
        Assert.Equal(',', FormulaSyntax.PtBr.DecimalSeparator);
        Assert.Equal(';', FormulaSyntax.PtBr.ArgumentSeparator);
    }
}
