using Nordxcel.Core.Formulas;
using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Formulas;

public class FormulaParserTests
{
    /// <summary>
    /// Forma canônica em prefixo da árvore, para que os testes descrevam a
    /// estrutura sem precisar navegar nó a nó.
    /// </summary>
    private static string Tree(string formula) => FormulaParser.ParseDefault(formula).ToString()!;

    private static FormulaNode Parse(string formula) => FormulaParser.ParseDefault(formula);

    // ---------------------------------------------------------------- literais

    [Fact]
    public void Parse_Literais()
    {
        Assert.Equal(new NumberNode(1.5), Parse("1,5"));
        Assert.Equal(new TextNode("Receita"), Parse("\"Receita\""));
        Assert.Equal(new LogicalNode(true), Parse("VERDADEIRO"));
        Assert.Equal(new LogicalNode(false), Parse("falso"));
        Assert.Equal(new ErrorNode(CellErrorType.NotAvailable), Parse("#N/D"));
    }

    [Fact]
    public void Parse_NomeDesconhecidoViraNameNode() =>
        Assert.Equal(new NameNode("Taxa_Imposto"), Parse("Taxa_Imposto"));

    // -------------------------------------------------- sintaxe EnUs (importação)

    [Fact]
    public void EnUs_ReconheceLiteraisLogicosEmIngles()
    {
        var parser = new FormulaParser(FormulaSyntax.EnUs);

        Assert.Equal(new LogicalNode(true), parser.Parse("TRUE"));
        Assert.Equal(new LogicalNode(false), parser.Parse("false"));
    }

    [Fact]
    public void EnUs_ReconheceTokenDeErroEmIngles()
    {
        var parser = new FormulaParser(FormulaSyntax.EnUs);

        Assert.Equal(new ErrorNode(CellErrorType.NotAvailable), parser.Parse("#N/A"));
        Assert.Equal(new ErrorNode(CellErrorType.Value), parser.Parse("#VALUE!"));
    }

    [Fact]
    public void EnUs_TambemAceitaTokenDeErroEmPortugues() =>
        // Uma fórmula importada pode ter sido originalmente redigitada por alguém
        // usando o Excel em português — os dois vocabulários precisam funcionar.
        Assert.Equal(
            new ErrorNode(CellErrorType.NotAvailable),
            new FormulaParser(FormulaSyntax.EnUs).Parse("#N/D"));

    [Fact]
    public void PtBr_NaoReconheceLiteralLogicoEmIngles() =>
        // Sintaxe padrão continua só em português — "TRUE" vira nome desconhecido.
        Assert.Equal(new NameNode("TRUE"), Parse("TRUE"));

    // ------------------------------------------------- referências e intervalos

    [Fact]
    public void Parse_ReferenciaSimples() =>
        Assert.Equal(new ReferenceNode(CellReference.Parse("$B$3")), Parse("$B$3"));

    [Fact]
    public void Parse_IntervaloViraRangeNode()
    {
        var node = Assert.IsType<RangeNode>(Parse("B2:B10"));

        Assert.Equal(CellRange.Parse("B2:B10"), node.ToRange());
        Assert.Null(node.Sheet);
        Assert.Equal("B2:B10", node.ToString());
    }

    [Fact]
    public void Parse_IntervaloDeOutraAbaHerdaAAbaNoExtremoFinal()
    {
        var node = Assert.IsType<RangeNode>(Parse("Premissas!B2:B10"));

        Assert.Equal("Premissas", node.Sheet);
        Assert.Equal("Premissas", node.End.Sheet);
        Assert.Equal("Premissas!B2:B10", node.ToString());
    }

    [Fact]
    public void Parse_IntervaloComAAbaRepetidaNosDoisLadosEhAceito() =>
        Assert.Equal("Premissas!B2:B10", Tree("Premissas!B2:Premissas!B10"));

