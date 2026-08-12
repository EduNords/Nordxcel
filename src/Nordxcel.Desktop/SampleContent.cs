using Nordxcel.Core.Calculation;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Desktop;

/// <summary>
/// Conteúdo de demonstração para a janela abrir com algo na tela enquanto abrir e
/// salvar arquivo ainda não existem. <b>Some no commit de persistência</b> — a partir
/// de lá o aplicativo abre com uma planilha em branco, como o Excel.
/// </summary>
internal static class SampleContent
{
    public static CalculationEngine CreateWorkbook()
    {
        var workbook = new Workbook();
        Worksheet premissas = workbook.AddWorksheet("Premissas");
        Worksheet plan = workbook.AddWorksheet("Planilha1");

        premissas.SetColumnWidth(0, 180);
        plan.SetColumnWidth(0, 200);

        var engine = new CalculationEngine(workbook) { AutoRecalculate = false };

        Label(engine, "Premissas", "A1", "WACC", bold: true);
        Label(engine, "Premissas", "A2", "Crescimento na perpetuidade", bold: true);

        Percent(engine, "Premissas", "B1", 0.11);
        Percent(engine, "Premissas", "B2", 0.03);

        Label(engine, "Planilha1", "A1", "Fluxo de caixa livre", bold: true);
        Label(engine, "Planilha1", "A3", "FCF projetado");
        Label(engine, "Planilha1", "A4", "Fator de desconto");
        Label(engine, "Planilha1", "A5", "FCF descontado");
        Label(engine, "Planilha1", "A7", "Soma dos descontados", bold: true);
        Label(engine, "Planilha1", "A8", "Valor terminal", bold: true);
        Label(engine, "Planilha1", "A9", "Enterprise Value", bold: true);

        double[] flows = [1000, 1100, 1210, 1331, 1464.1];

        for (int year = 0; year < flows.Length; year++)
        {
            int column = year + 1;

            Number(engine, "Planilha1", new CellAddress(1, column), year + 1, "0");
            Number(engine, "Planilha1", new CellAddress(2, column), flows[year], StandardNumberFormats.Thousands);

            string letter = CellAddress.ColumnToName(column);

            Formula(engine, "Planilha1", $"{letter}4", $"1/(1+Premissas!$B$1)^{letter}2", "0.000");
            Formula(engine, "Planilha1", $"{letter}5", $"{letter}3*{letter}4", StandardNumberFormats.Thousands);
        }

        Formula(engine, "Planilha1", "B7", "SOMA(B5:F5)", StandardNumberFormats.Thousands);
        Formula(engine, "Planilha1", "B8", "F3*(1+Premissas!$B$2)/(Premissas!$B$1-Premissas!$B$2)*F4", StandardNumberFormats.Thousands);
        Formula(engine, "Planilha1", "B9", "B7+B8", StandardNumberFormats.Thousands);

        var total = new CellLocation("Planilha1", CellAddress.Parse("B9"));
        Cell totalCell = plan.GetCell(total.Address);

        plan.SetCell(total.Address, totalCell with
        {
            Style = totalCell.Style with
            {
                Bold = true,
                Borders = CellBorders.None with { Top = BorderEdge.Thin(RgbColor.Black) },
            },
        });

        engine.AutoRecalculate = true;
        engine.RecalculateAll();

        return engine;
    }

    private static void Label(CalculationEngine engine, string sheet, string address, string text, bool bold = false)
    {
        var location = new CellLocation(sheet, CellAddress.Parse(address));

        engine.SetCell(location, new Cell
        {
            Value = CellValue.Text(text),
            Style = bold ? CellStyle.Default with { Bold = true } : CellStyle.Default,
        });
    }

    private static void Percent(CalculationEngine engine, string sheet, string address, double value) =>
        engine.SetCell(
            new CellLocation(sheet, CellAddress.Parse(address)),
            new Cell { Value = CellValue.Number(value), NumberFormat = StandardNumberFormats.Percent });

    private static void Number(CalculationEngine engine, string sheet, CellAddress address, double value, string mask) =>
        engine.SetCell(
            new CellLocation(sheet, address),
            new Cell { Value = CellValue.Number(value), NumberFormat = mask });

    private static void Formula(CalculationEngine engine, string sheet, string address, string formula, string mask) =>
        engine.SetCell(
            new CellLocation(sheet, CellAddress.Parse(address)),
            Cell.FromFormula(formula) with { NumberFormat = mask });
}
