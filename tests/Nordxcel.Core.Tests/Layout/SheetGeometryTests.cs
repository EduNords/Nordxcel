using Nordxcel.Core.Layout;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Layout;

public class SheetGeometryTests
{
    private static AxisMetrics Axis(double defaultSize, int count, params (int Index, double Size)[] overrides)
    {
        var map = new Dictionary<int, double>();

        foreach ((int index, double size) in overrides)
        {
            map[index] = size;
        }

        return new AxisMetrics(defaultSize, count, map);
    }

    // ----------------------------------------------------------- posições

    [Fact]
    public void SemAjustes_APosicaoEhOIndiceVezesOPadrao()
    {
        AxisMetrics axis = Axis(80, 100);

        Assert.Equal(0d, axis.OffsetOf(0));
        Assert.Equal(80d, axis.OffsetOf(1));
        Assert.Equal(800d, axis.OffsetOf(10));
        Assert.Equal(80d, axis.SizeOf(5));
    }

    [Fact]
    public void ComAjustes_APosicaoAcumulaOsDesvios()
    {
        // Coluna 2 com 200 em vez de 80: tudo depois dela desloca 120.
        AxisMetrics axis = Axis(80, 100, (2, 200));

        Assert.Equal(160d, axis.OffsetOf(2));
        Assert.Equal(360d, axis.OffsetOf(3));
        Assert.Equal(440d, axis.OffsetOf(4));
        Assert.Equal(200d, axis.SizeOf(2));
    }

    [Fact]
    public void ComVariosAjustes_ASomaEhAcumulada()
    {
        AxisMetrics axis = Axis(80, 100, (0, 200), (3, 40));

        Assert.Equal(0d, axis.OffsetOf(0));
        Assert.Equal(200d, axis.OffsetOf(1));
        Assert.Equal(280d, axis.OffsetOf(2));
        Assert.Equal(360d, axis.OffsetOf(3));
        Assert.Equal(400d, axis.OffsetOf(4));
        Assert.Equal(480d, axis.OffsetOf(5));
    }

    [Fact]
    public void TotalSize_CobreOEixoInteiro() =>
        Assert.Equal(1000d + 120d, Axis(10, 100, (5, 130)).TotalSize);

    // ------------------------------------------------------- busca por posição

    [Fact]
    public void IndexAt_SemAjustes()
    {
        AxisMetrics axis = Axis(80, 100);

        Assert.Equal(0, axis.IndexAt(0));
        Assert.Equal(0, axis.IndexAt(79.9));
        Assert.Equal(1, axis.IndexAt(80));
        Assert.Equal(12, axis.IndexAt(1000));
    }

    [Fact]
    public void IndexAt_ComAjustes()
    {
        AxisMetrics axis = Axis(80, 100, (2, 200));

        Assert.Equal(1, axis.IndexAt(159));
        Assert.Equal(2, axis.IndexAt(160));
        Assert.Equal(2, axis.IndexAt(359));
        Assert.Equal(3, axis.IndexAt(360));
        Assert.Equal(4, axis.IndexAt(440));
    }

    [Fact]
    public void IndexAt_EhOInversoDeOffsetOf()
    {
        AxisMetrics axis = Axis(20, 5000, (10, 60), (11, 5), (900, 120), (4000, 1));

        foreach (int index in new[] { 0, 1, 9, 10, 11, 12, 500, 899, 900, 901, 3999, 4000, 4001, 4999 })
        {
            Assert.Equal(index, axis.IndexAt(axis.OffsetOf(index)));
            Assert.Equal(index, axis.IndexAt(axis.OffsetOf(index) + (axis.SizeOf(index) / 2d)));
        }
    }

    [Fact]
    public void IndexAt_NaoSaiDosLimites()
    {
        AxisMetrics axis = Axis(80, 10);

        Assert.Equal(0, axis.IndexAt(-500));
        Assert.Equal(9, axis.IndexAt(1_000_000));
    }

    [Fact]
    public void Invalidate_FazAsPosicoesRefletiremANovaLargura()
    {
        var overrides = new Dictionary<int, double>();
        var axis = new AxisMetrics(80, 100, overrides);

        Assert.Equal(160d, axis.OffsetOf(2));

        overrides[0] = 200d;
        axis.Invalidate();

        Assert.Equal(280d, axis.OffsetOf(2));
    }

    // ------------------------------------------------------------- geometria

