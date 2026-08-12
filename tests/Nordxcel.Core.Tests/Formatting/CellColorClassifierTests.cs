using Nordxcel.Core.Calculation;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Tests.Formatting;

public class CellColorClassifierTests
{
    private const string Sheet = "DCF";

    private static CellColorRole Classify(string? formula, CellValue? value = null) =>
        CellColorClassifier.Classify(
            formula is null ? null : FormulaParser.ParseDefault(formula),
            value ?? CellValue.Number(1),
            Sheet);

    [Fact]
    public void NumeroDigitado_EhPremissaEFicaAzul()
    {
        Assert.Equal(CellColorRole.Input, Classify(null, CellValue.Number(0.11)));
        Assert.Equal(new RgbColor(0, 0, 255), CellColorClassifier.ColorOf(CellColorRole.Input));
    }

    [Fact]
    public void TextoDigitado_EhRotuloEFicaPreto()
    {
        // A convenção colore os números do modelo; rótulo azul só poluiria a leitura.
        Assert.Equal(CellColorRole.Label, Classify(null, CellValue.Text("Receita líquida")));
        Assert.Equal(new RgbColor(0, 0, 0), CellColorClassifier.ColorOf(CellColorRole.Label));
    }

    [Fact]
    public void FormulaDaPropriaAba_FicaPreta()
    {
        Assert.Equal(CellColorRole.Formula, Classify("A1*2"));
        Assert.Equal(CellColorRole.Formula, Classify("SOMA(B2:B10)"));
        Assert.Equal(new RgbColor(0, 0, 0), CellColorClassifier.ColorOf(CellColorRole.Formula));
    }

    [Fact]
    public void FormulaSemReferencia_TambemContaComoFormula()
    {
        // Constante escrita como fórmula, tipo =365, continua sendo cálculo.
        Assert.Equal(CellColorRole.Formula, Classify("365"));
    }

    [Fact]
    public void FormulaQueDevolveTexto_ContinuaSendoFormula() =>
        Assert.Equal(CellColorRole.Formula, Classify("\"Ano \"&A1", CellValue.Text("Ano 1")));

    [Fact]
    public void FormulaQuePuxaDeOutraAba_FicaVerde()
    {
        Assert.Equal(CellColorRole.Link, Classify("Premissas!B3"));
        Assert.Equal(CellColorRole.Link, Classify("D12/(1+Premissas!$B$3)^3"));
        Assert.Equal(new RgbColor(0, 128, 0), CellColorClassifier.ColorOf(CellColorRole.Link));
    }

    [Fact]
    public void IntervaloDeOutraAba_TambemEhLink() =>
        Assert.Equal(CellColorRole.Link, Classify("SOMA(Premissas!B2:B10)"));

    [Fact]
    public void ReferenciaAPropriaAbaComCaixaDiferente_NaoContaComoLink() =>
        Assert.Equal(CellColorRole.Formula, Classify("dcf!A1"));

    [Fact]
    public void CorManual_TemPrioridadeSobreAAutomatica()
    {
        var manual = CellStyle.Default with { FontColor = new RgbColor(200, 0, 0) };

        Assert.Equal(
            new RgbColor(200, 0, 0),
            CellColorClassifier.ResolveFontColor(manual, CellColorRole.Input));

        Assert.Equal(
            CellColorClassifier.InputColor,
            CellColorClassifier.ResolveFontColor(CellStyle.Default, CellColorRole.Input));
    }

    [Fact]
    public void Classificacao_UsaAArvoreQueOMotorJaTem()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet(Sheet);
        workbook.AddWorksheet("Premissas");

        var engine = new CalculationEngine(workbook);

        var label = new CellLocation(Sheet, CellAddress.Parse("A1"));
        var input = new CellLocation(Sheet, CellAddress.Parse("B1"));
        var formula = new CellLocation(Sheet, CellAddress.Parse("B2"));
        var link = new CellLocation(Sheet, CellAddress.Parse("B3"));

        engine.SetValue(label, CellValue.Text("WACC"));
        engine.SetValue(input, CellValue.Number(0.11));
        engine.SetFormula(formula, "B1*2");
        engine.SetFormula(link, "Premissas!B1");

        Worksheet sheet = workbook[Sheet];

        CellColorRole RoleOf(CellLocation location) => CellColorClassifier.Classify(
            sheet.GetCell(location.Address),
            engine.GetFormula(location),
            Sheet);

        Assert.Equal(CellColorRole.Label, RoleOf(label));
        Assert.Equal(CellColorRole.Input, RoleOf(input));
        Assert.Equal(CellColorRole.Formula, RoleOf(formula));
        Assert.Equal(CellColorRole.Link, RoleOf(link));
    }
}
