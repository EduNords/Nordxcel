using Nordxcel.Core.Formulas;
using Nordxcel.Core.Formulas.Ast;

namespace Nordxcel.Core.Tests.Formulas;

public class FormulaWriterTests
{
    /// <summary>Interpreta e escreve de volta — o teste de round-trip mais direto que existe.</summary>
    private static string RoundTrip(string formula) => FormulaWriter.Write(FormulaParser.ParseDefault(formula));

    [Theory]
    [InlineData("1")]
    [InlineData("1,5")]
    [InlineData("-3,25")]
    [InlineData("\"Receita\"")]
    [InlineData("VERDADEIRO")]
    [InlineData("FALSO")]
    [InlineData("#N/D")]
    [InlineData("A1")]
    [InlineData("$A$1")]
    [InlineData("A$1")]
    [InlineData("$A1")]
    [InlineData("B2:B10")]
    [InlineData("$B$2:$B$10")]
    public void RoundTrip_LiteraisEReferenciasVoltamIdenticos(string formula) =>
        Assert.Equal(formula, RoundTrip(formula));

    [Fact]
    public void RoundTrip_ReferenciaEntreAbas() =>
        Assert.Equal("Premissas!B5", RoundTrip("Premissas!B5"));

    [Fact]
    public void RoundTrip_AbaComEspacoMantemAsAspas() =>
        Assert.Equal("'Fluxo de Caixa'!$B$5", RoundTrip("'Fluxo de Caixa'!$B$5"));

    [Fact]
    public void RoundTrip_IntervaloEntreAbas() =>
        Assert.Equal("Premissas!B2:B10", RoundTrip("Premissas!B2:B10"));

    [Theory]
    [InlineData("1+2")]
    [InlineData("1-2")]
    [InlineData("1*2")]
    [InlineData("1/2")]
    [InlineData("1^2")]
    [InlineData("1&2")]
    [InlineData("1=2")]
    [InlineData("1<>2")]
    [InlineData("1<2")]
    [InlineData("1<=2")]
    [InlineData("1>2")]
    [InlineData("1>=2")]
    public void RoundTrip_OperadoresBinarios(string formula) => Assert.Equal(formula, RoundTrip(formula));

    [Fact]
    public void RoundTrip_FuncaoComArgumentos() =>
        Assert.Equal("SOMA(A1;A2;A3)", RoundTrip("SOMA(A1;A2;A3)"));

    [Fact]
    public void RoundTrip_FuncaoAninhada() =>
        Assert.Equal("SEERRO(MÉDIA(B2:B10);0)", RoundTrip("SEERRO(MÉDIA(B2:B10);0)"));

    [Fact]
    public void RoundTrip_ArgumentoOmitido() =>
        Assert.Equal("SE(A1;;0)", RoundTrip("SE(A1;;0)"));

    [Fact]
    public void RoundTrip_NomeNaoResolvido() =>
        Assert.Equal("Taxa_Imposto", RoundTrip("Taxa_Imposto"));

    // -------------------------------------------------- precedência e parênteses

    [Fact]
    public void SoParentesesQueMudamOResultadoSaoPreservados()
    {
        // (1+2)*3 precisa dos parênteses para não virar 1+2*3, que é outra coisa.
        Assert.Equal("(1+2)*3", RoundTrip("(1+2)*3"));

        // Já 1+(2*3) não precisa: multiplicação já é mais forte que soma, então o
        // escritor devolve a forma mínima e ainda assim recalcula o mesmo valor.
        Assert.Equal("1+2*3", RoundTrip("1+(2*3)"));
    }

    [Fact]
    public void AssociatividadeAEsquerda_PrecisaDeParentesesSoDoLadoDireito()
    {
        // "10-3-2" já associa à esquerda por padrão: sem parênteses.
        Assert.Equal("10-3-2", RoundTrip("10-3-2"));

        // "10-(3-2)" muda o resultado (5 vira 9), então os parênteses são obrigatórios.
        Assert.Equal("10-(3-2)", RoundTrip("10-(3-2)"));
    }

