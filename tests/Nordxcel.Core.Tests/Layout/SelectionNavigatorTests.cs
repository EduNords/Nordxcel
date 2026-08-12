using Nordxcel.Core.Layout;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Layout;

public class SelectionNavigatorTests
{
    private static CellAddress At(string address) => CellAddress.Parse(address);

    /// <summary>Monta uma aba com as células informadas preenchidas.</summary>
    private static Worksheet SheetWith(params string[] addresses)
    {
        var sheet = new Worksheet("DCF");

        foreach (string address in addresses)
        {
            sheet.SetValue(At(address), CellValue.Number(1));
        }

        return sheet;
    }

    // ------------------------------------------------------------ passo simples

    [Theory]
    [InlineData("B2", NavigationDirection.Up, "B1")]
    [InlineData("B2", NavigationDirection.Down, "B3")]
    [InlineData("B2", NavigationDirection.Left, "A2")]
    [InlineData("B2", NavigationDirection.Right, "C2")]
    public void Step_AndaUmaCelula(string from, NavigationDirection direction, string expected) =>
        Assert.Equal(At(expected), SelectionNavigator.Step(At(from), direction));

    [Fact]
    public void Step_ParaNasBordasDaPlanilha()
    {
        Assert.Equal(At("A1"), SelectionNavigator.Step(At("A1"), NavigationDirection.Up));
        Assert.Equal(At("A1"), SelectionNavigator.Step(At("A1"), NavigationDirection.Left));
    }

    [Fact]
    public void Step_ComContagemAndaVariasCelulas() =>
        Assert.Equal(At("B22"), SelectionNavigator.Step(At("B2"), NavigationDirection.Down, 20));

    // ---------------------------------------------------------- Ctrl com seta

    [Fact]
    public void JumpToEdge_DeDentroDeUmBlocoVaiAteOFimDele()
    {
        // Coluna A preenchida de A1 a A5, e depois nada.
        Worksheet sheet = SheetWith("A1", "A2", "A3", "A4", "A5");

        Assert.Equal(At("A5"), SelectionNavigator.JumpToEdge(sheet, At("A1"), NavigationDirection.Down));
        Assert.Equal(At("A5"), SelectionNavigator.JumpToEdge(sheet, At("A2"), NavigationDirection.Down));
    }

    [Fact]
    public void JumpToEdge_DoFimDeUmBlocoPulaOVazioAteOProximoConteudo()
    {
        Worksheet sheet = SheetWith("A1", "A2", "A10", "A11");

        Assert.Equal(At("A10"), SelectionNavigator.JumpToEdge(sheet, At("A2"), NavigationDirection.Down));
    }

    [Fact]
    public void JumpToEdge_DeCelulaVaziaProcuraOProximoConteudo()
    {
        Worksheet sheet = SheetWith("A10");

        Assert.Equal(At("A10"), SelectionNavigator.JumpToEdge(sheet, At("A3"), NavigationDirection.Down));
    }

    [Fact]
    public void JumpToEdge_SemNadaPelaFrenteVaiParaABordaDaPlanilha()
    {
        Worksheet sheet = SheetWith("A1");

        Assert.Equal(
            new CellAddress(CellAddress.MaxRows - 1, 0),
            SelectionNavigator.JumpToEdge(sheet, At("A1"), NavigationDirection.Down));

        Assert.Equal(
            new CellAddress(0, CellAddress.MaxColumns - 1),
            SelectionNavigator.JumpToEdge(sheet, At("A1"), NavigationDirection.Right));
    }

    [Fact]
    public void JumpToEdge_ParaCimaEParaEsquerda()
    {
        Worksheet sheet = SheetWith("A5", "A6", "A7", "C7", "D7", "E7");

        Assert.Equal(At("A5"), SelectionNavigator.JumpToEdge(sheet, At("A7"), NavigationDirection.Up));
        Assert.Equal(At("C7"), SelectionNavigator.JumpToEdge(sheet, At("E7"), NavigationDirection.Left));
    }

    [Fact]
    public void JumpToEdge_AtravessaUmaLinhaDeFluxoDeCaixa()
    {
        // Linha 12 preenchida de B12 a G12: é o caso de uso real do atalho.
        Worksheet sheet = SheetWith("B12", "C12", "D12", "E12", "F12", "G12");

        Assert.Equal(At("G12"), SelectionNavigator.JumpToEdge(sheet, At("B12"), NavigationDirection.Right));
        Assert.Equal(At("B12"), SelectionNavigator.JumpToEdge(sheet, At("G12"), NavigationDirection.Left));
    }

    [Fact]
    public void JumpToEdge_NaoVarreAPlanilhaInteiraAtrasDeNada()
    {
        // Sem limite pela área usada, isso percorreria um milhão de linhas.
        Worksheet sheet = SheetWith("A1");

        CellAddress result = SelectionNavigator.JumpToEdge(sheet, At("A1"), NavigationDirection.Down);

        Assert.Equal(CellAddress.MaxRows - 1, result.Row);
    }

    // ------------------------------------------------------------ Home e End

    [Fact]
    public void Home_VaiParaOComecoDaLinha() =>
        Assert.Equal(At("A7"), SelectionNavigator.HomeOfRow(At("F7")));

    [Fact]
    public void CtrlHome_VaiParaA1() =>
        Assert.Equal(CellAddress.Origin, SelectionNavigator.HomeOfSheet());

    [Fact]
    public void CtrlEnd_VaiParaOFimDaAreaUsada()
    {
        Worksheet sheet = SheetWith("B3", "F12");

        Assert.Equal(At("F12"), SelectionNavigator.EndOfContent(sheet));
        Assert.Equal(CellAddress.Origin, SelectionNavigator.EndOfContent(new Worksheet("Vazia")));
    }
}