    [Fact]
    public void FaixaVisivel_CobreExatamenteOQueApareceNaTela()
    {
        var sheet = new Worksheet("DCF");
        var geometry = new SheetGeometry(sheet);

        // 800 de largura com colunas de 80 mostra 10 colunas inteiras; a 11ª entra na borda.
        VisibleRange range = geometry.GetVisibleRange(0, 0, 800, 400);

        Assert.Equal(0, range.FirstColumn);
        Assert.Equal(10, range.LastColumn);
        Assert.Equal(0, range.FirstRow);
        Assert.Equal(20, range.LastRow);
    }

    [Fact]
    public void FaixaVisivel_AcompanhaARolagem()
    {
        var sheet = new Worksheet("DCF");
        var geometry = new SheetGeometry(sheet);

        VisibleRange range = geometry.GetVisibleRange(800, 200, 400, 100);

        Assert.Equal(10, range.FirstColumn);
        Assert.Equal(15, range.LastColumn);
        Assert.Equal(10, range.FirstRow);
        Assert.Equal(15, range.LastRow);
    }

    [Fact]
    public void FaixaVisivel_NaoDependeDoTamanhoDaPlanilha()
    {
        // O custo do desenho tem que vir da janela, não do modelo.
        var sheet = new Worksheet("DCF");
        sheet.SetValue(new CellAddress(900_000, 15_000), CellValue.Number(1));

        var geometry = new SheetGeometry(sheet);
        VisibleRange range = geometry.GetVisibleRange(0, 0, 800, 400);

        Assert.Equal(11, range.ColumnCount);
        Assert.Equal(21, range.RowCount);
    }

    [Fact]
    public void GeometriaAcompanhaAMudancaDeLargura()
    {
        var sheet = new Worksheet("DCF");
        var geometry = new SheetGeometry(sheet);

        Assert.Equal(160d, geometry.Columns.OffsetOf(2));

        sheet.SetColumnWidth(0, 200);
        geometry.Synchronize();

        Assert.Equal(280d, geometry.Columns.OffsetOf(2));
    }

    [Fact]
    public void RetanguloDaCelula_UsaAsDimensoesDaAba()
    {
        var sheet = new Worksheet("DCF");
        sheet.SetColumnWidth(0, 200);
        sheet.SetRowHeight(0, 40);

        var geometry = new SheetGeometry(sheet);

        (double x, double y, double width, double height) = geometry.GetCellBounds(CellAddress.Parse("B2"));

        Assert.Equal(200d, x);
        Assert.Equal(40d, y);
        Assert.Equal(Worksheet.DefaultColumnWidth, width);
        Assert.Equal(Worksheet.DefaultRowHeight, height);
    }

    [Fact]
    public void ExtensaoRolavel_CobreAAreaUsadaComFolga()
    {
        var sheet = new Worksheet("DCF");
        var geometry = new SheetGeometry(sheet);

        (double emptyWidth, double emptyHeight) = geometry.GetScrollExtent(0, 0, 400, 300);

        sheet.SetValue(new CellAddress(200, 30), CellValue.Number(1));

        (double usedWidth, double usedHeight) = geometry.GetScrollExtent(0, 0, 400, 300);

        Assert.True(usedWidth > emptyWidth);
        Assert.True(usedHeight > emptyHeight);
    }

    [Fact]
    public void ExtensaoRolavel_SempreDeixaMeiaTelaAlemDaPosicaoAtual()
    {
        // Sem isso a rolagem bateria num fim artificial no meio da planilha.
        var geometry = new SheetGeometry(new Worksheet("DCF"));

        (_, double height) = geometry.GetScrollExtent(0, 10_000, 400, 300);

        Assert.True(height >= 10_000d + 300d);
    }

    [Fact]
    public void ExtensaoRolavel_NuncaFicaMenorQueAJanela()
    {
        var geometry = new SheetGeometry(new Worksheet("DCF"));

        (double width, double height) = geometry.GetScrollExtent(0, 0, 5000, 4000);

        Assert.True(width >= 5000d);
        Assert.True(height >= 4000d);
    }

    [Fact]
    public void ExtensaoRolavel_NaoPassaDoFimDaPlanilha()
    {
        var geometry = new SheetGeometry(new Worksheet("DCF"));

        (double width, double height) = geometry.GetScrollExtent(1e9, 1e9, 400, 300);

        Assert.Equal(geometry.Columns.TotalSize, width);
        Assert.Equal(geometry.Rows.TotalSize, height);
    }
}