    [Fact]
    public void Potencia_AssociaAEsquerdaSemParenteses() =>
        Assert.Equal("2^3^2", RoundTrip("2^3^2"));

    [Fact]
    public void Potencia_ParentesesADireitaSaoObrigatorios() =>
        // "2^(3^2)" é outro número (512, e não 64) — o escritor tem que preservar isso.
        Assert.Equal("2^(3^2)", RoundTrip("2^(3^2)"));

    [Fact]
    public void MenosUnario_AntesDePotenciaNaoPrecisaDeParenteses() =>
        // Bate com o próprio comportamento do parser: -2^2 já significa (-2)^2.
        Assert.Equal("-2^2", RoundTrip("-2^2"));

    [Fact]
    public void MenosUnario_EmpilhaSemParenteses() =>
        Assert.Equal("--5", RoundTrip("--5"));

    [Fact]
    public void Porcentagem_DepoisDeMenosUnario_NaoPrecisaDeParenteses()
    {
        // "-50%" já significa -(50%): o escritor devolve a forma sem parênteses.
        Assert.Equal("-50%", RoundTrip("-50%"));
    }

    [Fact]
    public void Porcentagem_AntesDeMenosUnario_PrecisaDeParenteses()
    {
        // A árvore (-5)% é diferente de -(5%) — e só surge de reescrita
        // programática (tradução de fórmula), nunca de texto digitado, porque a
        // gramática não deixa % grudar num sinal unário. Sem os parênteses aqui,
        // reescrever essa árvore como texto produziria "-5%", que reinterpretado
        // volta como -(5%): a árvore errada.
        var tree = new UnaryNode(UnaryOperator.Percent, new UnaryNode(UnaryOperator.Negate, new NumberNode(5)));

        string written = FormulaWriter.Write(tree);

        Assert.Equal("(-5)%", written);
        Assert.Equal(tree, FormulaParser.ParseDefault(written));
    }

    [Fact]
    public void ConcatenacaoEComparacao_RespeitamAPrecedencia()
    {
        Assert.Equal("1+2&\"x\"", RoundTrip("1+2&\"x\""));
        Assert.Equal("A1+1>=B1*2", RoundTrip("A1+1>=B1*2"));
    }

    // ------------------------------------------------------------------ texto

    [Fact]
    public void Texto_ComAspaEmbutida_EscapaDobrandoAAspa()
    {
        // O inverso exato do que o tokenizer desfaz ao ler.
        Assert.Equal("\"diz \"\"oi\"\"\"", RoundTrip("\"diz \"\"oi\"\"\""));
    }

    [Fact]
    public void Texto_Vazio() => Assert.Equal("\"\"", RoundTrip("\"\""));

    // ---------------------------------------------------------------- números

    [Fact]
    public void Numero_UsaAVirgulaDecimalDaConvencaoBrasileira() =>
        Assert.Equal("1234,5", RoundTrip("1234,5"));

    [Fact]
    public void Numero_ConvencaoAmericanaUsaPonto()
    {
        FormulaNode tree = new FormulaParser(FormulaSyntax.EnUs).Parse("1.5+2.25");

        Assert.Equal("1.5+2.25", FormulaWriter.Write(tree, FormulaSyntax.EnUs));
    }

    // ------------------------------------------------- sintaxe EnUs (exportação)