    [Fact]
    public void Parse_IntervaloEntreAbasDiferentes_Lanca()
    {
        FormulaSyntaxException exception =
            Assert.Throws<FormulaSyntaxException>(() => Parse("Premissas!B2:DCF!B10"));

        Assert.Contains("duas abas", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DoisPontosSemReferenciaDepois_Lanca() =>
        Assert.Throws<FormulaSyntaxException>(() => Parse("B2:5"));

    [Fact]
    public void Parse_IntervaloPreservaAsMarcacoesDeAbsoluto()
    {
        var node = Assert.IsType<RangeNode>(Parse("$B$2:$B$10"));

        Assert.True(node.Start.AbsoluteColumn);
        Assert.True(node.End.AbsoluteRow);
        Assert.Equal("$B$2:$B$10", node.ToString());
    }

    // ------------------------------------------------------------- precedência

    [Fact]
    public void Parse_MultiplicacaoAntesDeSoma() =>
        Assert.Equal("(+ 1 (* 2 3))", Tree("1+2*3"));

    [Fact]
    public void Parse_ParentesesMudamAPrecedencia() =>
        Assert.Equal("(* (+ 1 2) 3)", Tree("(1+2)*3"));

    [Fact]
    public void Parse_SomaESubtracaoAssociamAEsquerda() =>
        Assert.Equal("(- (- 10 3) 2)", Tree("10-3-2"));

    [Fact]
    public void Parse_DivisaoAssociaAEsquerda() =>
        Assert.Equal("(/ (/ 100 5) 2)", Tree("100/5/2"));

    [Fact]
    public void Parse_PotenciaAntesDeMultiplicacao() =>
        Assert.Equal("(* 2 (^ 3 2))", Tree("2*3^2"));

    [Fact]
    public void Parse_PotenciaAssociaAEsquerdaComoNoExcel()
    {
        // No Excel 2^3^2 é (2^3)^2 = 64, e não 2^(3^2) = 512.
        Assert.Equal("(^ (^ 2 3) 2)", Tree("2^3^2"));
    }

    [Fact]
    public void Parse_MenosUnarioEhMaisForteQuePotencia()
    {
        // No Excel -2^2 é (-2)^2 = 4, e não -(2^2) = -4.
        Assert.Equal("(^ (- 2) 2)", Tree("-2^2"));
    }

    [Fact]
    public void Parse_MenosUnarioDepoisDeOperador() =>
        Assert.Equal("(^ 2 (- 1))", Tree("2^-1"));

    [Fact]
    public void Parse_MenosUnarioEmpilhado() =>
        Assert.Equal("(- (- 5))", Tree("--5"));

    [Fact]
    public void Parse_PorcentagemEhOSufixoMaisForte()
    {
        Assert.Equal("(+ 1 (% 50))", Tree("1+50%"));
        Assert.Equal("(- (% 50))", Tree("-50%"));
        Assert.Equal("(% (% 50))", Tree("50%%"));
    }

    [Fact]
    public void Parse_ConcatenacaoFicaEntreAritmeticaEComparacao()
    {
        Assert.Equal("(& (+ 1 2) \"x\")", Tree("1+2&\"x\""));
        Assert.Equal("(= (& \"a\" \"b\") \"ab\")", Tree("\"a\"&\"b\"=\"ab\""));
    }

    [Fact]
    public void Parse_ComparacaoEhOOperadorMaisFraco() =>
        Assert.Equal("(>= (+ A1 1) (* B1 2))", Tree("A1+1>=B1*2"));

    [Theory]
    [InlineData("1=2", "(= 1 2)")]
    [InlineData("1<>2", "(<> 1 2)")]
    [InlineData("1<2", "(< 1 2)")]
    [InlineData("1<=2", "(<= 1 2)")]
    [InlineData("1>2", "(> 1 2)")]
    [InlineData("1>=2", "(>= 1 2)")]
    public void Parse_TodosOsComparadores(string formula, string expected) =>
        Assert.Equal(expected, Tree(formula));

    // ---------------------------------------------------------------- funções

    [Fact]
    public void Parse_ChamadaDeFuncaoComArgumentos()
    {
        var node = Assert.IsType<FunctionNode>(Parse("ARRED(A1;2)"));

        Assert.Equal("ARRED", node.Name);
        Assert.Equal(2, node.Arguments.Count);
        Assert.Equal("ARRED(A1, 2)", node.ToString());
    }

    [Fact]
    public void Parse_NomeDeFuncaoEhNormalizadoParaMaiusculas() =>
        Assert.Equal("MÉDIA", Assert.IsType<FunctionNode>(Parse("média(A1:A3)")).Name);

    [Fact]
    public void Parse_FuncaoSemArgumentos() =>
        Assert.Empty(Assert.IsType<FunctionNode>(Parse("HOJE()")).Arguments);

    [Fact]
    public void Parse_ArgumentoOmitidoViraMissingArgument()
    {
        var node = Assert.IsType<FunctionNode>(Parse("SE(A1;;0)"));

        Assert.Equal(3, node.Arguments.Count);
        Assert.Same(MissingArgumentNode.Instance, node.Arguments[1]);
        Assert.Equal("SE(A1, <vazio>, 0)", node.ToString());
    }

    [Fact]
    public void Parse_FuncoesAninhadas() =>
        Assert.Equal(
            "SEERRO(MÉDIA(B2:B10), 0)",
            Tree("SEERRO(MÉDIA(B2:B10);0)"));

    [Fact]
    public void Parse_FuncaoSemFecharParentese_Lanca()
    {
        FormulaSyntaxException exception = Assert.Throws<FormulaSyntaxException>(() => Parse("SOMA(A1;A2"));

        Assert.Contains("SOMA", exception.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ erros

    [Fact]
    public void Parse_FormulaVazia_Lanca() =>
        Assert.Throws<FormulaSyntaxException>(() => Parse(""));

    [Fact]
    public void Parse_OperadorSemOperandoDireito_Lanca()
    {
        FormulaSyntaxException exception = Assert.Throws<FormulaSyntaxException>(() => Parse("1+"));

        Assert.Equal(2, exception.Position);
    }

    [Fact]
    public void Parse_ParenteseSemFechar_Lanca() =>
        Assert.Throws<FormulaSyntaxException>(() => Parse("(1+2"));

    [Fact]
    public void Parse_SobraDeConteudoDepoisDaExpressao_Lanca()
    {
        FormulaSyntaxException exception = Assert.Throws<FormulaSyntaxException>(() => Parse("1 2"));

        Assert.Equal(2, exception.Position);
    }

    [Fact]
    public void Parse_SeparadorForaDeFuncao_Lanca() =>
        Assert.Throws<FormulaSyntaxException>(() => Parse("1;2"));

    // ------------------------------------------------------------- igualdade

    [Fact]
    public void Arvores_TemIgualdadeEstruturalInclusiveNosArgumentos()
    {
        // Sem isso, guardar fórmulas já compiladas em cache compararia listas por referência.
        Assert.Equal(Parse("SOMA(A1;A2)"), Parse("soma(A1;A2)"));
        Assert.Equal(Parse("SOMA(A1;A2)").GetHashCode(), Parse("SOMA(A1;A2)").GetHashCode());
        Assert.NotEqual(Parse("SOMA(A1;A2)"), Parse("SOMA(A1;A3)"));
        Assert.NotEqual(Parse("SOMA(A1)"), Parse("SOMA(A1;A2)"));
    }

    // --------------------------------------------------------- fórmula de DCF

    [Fact]
    public void Parse_FluxoDescontadoComPremissaTravadaEmOutraAba() =>
        Assert.Equal(
            "SEERRO((/ D12 (^ (+ 1 Premissas!$B$3) D$4)), 0)",
            Tree("SEERRO(D12/(1+Premissas!$B$3)^D$4;0)"));

    [Fact]
    public void Parse_ValorTerminalPorGordonGrowth() =>
        Assert.Equal(
            "(/ (* D12 (+ 1 $B$4)) (- $B$3 $B$4))",
            Tree("D12*(1+$B$4)/($B$3-$B$4)"));

    [Fact]
    public void Parse_MargemComProtecaoContraDivisaoPorZero() =>
        Assert.Equal(
            "SEERRO((/ D8 D5), 0)",
            Tree("SEERRO(D8/D5;0)"));
}
