using Nordxcel.Core.Calculation;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Calculation;

public class CalculationEngineTests
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

    private static double ValueOf(Workbook workbook, string address, string sheet = Sheet) =>
        workbook[sheet].GetValue(CellAddress.Parse(address)).AsNumber();

    private static CellValue Raw(Workbook workbook, string address, string sheet = Sheet) =>
        workbook[sheet].GetValue(CellAddress.Parse(address));

    // ------------------------------------------------------- cálculo em cadeia

    [Fact]
    public void CalculaEmOrdemTopologica()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        // Gravadas fora de ordem de propósito: C1 depende de B1, que ainda não existe.
        engine.SetFormula(At("C1"), "B1+3");
        engine.SetFormula(At("B1"), "A1*2");
        engine.SetValue(At("A1"), CellValue.Number(10));

        Assert.Equal(20d, ValueOf(workbook, "B1"));
        Assert.Equal(23d, ValueOf(workbook, "C1"));
    }

    [Fact]
    public void AlterarAPremissaPropagaAteOFim()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(10));
        engine.SetFormula(At("B1"), "A1*2");
        engine.SetFormula(At("C1"), "B1+3");

        engine.SetValue(At("A1"), CellValue.Number(100));

        Assert.Equal(200d, ValueOf(workbook, "B1"));
        Assert.Equal(203d, ValueOf(workbook, "C1"));
    }

    [Fact]
    public void DependenciaEmLosangoAvaliaCadaCelulaUmaVezSo()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(2));
        engine.SetFormula(At("B1"), "A1*3");
        engine.SetFormula(At("C1"), "A1*5");
        engine.SetFormula(At("D1"), "B1+C1");

        engine.SetValue(At("A1"), CellValue.Number(4));

        Assert.Equal(32d, ValueOf(workbook, "D1"));
        Assert.Equal(3, engine.LastRecalculatedCount);
    }

    [Fact]
    public void IntervaloDisparaRecalculoDeQualquerCelulaDentroDele()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetFormula(At("D1"), "SOMA(A1:C1)");
        engine.SetValue(At("A1"), CellValue.Number(1));
        engine.SetValue(At("B1"), CellValue.Number(2));
        engine.SetValue(At("C1"), CellValue.Number(3));

        Assert.Equal(6d, ValueOf(workbook, "D1"));

        engine.SetValue(At("B1"), CellValue.Number(20));

        Assert.Equal(24d, ValueOf(workbook, "D1"));
    }

    [Fact]
    public void DependenciaEntreAbas()
    {
        (CalculationEngine engine, Workbook workbook) = Create("Premissas");

        engine.SetValue(At("B3", "Premissas"), CellValue.Number(0.11));
        engine.SetValue(At("D12"), CellValue.Number(1000));
        engine.SetFormula(At("D14"), "D12/(1+Premissas!$B$3)^3");

        Assert.Equal(1000d / Math.Pow(1.11d, 3), ValueOf(workbook, "D14"), 9);

        engine.SetValue(At("B3", "Premissas"), CellValue.Number(0.08));

        Assert.Equal(1000d / Math.Pow(1.08d, 3), ValueOf(workbook, "D14"), 9);
    }

    // ------------------------------------------------------------ incremental

    [Fact]
    public void RecalculaSoOQueDescendeDaCelulaAlterada()
    {
        (CalculationEngine engine, _) = Create();

        engine.SetValue(At("A1"), CellValue.Number(1));
        engine.SetFormula(At("B1"), "A1*2");
        engine.SetFormula(At("C1"), "B1*2");

        // Ramo sem nenhuma ligação com A1.
        engine.SetValue(At("A5"), CellValue.Number(50));
        engine.SetFormula(At("B5"), "A5*2");

        engine.SetValue(At("A1"), CellValue.Number(7));

        Assert.Equal(2, engine.LastRecalculatedCount);
    }

    [Fact]
    public void AlterarCelulaSemDependentes_NaoRecalculaNada()
    {
        (CalculationEngine engine, _) = Create();

        engine.SetFormula(At("B1"), "A1*2");
        engine.SetValue(At("Z50"), CellValue.Number(1));

        Assert.Equal(0, engine.LastRecalculatedCount);
    }

    [Fact]
    public void RecalculateAll_ReavaliaTodasAsFormulas()
    {
        (CalculationEngine engine, _) = Create();

        engine.SetValue(At("A1"), CellValue.Number(1));
        engine.SetFormula(At("B1"), "A1*2");
        engine.SetFormula(At("C1"), "B1*2");

        engine.RecalculateAll();

        Assert.Equal(2, engine.LastRecalculatedCount);
    }

    [Fact]
    public void AutoRecalculoDesligado_AdiaOCalculoAteOFim()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.AutoRecalculate = false;

        engine.SetValue(At("A1"), CellValue.Number(10));
        engine.SetFormula(At("B1"), "A1*2");

        Assert.True(Raw(workbook, "B1").IsBlank);

        engine.Recalculate();

        Assert.Equal(20d, ValueOf(workbook, "B1"));
    }

    // ------------------------------------------------------ ciclos e limpeza

    [Fact]
    public void ReferenciaCircularDeDuasCelulas_ViraErroDeCiclo()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetFormula(At("A1"), "B1+1");
        engine.SetFormula(At("B1"), "A1+1");

        Assert.Equal(CellErrorType.Circular, Raw(workbook, "A1").AsError());
        Assert.Equal(CellErrorType.Circular, Raw(workbook, "B1").AsError());
        Assert.True(engine.HasCircularReferences);
        Assert.Equal(2, engine.CircularCells.Count);
    }

    [Fact]
    public void CelulaQueReferenciaASiMesma_ViraErroDeCiclo()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetFormula(At("A1"), "A1+1");

        Assert.Equal(CellErrorType.Circular, Raw(workbook, "A1").AsError());
    }

    [Fact]
    public void CicloAtravesDeIntervalo_EhDetectado()
    {
        // O erro clássico: somar a coluna inteira estando dentro dela.
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetFormula(At("A5"), "SOMA(A1:A5)");

        Assert.Equal(CellErrorType.Circular, Raw(workbook, "A5").AsError());
    }

    [Fact]
    public void CicloEntreAbas_EhDetectado()
    {
        (CalculationEngine engine, Workbook workbook) = Create("Premissas");

        engine.SetFormula(At("A1"), "Premissas!A1+1");
        engine.SetFormula(At("A1", "Premissas"), "DCF!A1+1");

        Assert.Equal(CellErrorType.Circular, Raw(workbook, "A1").AsError());
        Assert.Equal(CellErrorType.Circular, Raw(workbook, "A1", "Premissas").AsError());
    }

    [Fact]
    public void CelulaQueDependeDeUmCiclo_TambemFicaMarcada()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetFormula(At("A1"), "B1+1");
        engine.SetFormula(At("B1"), "A1+1");
        engine.SetFormula(At("C1"), "A1*2");

        Assert.Equal(CellErrorType.Circular, Raw(workbook, "C1").AsError());
    }

    [Fact]
    public void DesfazerOCiclo_DevolveOsValores()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetFormula(At("A1"), "B1+1");
        engine.SetFormula(At("B1"), "A1+1");

        Assert.True(engine.HasCircularReferences);

        engine.SetValue(At("B1"), CellValue.Number(10));

        Assert.Equal(11d, ValueOf(workbook, "A1"));
        Assert.False(engine.HasCircularReferences);
    }

    [Fact]
    public void CicloNaoImpedeORestoDoModeloDeCalcular()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetFormula(At("A1"), "B1+1");
        engine.SetFormula(At("B1"), "A1+1");

        engine.SetValue(At("D1"), CellValue.Number(5));
        engine.SetFormula(At("E1"), "D1*2");

        Assert.Equal(10d, ValueOf(workbook, "E1"));
    }

    // ---------------------------------------------------- alteração de células

    [Fact]
    public void TrocarFormulaPorValor_DesfazAsDependencias()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(10));
        engine.SetFormula(At("B1"), "A1*2");

        engine.SetValue(At("B1"), CellValue.Number(99));
        engine.SetValue(At("A1"), CellValue.Number(1000));

        Assert.Equal(99d, ValueOf(workbook, "B1"));
        Assert.Empty(engine.Dependencies.GetDirectDependents(At("A1")));
    }

    [Fact]
    public void ApagarACelula_RecalculaQuemDependiaDela()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(10));
        engine.SetFormula(At("B1"), "A1*2");

        engine.ClearCell(At("A1"));

        Assert.Equal(0d, ValueOf(workbook, "B1"));
    }

    [Fact]
    public void SetFormula_PreservaFormatoEEstilo()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetCell(At("B1"), new Cell { NumberFormat = "#,##0;(#,##0)" });
        engine.SetFormula(At("B1"), "2+2");

        Cell cell = workbook[Sheet].GetCell(CellAddress.Parse("B1"));

        Assert.Equal("#,##0;(#,##0)", cell.NumberFormat);
        Assert.Equal(4d, cell.Value.AsNumber());
    }

    [Fact]
    public void FormulaInvalida_NaoAlteraAPlanilha()
    {
        (CalculationEngine engine, Workbook workbook) = Create();

        engine.SetValue(At("A1"), CellValue.Number(7));

        Assert.Throws<FormulaSyntaxException>(() => engine.SetFormula(At("A1"), "1+"));

        Assert.Equal(7d, ValueOf(workbook, "A1"));
    }

    [Fact]
    public void Rebuild_ReconstroiOGrafoDeUmaPastaJaPreenchida()
    {
        var workbook = new Workbook();
        Worksheet sheet = workbook.AddWorksheet(Sheet);

        // Conteúdo gravado direto na aba, como viria de um arquivo.
        sheet.SetValue(CellAddress.Parse("A1"), CellValue.Number(10));
        sheet.SetCell(CellAddress.Parse("B1"), Cell.FromFormula("A1*2"));
        sheet.SetCell(CellAddress.Parse("C1"), Cell.FromFormula("B1+5"));

        var engine = new CalculationEngine(workbook);

        Assert.Equal(20d, ValueOf(workbook, "B1"));
        Assert.Equal(25d, ValueOf(workbook, "C1"));

        engine.SetValue(At("A1"), CellValue.Number(100));

        Assert.Equal(205d, ValueOf(workbook, "C1"));
    }

    [Fact]
    public void Rebuild_FormulaCorrompidaNaoDerrubaOCarregamento()
    {
        var workbook = new Workbook();
        Worksheet sheet = workbook.AddWorksheet(Sheet);

        sheet.SetCell(CellAddress.Parse("A1"), Cell.FromFormula("1+"));
        sheet.SetCell(CellAddress.Parse("B1"), Cell.FromFormula("2+2"));

        _ = new CalculationEngine(workbook);

        Assert.Equal(CellErrorType.Name, Raw(workbook, "A1").AsError());
        Assert.Equal(4d, ValueOf(workbook, "B1"));
    }

    // -------------------------------------------------------- modelo completo

    [Fact]
    public void DcfCompleto_RecalculaAoMudarOWacc()
    {
        (CalculationEngine engine, Workbook workbook) = Create("Premissas");

        engine.SetValue(At("B1", "Premissas"), CellValue.Number(0.11));  // WACC
        engine.SetValue(At("B2", "Premissas"), CellValue.Number(0.03));  // crescimento na perpetuidade

        double[] flows = [1000, 1100, 1210];

        for (int year = 0; year < flows.Length; year++)
        {
            engine.SetValue(new CellLocation(Sheet, new CellAddress(0, year + 1)), CellValue.Number(flows[year]));
        }

        engine.SetFormula(At("B3"), "VPL(Premissas!$B$1;B1:D1)");
        engine.SetFormula(At("B4"), "D1*(1+Premissas!$B$2)/(Premissas!$B$1-Premissas!$B$2)/(1+Premissas!$B$1)^3");
        engine.SetFormula(At("B5"), "B3+B4");

        double Expected(double wacc)
        {
            double explicitPeriod = 0d;

            for (int year = 1; year <= flows.Length; year++)
            {
                explicitPeriod += flows[year - 1] / Math.Pow(1d + wacc, year);
            }

            double terminal = 1210d * 1.03d / (wacc - 0.03d) / Math.Pow(1d + wacc, 3);

            return explicitPeriod + terminal;
        }

        Assert.Equal(Expected(0.11d), ValueOf(workbook, "B5"), 8);

        engine.SetValue(At("B1", "Premissas"), CellValue.Number(0.09));

        Assert.Equal(Expected(0.09d), ValueOf(workbook, "B5"), 8);
    }
}
