using Nordxcel.Core.Calculation;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Calculation;

public class DependencyGraphTests
{
    private const string Sheet = "DCF";

    private static CellLocation At(string address, string sheet = Sheet) =>
        new(sheet, CellAddress.Parse(address));

    /// <summary>Registra no grafo a fórmula que a célula conteria.</summary>
    private static void Register(DependencyGraph graph, CellLocation location, string formula)
    {
        var cells = new List<CellLocation>();
        var ranges = new List<RangeLocation>();

        FormulaDependencyScanner.Collect(
            FormulaParser.ParseDefault(formula),
            location.SheetName,
            cells,
            ranges);

        graph.SetPrecedents(location, cells, ranges);
    }

    // ---------------------------------------------------------------- scanner

    [Fact]
    public void Scanner_EncontraReferenciasEIntervalos()
    {
        var cells = new List<CellLocation>();
        var ranges = new List<RangeLocation>();

        FormulaDependencyScanner.Collect(
            FormulaParser.ParseDefault("SOMA(B2:B10)+A1*Premissas!$C$3"),
            Sheet,
            cells,
            ranges);

        Assert.Equal([At("A1"), At("C3", "Premissas")], cells);
        Assert.Equal([new RangeLocation(Sheet, CellRange.Parse("B2:B10"))], ranges);
    }

    [Fact]
    public void Scanner_ReferenciaSemAbaAssumeAAbaDaFormula()
    {
        var cells = new List<CellLocation>();

        FormulaDependencyScanner.Collect(FormulaParser.ParseDefault("A1"), "Premissas", cells, []);

        Assert.Equal("Premissas", Assert.Single(cells).SheetName);
    }

    [Fact]
    public void Scanner_FormulaSemReferencia_NaoDependeDeNada()
    {
        var cells = new List<CellLocation>();
        var ranges = new List<RangeLocation>();

        FormulaDependencyScanner.Collect(FormulaParser.ParseDefault("1+1"), Sheet, cells, ranges);

        Assert.Empty(cells);
        Assert.Empty(ranges);
    }

    // ------------------------------------------------------------------ grafo

    [Fact]
    public void Grafo_RegistraPrecedentesEDependentes()
    {
        var graph = new DependencyGraph();
        Register(graph, At("C1"), "A1+B1");

        Assert.Equal([At("A1"), At("B1")], graph.GetPrecedentCells(At("C1")));
        Assert.Equal([At("C1")], graph.GetDirectDependents(At("A1")));
        Assert.Equal([At("C1")], graph.GetDirectDependents(At("B1")));
        Assert.Empty(graph.GetDirectDependents(At("Z9")));
    }

    [Fact]
    public void Grafo_CelulaDentroDeIntervaloEhDependenciaSemVirarAresta()
    {
        var graph = new DependencyGraph();
        Register(graph, At("D1"), "SOMA(A1:C1)");

        Assert.Equal([At("D1")], graph.GetDirectDependents(At("B1")));
        Assert.Empty(graph.GetDirectDependents(At("D2")));

        // O intervalo é guardado inteiro, não expandido em três arestas.
        Assert.Single(graph.GetPrecedentRanges(At("D1")));
        Assert.Empty(graph.GetPrecedentCells(At("D1")));
    }

    [Fact]
    public void Grafo_IntervaloSoValeNaAbaCerta()
    {
        var graph = new DependencyGraph();
        Register(graph, At("D1"), "SOMA(Premissas!A1:C1)");

        Assert.Equal([At("D1")], graph.GetDirectDependents(At("B1", "Premissas")));
        Assert.Empty(graph.GetDirectDependents(At("B1")));
    }

    [Fact]
    public void Grafo_RedefinirPrecedentesRemoveAsArestasAntigas()
    {
        var graph = new DependencyGraph();
        Register(graph, At("C1"), "A1+B1");
        Register(graph, At("C1"), "B1*2");

        Assert.Empty(graph.GetDirectDependents(At("A1")));
        Assert.Equal([At("C1")], graph.GetDirectDependents(At("B1")));
    }

    [Fact]
    public void Grafo_RedefinirPrecedentesRemoveOsIntervalosAntigos()
    {
        var graph = new DependencyGraph();
        Register(graph, At("D1"), "SOMA(A1:C1)");
        Register(graph, At("D1"), "SOMA(A5:C5)");

        Assert.Empty(graph.GetDirectDependents(At("B1")));
        Assert.Equal([At("D1")], graph.GetDirectDependents(At("B5")));
    }

    [Fact]
    public void Grafo_RemoverCelulaLimpaOsDoisSentidos()
    {
        var graph = new DependencyGraph();
        Register(graph, At("C1"), "A1");
        Register(graph, At("D1"), "SOMA(A1:A9)");

        Assert.True(graph.RemoveCell(At("C1")));
        Assert.False(graph.RemoveCell(At("C1")));

        Assert.Equal([At("D1")], graph.GetDirectDependents(At("A1")));
        Assert.Empty(graph.GetPrecedentCells(At("C1")));

        Assert.True(graph.RemoveCell(At("D1")));
        Assert.Empty(graph.GetDirectDependents(At("A1")));
    }

    [Fact]
    public void Grafo_NaoRepeteDependenteQuandoACelulaEstaNoIntervaloENaReferencia()
    {
        var graph = new DependencyGraph();
        Register(graph, At("D1"), "SOMA(A1:C1)+A1");

        Assert.Single(graph.GetDirectDependents(At("A1")));
    }

    [Fact]
    public void Grafo_ContaAsCelulasRastreadas()
    {
        var graph = new DependencyGraph();
        Register(graph, At("C1"), "A1");
        Register(graph, At("C2"), "1+1");

        Assert.Equal(2, graph.TrackedCellCount);

        graph.Clear();

        Assert.Equal(0, graph.TrackedCellCount);
    }
}
