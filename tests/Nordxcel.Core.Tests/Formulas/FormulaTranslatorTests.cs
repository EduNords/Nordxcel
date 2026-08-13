using Nordxcel.Core.Formulas;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Formulas;

public class FormulaTranslatorTests
{
    private static string Translate(string formula, int rowDelta, int columnDelta)
    {
        var tree = FormulaParser.ParseDefault(formula);
        var translated = FormulaTranslator.Translate(tree, rowDelta, columnDelta);

        return FormulaWriter.Write(translated);
    }

    [Fact]
    public void ReferenciaRelativa_SeDeslocaComADistancia() =>
        Assert.Equal("C3", Translate("A1", 2, 2));

    [Fact]
    public void ReferenciaAbsoluta_NaoSeMove() =>
        Assert.Equal("$A$1", Translate("$A$1", 5, 5));

    [Fact]
    public void ReferenciaMista_SoOEixoRelativoSeMove()
    {
        Assert.Equal("$A3", Translate("$A1", 2, 5)); // coluna travada
        Assert.Equal("C$1", Translate("A$1", 2, 2)); // linha travada
    }

    [Fact]
    public void SemDeslocamento_DevolveAMesmaArvore()
    {
        var tree = FormulaParser.ParseDefault("A1+B1*2");

        var translated = FormulaTranslator.Translate(tree, 0, 0);

        Assert.Same(tree, translated);
    }

    [Fact]
    public void Intervalo_SeDeslocaMantendoOTamanho() =>
        Assert.Equal("C3:C11", Translate("B2:B10", 1, 1));

    [Fact]
    public void Intervalo_ComExtremoAbsoluto_SoOOutroExtremoSeMove() =>
        Assert.Equal("$B$2:C11", Translate("$B$2:B10", 1, 1));

    [Fact]
    public void ReferenciaEntreAbas_TraduzOEnderecoEPreservaAAba() =>
        Assert.Equal("Premissas!C3", Translate("Premissas!A1", 2, 2));

    [Fact]
    public void ReferenciaQueSaiDaPlanilha_ViraErroDeReferencia()
    {
        // Colar uma fórmula perto da borda esquerda faz a referência sair do grid.
        Assert.Equal("#REF!", Translate("A1", 0, -1));
        Assert.Equal("#REF!", Translate("A1", -1, 0));
    }

    [Fact]
    public void SoAPecaQueSaiDaPlanilhaViraErro_ORestoContinuaValido() =>
        // A1 sai da planilha (coluna -1); B1 desloca normalmente para A1.
        Assert.Equal("#REF!+A1", Translate("A1+B1", 0, -1));

    [Fact]
    public void IntervaloComUmExtremoForaDaPlanilha_ViraErroInteiro() =>
        // O intervalo não pode ficar "pela metade": ou os dois extremos traduzem, ou nenhum.
        Assert.Equal("#REF!", Translate("A1:A10", 0, -1));

    [Fact]
    public void FuncaoComReferenciasNosArgumentos_TraduzCadaUma() =>
        Assert.Equal("SOMA(C1;D1)", Translate("SOMA(A1;B1)", 0, 2));

    [Fact]
    public void ExpressaoAritmeticaComReferencias_TraduzTodas() =>
        Assert.Equal(
            "C3/(1+Premissas!$B$3)^C$4",
            Translate("A1/(1+Premissas!$B$3)^A$4", 2, 2));

    [Fact]
    public void UnarioEPorcentagem_TraduzemOOperando() =>
        Assert.Equal("-C3%", Translate("-A1%", 2, 2));

    [Fact]
    public void Literais_NaoTemReferenciaParaTraduzir()
    {
        Assert.Equal("1+2", Translate("1+2", 3, 3));
        Assert.Equal("\"texto\"", Translate("\"texto\"", 3, 3));
        Assert.Equal("VERDADEIRO", Translate("VERDADEIRO", 3, 3));
    }

    [Fact]
    public void TraducaoDeUmBlocoInteiro_UsaUmSoDeslocamentoParaTodasAsFormulas()
    {
        // O cenário real de colar um bloco: cada fórmula do bloco se desloca pela
        // MESMA distância, incluindo a que aponta para outra célula do próprio bloco.
        const int rowDelta = 3;
        const int columnDelta = 1;

        Assert.Equal("C5", Translate("B2", rowDelta, columnDelta));
        Assert.Equal("C6*2", Translate("B3*2", rowDelta, columnDelta));
    }
}