    [Theory]
    [InlineData("SOMA", "SUM")]
    [InlineData("MÉDIA", "AVERAGE")]
    [InlineData("MÍNIMO", "MIN")]
    [InlineData("MÁXIMO", "MAX")]
    [InlineData("CONT.VALORES", "COUNTA")]
    [InlineData("SE", "IF")]
    [InlineData("SEERRO", "IFERROR")]
    [InlineData("E", "AND")]
    [InlineData("OU", "OR")]
    [InlineData("ARRED", "ROUND")]
    [InlineData("ABS", "ABS")]
    [InlineData("POTÊNCIA", "POWER")]
    [InlineData("RAIZ", "SQRT")]
    [InlineData("VPL", "NPV")]
    [InlineData("TIR", "IRR")]
    [InlineData("VF", "FV")]
    [InlineData("TAXA", "RATE")]
    public void EnUs_TraduzTodaFuncaoEmbutidaParaONomeDoExcelEmIngles(string portugues, string ingles)
    {
        FormulaNode tree = FormulaParser.ParseDefault($"{portugues}(A1)");

        Assert.Equal($"{ingles}(A1)", FormulaWriter.Write(tree, FormulaSyntax.EnUs));
    }

    [Fact]
    public void EnUs_FuncaoAninhadaUsaVirgulaEntreArgumentos() =>
        Assert.Equal(
            "IFERROR(AVERAGE(B2:B10),0)",
            FormulaWriter.Write(FormulaParser.ParseDefault("SEERRO(MÉDIA(B2:B10);0)"), FormulaSyntax.EnUs));

    [Fact]
    public void EnUs_FuncaoDesconhecidaLancaEmVezDeVazarSemTraducao()
    {
        var chamada = new FunctionNode("PROCV", [new NumberNode(1)]);

        Assert.Throws<NotSupportedException>(() => FormulaWriter.Write(chamada, FormulaSyntax.EnUs));
    }

    [Theory]
    [InlineData("VERDADEIRO", "TRUE")]
    [InlineData("FALSO", "FALSE")]
    public void EnUs_TraduzLiteralLogico(string portugues, string ingles) =>
        Assert.Equal(ingles, FormulaWriter.Write(FormulaParser.ParseDefault(portugues), FormulaSyntax.EnUs));

    [Theory]
    [InlineData("#DIV/0!", "#DIV/0!")]
    [InlineData("#VALOR!", "#VALUE!")]
    [InlineData("#REF!", "#REF!")]
    [InlineData("#NOME?", "#NAME?")]
    [InlineData("#NÚM!", "#NUM!")]
    [InlineData("#N/D", "#N/A")]
    [InlineData("#NULO!", "#NULL!")]
    public void EnUs_TraduzTokenDeErro(string portugues, string ingles) =>
        Assert.Equal(ingles, FormulaWriter.Write(FormulaParser.ParseDefault(portugues), FormulaSyntax.EnUs));

    // -------------------------------------------------------- fórmula de DCF

    [Fact]
    public void RoundTrip_FluxoDescontadoComPremissaEmOutraAba() =>
        Assert.Equal(
            "SEERRO(D12/(1+Premissas!$B$3)^D$4;0)",
            RoundTrip("SEERRO(D12/(1+Premissas!$B$3)^D$4;0)"));

    [Fact]
    public void RoundTrip_ValorTerminalPorGordonGrowth() =>
        Assert.Equal(
            "D12*(1+$B$4)/($B$3-$B$4)",
            RoundTrip("D12*(1+$B$4)/($B$3-$B$4)"));

    // -------------------------------------------------------- reinterpretação

    [Fact]
    public void ArvoreEscritaDeVoltaGeraTextoQueOParserAceitaDeNovo()
    {
        // Não basta o texto "parecer" certo — precisa ser reinterpretado como a
        // mesma árvore, ou a fórmula colada calcularia um valor diferente.
        string[] formulas =
        [
            "SEERRO(D12/(1+Premissas!$B$3)^D$4;0)",
            "-2^2*3+1",
            "SOMA(A1:A10)*(1+$B$1)",
            "SE(A1>0;\"positivo\";\"negativo ou zero\")",
        ];

        foreach (string formula in formulas)
        {
            var original = FormulaParser.ParseDefault(formula);
            string written = FormulaWriter.Write(original);
            var reparsed = FormulaParser.ParseDefault(written);

            Assert.Equal(original, reparsed);
        }
    }
}
