using Nordxcel.Core.Layout;

namespace Nordxcel.Core.Tests.Layout;

public class FrozenPaneMathTests
{
    private static AxisMetrics UniformAxis(double size, int count) => new(size, count, new Dictionary<int, double>());

    // ------------------------------------------------------------- FrozenExtent

    [Fact]
    public void FrozenExtent_SemColunaCongelada_EhZero() =>
        Assert.Equal(0d, FrozenPaneMath.FrozenExtent(UniformAxis(80, 100), 0, 800, 60));

    [Fact]
    public void FrozenExtent_SomaALarguraDasColunasCongeladas() =>
        // Duas colunas de 80: a área congelada tem 160, e sobra espaço de sobra
        // para não bater no mínimo reservado ao quadrante rolável.
        Assert.Equal(160d, FrozenPaneMath.FrozenExtent(UniformAxis(80, 100), 2, 800, 60));

    [Fact]
    public void FrozenExtent_RespeitaLargurasCustomizadas()
    {
        var overrides = new Dictionary<int, double> { [0] = 200 };
        var axis = new AxisMetrics(80, 100, overrides);

        // Coluna 0 com 200 + coluna 1 com 80 (padrão) = 280.
        Assert.Equal(280d, FrozenPaneMath.FrozenExtent(axis, 2, 800, 60));
    }

    [Fact]
    public void FrozenExtent_NuncaEngoleOQuadranteRolavelInteiro()
    {
        // Congelar 50 colunas de 80 (4000px) numa janela de 800 estouraria tudo;
        // a extensão fica limitada a deixar pelo menos o mínimo reservado.
        double extent = FrozenPaneMath.FrozenExtent(UniformAxis(80, 100), 50, 800, 60);

        Assert.Equal(740d, extent); // 800 - 60 de folga mínima
    }

    [Fact]
    public void FrozenExtent_NaoFicaNegativaQuandoAJanelaEhMenorQueAFolga() =>
        Assert.Equal(0d, FrozenPaneMath.FrozenExtent(UniformAxis(80, 100), 5, 30, 60));

    [Fact]
    public void FrozenExtent_NaoPassaDaContagemDoEixo() =>
        // Pedir para congelar mais colunas do que a planilha tem não deve estourar.
        Assert.Equal(
            UniformAxis(80, 5).TotalSize,
            FrozenPaneMath.FrozenExtent(UniformAxis(80, 5), 999, 10_000, 60));

    // ------------------------------------------------------------ ScrollableOffset

    [Fact]
    public void ScrollableOffset_SemCongelamento_DevolveORolagemPura() =>
        // Frozen extent zero: o quadrante rolável ocupa a área toda, como antes de
        // existir painel congelado — este é o caso que qualquer teste sem
        // congelamento sempre acerta, mesmo com a fórmula errada.
        Assert.Equal(150d, FrozenPaneMath.ScrollableOffset(150d, 0d));

    [Fact]
    public void ScrollableOffset_NaPosicaoMinimaDaZero()
    {
        // A rolagem mínima (sem rolar além do painel congelado) precisa fazer a
        // primeira coluna rolável começar exatamente na origem do quadrante —
        // ou seja, deslocamento zero.
        double frozenExtent = 200d;
        double minimumScroll = frozenExtent;

        Assert.Equal(0d, FrozenPaneMath.ScrollableOffset(minimumScroll, frozenExtent));
    }

    [Fact]
    public void ScrollableOffset_DescontaAExtensaoCongelada()
    {
        // Rolado 80px além do mínimo (que já é 200, a largura congelada):
        // o deslocamento efetivo dentro do quadrante rolável é só os 80px extras.
        Assert.Equal(80d, FrozenPaneMath.ScrollableOffset(280d, 200d));
    }

    [Fact]
    public void ScrollableOffset_ReproduzOBugQueJaAconteceu()
    {
        // A fórmula errada (usar a rolagem absoluta direto, sem descontar a
        // largura congelada) devolveria 280 aqui — deslocando o conteúdo do
        // quadrante rolável para fora do próprio recorte. A correta devolve 80.
        double wrongFormulaResult = 280d;
        double correctResult = FrozenPaneMath.ScrollableOffset(280d, 200d);

        Assert.NotEqual(wrongFormulaResult, correctResult);
        Assert.Equal(80d, correctResult);
    }
}
