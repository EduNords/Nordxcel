using Nordxcel.Core.Calculation;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Calculation;

public class DataTableEngineTests
{
    private const string Sheet = "DCF";

    private static (CalculationEngine Engine, Workbook Workbook) Create(params string[] extraSheets)
    {
        var workbook = new Workbook();
        workbook.AddWorksheet(Sheet);

        foreach (string name in extraSheets)
        {
            workbook.AddWorksheet(name);
        }

        return (new CalculationEngine(workbook), workbook);
    }

    private static CellLocation At(string address, string sheet = Sheet) =>
        new(sheet, CellAddress.Parse(address));

    private static List<CellValue> Numbers(params double[] values) =>
        values.Select(CellValue.Number).ToList();

    // ------------------------------------------------------------ 1 variável

    [Fact]
    public void UmaVariavel_SubstituiOInputERecalculaASaida()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(0));
        engine.SetFormula(At("B1"), "A1*2");

        IReadOnlyList<CellValue> results = DataTableEngine.EvaluateSingleVariable(
            workbook, At("A1"), At("B1"), Numbers(1, 2, 3));

        Assert.Equal([2d, 4d, 6d], results.Select(v => v.AsNumber()));
    }

    [Fact]
    public void UmaVariavel_PropagaAoLongoDeUmaCadeiaDeFormulas()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(0));
        engine.SetFormula(At("B1"), "A1*2");
        engine.SetFormula(At("C1"), "B1+10");

        IReadOnlyList<CellValue> results = DataTableEngine.EvaluateSingleVariable(
            workbook, At("A1"), At("C1"), Numbers(5, 10));

        Assert.Equal([20d, 30d], results.Select(v => v.AsNumber()));
    }

    [Fact]
    public void UmaVariavel_NaoAlteraAPastaReal()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(8));
        engine.SetFormula(At("B1"), "A1*2");

        DataTableEngine.EvaluateSingleVariable(workbook, At("A1"), At("B1"), Numbers(1, 2, 3));

        Assert.Equal(8d, workbook[Sheet].GetValue(CellAddress.Parse("A1")).AsNumber());
        Assert.Equal(16d, workbook[Sheet].GetValue(CellAddress.Parse("B1")).AsNumber());
        Assert.Equal("A1*2", workbook[Sheet].GetCell(CellAddress.Parse("B1")).Formula);
    }

    [Fact]
    public void UmaVariavel_CelulaDeEntradaComFormulaViraLiteralSoNaSombra()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetFormula(At("A1"), "5+5");
        engine.SetFormula(At("B1"), "A1*2");

        IReadOnlyList<CellValue> results = DataTableEngine.EvaluateSingleVariable(
            workbook, At("A1"), At("B1"), Numbers(1));

        Assert.Equal(2d, results[0].AsNumber());
        Assert.Equal("5+5", workbook[Sheet].GetCell(CellAddress.Parse("A1")).Formula);
    }

    [Fact]
    public void UmaVariavel_ReferenciaEntreAbas()
    {
        (CalculationEngine engine, Workbook workbook) = Create("Premissas");

        engine.SetValue(At("B2", "Premissas"), CellValue.Number(0));
        engine.SetFormula(At("A1"), "Premissas!B2*3");

        IReadOnlyList<CellValue> results = DataTableEngine.EvaluateSingleVariable(
            workbook, At("B2", "Premissas"), At("A1"), Numbers(4, 5));

        Assert.Equal([12d, 15d], results.Select(v => v.AsNumber()));
    }

    [Fact]
    public void UmaVariavel_ListaVaziaDevolveListaVazia()
    {
        (CalculationEngine engine, Workbook workbook) = Create();
        engine.SetValue(At("A1"), CellValue.Number(1));

        IReadOnlyList<CellValue> results = DataTableEngine.EvaluateSingleVariable(
            workbook, At("A1"), At("A1"), []);

        Assert.Empty(results);
    }

    // ------------------------------------------------------------ 2 variáveis

    [Fact]
    public void DuasVariaveis_MatrizCombinaLinhaEColuna()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(0));
        engine.SetValue(At("B1"), CellValue.Number(0));
        engine.SetFormula(At("C1"), "A1+B1");

        CellValue[,] results = DataTableEngine.EvaluateTwoVariable(
            workbook,
            At("A1"), Numbers(1, 2, 3),
            At("B1"), Numbers(10, 20),
            At("C1"));

        Assert.Equal(3, results.GetLength(0));
        Assert.Equal(2, results.GetLength(1));

        Assert.Equal(11d, results[0, 0].AsNumber());
        Assert.Equal(21d, results[0, 1].AsNumber());
        Assert.Equal(12d, results[1, 0].AsNumber());
        Assert.Equal(22d, results[1, 1].AsNumber());
        Assert.Equal(13d, results[2, 0].AsNumber());
        Assert.Equal(23d, results[2, 1].AsNumber());
    }

    [Fact]
    public void DuasVariaveis_NaoAlteraAPastaReal()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(1));
        engine.SetValue(At("B1"), CellValue.Number(2));
        engine.SetFormula(At("C1"), "A1*B1");

        DataTableEngine.EvaluateTwoVariable(
            workbook,
            At("A1"), Numbers(5, 6),
            At("B1"), Numbers(7, 8),
            At("C1"));

        Assert.Equal(1d, workbook[Sheet].GetValue(CellAddress.Parse("A1")).AsNumber());
        Assert.Equal(2d, workbook[Sheet].GetValue(CellAddress.Parse("B1")).AsNumber());
        Assert.Equal(2d, workbook[Sheet].GetValue(CellAddress.Parse("C1")).AsNumber());
    }

    [Fact]
    public void DuasVariaveis_ListaDeUmValorEmCadaEixoDaTabelaMinima()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(0));
        engine.SetValue(At("B1"), CellValue.Number(0));
        engine.SetFormula(At("C1"), "A1+B1");

        CellValue[,] results = DataTableEngine.EvaluateTwoVariable(
            workbook,
            At("A1"), Numbers(100),
            At("B1"), Numbers(1),
            At("C1"));

        Assert.Equal(101d, results[0, 0].AsNumber());
    }

    [Fact]
    public void DuasVariaveis_UmEixoVazioDevolveMatrizVazia()
    {
        (CalculationEngine engine, Workbook workbook) = Create();
        engine.SetValue(At("A1"), CellValue.Number(0));
        engine.SetValue(At("B1"), CellValue.Number(0));

        CellValue[,] results = DataTableEngine.EvaluateTwoVariable(
            workbook,
            At("A1"), Numbers(1, 2),
            At("B1"), [],
            At("A1"));

        Assert.Equal(2, results.GetLength(0));
        Assert.Equal(0, results.GetLength(1));
    }

    [Fact]
    public void SaidaQueDependeDeUmCicloNaoRelacionadoAoInputViraErroCircular()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(0));
        engine.SetFormula(At("X1"), "Y1");
        engine.SetFormula(At("Y1"), "X1+A1");

        IReadOnlyList<CellValue> results = DataTableEngine.EvaluateSingleVariable(
            workbook, At("A1"), At("Y1"), Numbers(1, 2));

        Assert.All(results, value => Assert.Equal(CellErrorType.Circular, value.AsError()));
    }
}
