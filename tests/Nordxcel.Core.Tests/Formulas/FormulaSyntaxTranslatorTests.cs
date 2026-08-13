using Nordxcel.Core.Formulas;
using Nordxcel.Core.Formulas.Ast;

namespace Nordxcel.Core.Tests.Formulas;

public class FormulaSyntaxTranslatorTests
{
    private static FormulaNode ParseEnUs(string formula) => new FormulaParser(FormulaSyntax.EnUs).Parse(formula);

    [Fact]
    public void TraduzNomeDeFuncaoConhecida()
    {
        FormulaNode node = ParseEnUs("SUM(A1,A2)");
        var unknown = new HashSet<string>();

        FormulaNode translated = FormulaSyntaxTranslator.ToDefault(node, FormulaSyntax.EnUs, unknown);

        Assert.Equal("SOMA(A1;A2)", FormulaWriter.Write(translated));
        Assert.Empty(unknown);
    }

    [Fact]
    public void FuncaoAninhada_TraduzTodosOsNiveis()
    {
        FormulaNode node = ParseEnUs("IFERROR(AVERAGE(B2:B10),0)");
        var unknown = new HashSet<string>();

        FormulaNode translated = FormulaSyntaxTranslator.ToDefault(node, FormulaSyntax.EnUs, unknown);

        Assert.Equal("SEERRO(MÉDIA(B2:B10);0)", FormulaWriter.Write(translated));
    }

    [Fact]
    public void FuncaoSemTraducao_MantemONomeEEntraNoConjuntoDeDesconhecidas()
    {
        FormulaNode node = ParseEnUs("OFFSET(A1,1,1)");
        var unknown = new HashSet<string>();

        FormulaNode translated = FormulaSyntaxTranslator.ToDefault(node, FormulaSyntax.EnUs, unknown);

        Assert.Equal("OFFSET(A1;1;1)", FormulaWriter.Write(translated));
        Assert.Contains("OFFSET", unknown);
    }

    [Fact]
    public void MisturaDeFuncaoConhecidaEDesconhecida_TraduzSoAConhecida()
    {
        FormulaNode node = ParseEnUs("SUM(OFFSET(A1,0,0),B1)");
        var unknown = new HashSet<string>();

        FormulaNode translated = FormulaSyntaxTranslator.ToDefault(node, FormulaSyntax.EnUs, unknown);

        Assert.Equal("SOMA(OFFSET(A1;0;0);B1)", FormulaWriter.Write(translated));
        Assert.Equal(["OFFSET"], unknown);
    }

    [Fact]
    public void SintaxePtBr_NaoTraduzNada()
    {
        // FunctionNamesReverse é null pra sintaxe padrão — a árvore volta intacta.
        FormulaNode node = FormulaParser.ParseDefault("SOMA(A1;A2)");
        var unknown = new HashSet<string>();

        FormulaNode translated = FormulaSyntaxTranslator.ToDefault(node, FormulaSyntax.PtBr, unknown);

        Assert.Same(node, translated);
        Assert.Empty(unknown);
    }

    [Fact]
    public void OperadorBinarioEUnario_TraduzDentroDosOperandos()
    {
        FormulaNode node = ParseEnUs("-SUM(A1,A2)+PRODUCT(B1,B2)");
        var unknown = new HashSet<string>();

        FormulaNode translated = FormulaSyntaxTranslator.ToDefault(node, FormulaSyntax.EnUs, unknown);

        Assert.Equal("-SOMA(A1;A2)+MULT(B1;B2)", FormulaWriter.Write(translated));
    }
}
