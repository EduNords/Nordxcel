using Avalonia.Controls;
using Avalonia.Interactivity;
using Nordxcel.Core.Calculation;

namespace Nordxcel.Desktop;

public partial class MainWindow : Window
{
    private CalculationEngine _engine = null!;

    public MainWindow()
    {
        InitializeComponent();

        _engine = SampleContent.CreateWorkbook();

        Sheet.Engine = _engine;
        Sheet.SheetName = _engine.Workbook.Worksheets[1].Name;

        Tabs.Workbook = _engine.Workbook;
        Tabs.ActiveSheet = Sheet.SheetName;

        Sheet.SelectionChanged += (_, _) => UpdateFormulaBar();

        Tabs.SheetSelected += (_, name) =>
        {
            Sheet.SheetName = name;
            Tabs.ActiveSheet = name;
            UpdateFormulaBar();
            Sheet.FocusGrid();
        };

        Tabs.SheetRenamed += (_, names) => Sheet.NotifySheetRenamed(names.OldName, names.NewName);

        Tabs.WorkbookChanged += (_, _) =>
        {
            // Renomear ou excluir aba pode invalidar referências de fórmula em outras
            // abas; reconstruir o grafo garante que elas virem #REF! em vez de ficar
            // presas ao nome antigo.
            _engine.Rebuild();
            Sheet.Refresh();
            UpdateFormulaBar();
        };

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

    private void OnFreezePanes(object? sender, RoutedEventArgs e) => Sheet.FreezeAtSelection();

    private void OnFreezeTopRow(object? sender, RoutedEventArgs e) => Sheet.FreezeTopRow();

    private void OnFreezeFirstColumn(object? sender, RoutedEventArgs e) => Sheet.FreezeFirstColumn();

    private void OnUnfreezePanes(object? sender, RoutedEventArgs e) => Sheet.UnfreezePanes();
}
