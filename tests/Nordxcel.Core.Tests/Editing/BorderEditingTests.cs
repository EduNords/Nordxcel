using Nordxcel.Core.Editing;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Tests.Editing;

public class BorderEditingTests
{
    private static CellAddress At(string address) => CellAddress.Parse(address);

    private static CellBorders BordersAt(Worksheet sheet, string address) =>
        sheet.GetCell(At(address)).Style.Borders;

    [Fact]
    public void All_DaBordaEmTodaAresta_InternaEExterna()
    {
        var sheet = new Worksheet("DCF");

        BorderEditing.Apply(sheet, CellRange.Parse("A1:B2"), BorderPreset.All, BorderLineStyle.Thin, RgbColor.Black);

        foreach (string address in new[] { "A1", "A2", "B1", "B2" })
        {
            CellBorders borders = BordersAt(sheet, address);

            Assert.True(borders.Top.IsVisible);
            Assert.True(borders.Bottom.IsVisible);
            Assert.True(borders.Left.IsVisible);
            Assert.True(borders.Right.IsVisible);
        }
    }

    [Fact]
    public void Outline_SoMarcaAsArestasQueEstaoNoPerimetro()
    {
        var sheet = new Worksheet("DCF");

        BorderEditing.Apply(sheet, CellRange.Parse("A1:C3"), BorderPreset.Outline, BorderLineStyle.Thin, RgbColor.Black);

        // Canto superior esquerdo: só cima e esquerda.
        CellBorders topLeft = BordersAt(sheet, "A1");
        Assert.True(topLeft.Top.IsVisible);
        Assert.True(topLeft.Left.IsVisible);
        Assert.False(topLeft.Bottom.IsVisible);
        Assert.False(topLeft.Right.IsVisible);

        // Canto inferior direito: só baixo e direita.
        CellBorders bottomRight = BordersAt(sheet, "C3");
        Assert.True(bottomRight.Bottom.IsVisible);
        Assert.True(bottomRight.Right.IsVisible);
        Assert.False(bottomRight.Top.IsVisible);
        Assert.False(bottomRight.Left.IsVisible);

        // Célula do meio: nenhuma aresta é do perímetro.
        Assert.False(BordersAt(sheet, "B2").HasAny);
    }

    [Fact]
    public void Bottom_SoMarcaAUltimaLinhaDoIntervalo()
    {
        // O caso de uso real: sublinhar uma linha de total selecionando várias
        // colunas — só a borda inferior da seleção recebe o traço.
        var sheet = new Worksheet("DCF");

        BorderEditing.Apply(sheet, CellRange.Parse("A7:D9"), BorderPreset.Bottom, BorderLineStyle.Thin, RgbColor.Black);

        Assert.False(BordersAt(sheet, "A7").Bottom.IsVisible);
        Assert.False(BordersAt(sheet, "A8").Bottom.IsVisible);
        Assert.True(BordersAt(sheet, "A9").Bottom.IsVisible);
        Assert.True(BordersAt(sheet, "D9").Bottom.IsVisible);
    }

    [Fact]
    public void Top_Left_Right_SoMarcamOLadoCorrespondente()
    {
        var sheet = new Worksheet("DCF");

        BorderEditing.Apply(sheet, CellRange.Parse("A1:C3"), BorderPreset.Top, BorderLineStyle.Thin, RgbColor.Black);
        Assert.True(BordersAt(sheet, "B1").Top.IsVisible);
        Assert.False(BordersAt(sheet, "B3").Top.IsVisible);

        BorderEditing.Apply(sheet, CellRange.Parse("A1:C3"), BorderPreset.Left, BorderLineStyle.Thin, RgbColor.Black);
        Assert.True(BordersAt(sheet, "A2").Left.IsVisible);
        Assert.False(BordersAt(sheet, "C2").Left.IsVisible);

        BorderEditing.Apply(sheet, CellRange.Parse("A1:C3"), BorderPreset.Right, BorderLineStyle.Thin, RgbColor.Black);
        Assert.True(BordersAt(sheet, "C2").Right.IsVisible);
        Assert.False(BordersAt(sheet, "A2").Right.IsVisible);
    }

    [Fact]
    public void None_RemoveTodaBordaDoIntervalo()
    {
        var sheet = new Worksheet("DCF");
        BorderEditing.Apply(sheet, CellRange.Parse("A1:B2"), BorderPreset.All, BorderLineStyle.Thin, RgbColor.Black);

        BorderEditing.Apply(sheet, CellRange.Parse("A1:B2"), BorderPreset.None, BorderLineStyle.Thin, RgbColor.Black);

        Assert.False(BordersAt(sheet, "A1").HasAny);
        Assert.False(BordersAt(sheet, "B2").HasAny);
    }

    [Fact]
    public void Outline_PreservaBordaInternaJaExistente()
    {
        // Aplica "todas" primeiro (grade completa) e depois "contorno": as arestas
        // internas, que o contorno não mexe, continuam lá.
        var sheet = new Worksheet("DCF");
        BorderEditing.Apply(sheet, CellRange.Parse("A1:B2"), BorderPreset.All, BorderLineStyle.Thin, RgbColor.Black);

        BorderEditing.Apply(sheet, CellRange.Parse("A1:B2"), BorderPreset.Outline, BorderLineStyle.Thick, RgbColor.Black);

        // A borda direita de A1 é interna ao intervalo 2x2 — o contorno não a toca.
        Assert.Equal(BorderLineStyle.Thin, BordersAt(sheet, "A1").Right.Style);
        // Já a borda superior de A1 é externa e devia ter virado grossa.
        Assert.Equal(BorderLineStyle.Thick, BordersAt(sheet, "A1").Top.Style);
    }

    [Fact]
    public void Double_AplicaLinhaDuplaParaConvencaoDeTotal()
    {
        var sheet = new Worksheet("DCF");

        BorderEditing.Apply(sheet, CellRange.Parse("B9:D9"), BorderPreset.Top, BorderLineStyle.Double, RgbColor.Black);

        Assert.Equal(BorderLineStyle.Double, BordersAt(sheet, "C9").Top.Style);
    }

    [Fact]
    public void CelulaUnica_ContornoEhIgualATodasAsBordas()
    {
        var sheet = new Worksheet("DCF");

        BorderEditing.Apply(sheet, CellRange.Parse("A1"), BorderPreset.Outline, BorderLineStyle.Thin, RgbColor.Black);

        Assert.True(BordersAt(sheet, "A1").HasAny);
        CellBorders borders = BordersAt(sheet, "A1");
        Assert.True(borders.Top.IsVisible && borders.Bottom.IsVisible && borders.Left.IsVisible && borders.Right.IsVisible);
    }

    [Fact]
    public void SelecaoDeColunaOuLinhaInteira_NaoFazNada()
    {
        var sheet = new Worksheet("DCF");
        var wholeColumn = new CellRange(new CellAddress(0, 1), new CellAddress(CellAddress.MaxRows - 1, 1));

        BorderEditing.Apply(sheet, wholeColumn, BorderPreset.All, BorderLineStyle.Thin, RgbColor.Black);

        Assert.Equal(0, sheet.CellCount);
    }
}
