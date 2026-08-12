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

        WireToolbar();

        UpdateFormulaBar();

        Opened += (_, _) => Sheet.FocusGrid();
    }

    private void UpdateFormulaBar()
    {
        Formulas.Update(Sheet.SelectionReference, Sheet.ActiveCellText);
        Toolbar.UpdateFromStyle(Sheet.ActiveStyle, Sheet.ActiveNumberFormat);
    }

    /// <summary>
    /// Liga cada botão da barra de formatação à seleção atual. A barra em si não
    /// conhece <c>Worksheet</c> nem <c>CalculationEngine</c> — só levanta eventos —
    /// e é o <see cref="SpreadsheetView"/> que sabe aplicar sobre a seleção.
    /// </summary>
    private void WireToolbar()
    {
        Toolbar.BoldToggleRequested += (_, _) => { Sheet.ToggleBold(); Sheet.FocusGrid(); };
        Toolbar.ItalicToggleRequested += (_, _) => { Sheet.ToggleItalic(); Sheet.FocusGrid(); };
        Toolbar.UnderlineToggleRequested += (_, _) => { Sheet.ToggleUnderline(); Sheet.FocusGrid(); };

        Toolbar.FontColorSelected += (_, color) => { Sheet.SetFontColor(color); Sheet.FocusGrid(); };
        Toolbar.FillColorSelected += (_, color) => { Sheet.SetFillColor(color); Sheet.FocusGrid(); };
        Toolbar.FontFamilySelected += (_, family) => { Sheet.SetFontFamily(family); Sheet.FocusGrid(); };
        Toolbar.FontSizeSelected += (_, size) => { Sheet.SetFontSize(size); Sheet.FocusGrid(); };

        Toolbar.HorizontalAlignmentSelected += (_, alignment) => { Sheet.SetHorizontalAlignment(alignment); Sheet.FocusGrid(); };
        Toolbar.VerticalAlignmentSelected += (_, alignment) => { Sheet.SetVerticalAlignment(alignment); Sheet.FocusGrid(); };

        Toolbar.BorderPresetSelected += (_, request) =>
        {
            Sheet.ApplyBorderPreset(request.Preset, request.Style, request.Color);
            Sheet.FocusGrid();
        };

        Toolbar.NumberFormatSelected += (_, mask) => { Sheet.SetNumberFormat(mask); Sheet.FocusGrid(); };
        Toolbar.DecimalStepRequested += (_, increase) => { Sheet.StepDecimals(increase); Sheet.FocusGrid(); };
    }

    private void OnFreezePanes(object? sender, RoutedEventArgs e) => Sheet.FreezeAtSelection();

    private void OnFreezeTopRow(object? sender, RoutedEventArgs e) => Sheet.FreezeTopRow();

    private void OnFreezeFirstColumn(object? sender, RoutedEventArgs e) => Sheet.FreezeFirstColumn();

    private void OnUnfreezePanes(object? sender, RoutedEventArgs e) => Sheet.UnfreezePanes();
}
