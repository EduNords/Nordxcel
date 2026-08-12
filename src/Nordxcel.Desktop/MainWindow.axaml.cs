using Avalonia.Controls;
using Nordxcel.Core.Calculation;

namespace Nordxcel.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        CalculationEngine engine = SampleContent.CreateWorkbook();

        Sheet.Engine = engine;
        Sheet.SheetName = engine.Workbook.Worksheets[1].Name;

        Sheet.SelectionChanged += (_, _) => UpdateFormulaBar();

        Formulas.Committed += (_, text) =>
        {
            Sheet.CommitActiveCell(text);
            Sheet.FocusGrid();
        };

        Formulas.Cancelled += (_, _) =>
        {
            UpdateFormulaBar();
            Sheet.FocusGrid();
        };

        UpdateFormulaBar();

        Opened += (_, _) => Sheet.FocusGrid();
    }

    private void UpdateFormulaBar() => Formulas.Update(Sheet.SelectionReference, Sheet.ActiveCellText);
}
