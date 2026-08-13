using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Nordxcel.Core.Calculation;
using Nordxcel.Core.Model;
using Nordxcel.Core.Persistence;
using Nordxcel.Desktop.Controls;

namespace Nordxcel.Desktop;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType NordxcelFileType = new("Nordxcel") { Patterns = ["*.nxcl"] };

    private CalculationEngine _engine = null!;
    private string? _currentFilePath;
    private bool _isDirty;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        // Abre em branco, como o Excel — o conteúdo de exemplo era só para
        // desenvolver a grade antes de existir salvar/abrir de verdade.
        LoadWorkbook(Workbook.CreateDefault(), path: null);

        Sheet.SelectionChanged += (_, _) => UpdateFormulaBar();

        Sheet.ContentChanged += (_, _) =>
        {
            _isDirty = true;
            UpdateTitle();
        };

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
            _isDirty = true;
            Sheet.Refresh();
            UpdateFormulaBar();
            UpdateTitle();
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

        Closing += OnWindowClosing;

        UpdateFormulaBar();

        Opened += (_, _) => Sheet.FocusGrid();
    }

    private void UpdateFormulaBar()
    {
        Formulas.Update(Sheet.SelectionReference, Sheet.ActiveCellText);
        Toolbar.UpdateFromStyle(Sheet.ActiveStyle, Sheet.ActiveNumberFormat);

        UndoMenuItem.Header = Sheet.NextUndoDescription is { } undo ? $"Desfazer {undo}" : "Desfazer";
        UndoMenuItem.IsEnabled = Sheet.CanUndo;

        RedoMenuItem.Header = Sheet.NextRedoDescription is { } redo ? $"Refazer {redo}" : "Refazer";
        RedoMenuItem.IsEnabled = Sheet.CanRedo;
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

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        Sheet.Undo();
        Sheet.FocusGrid();
    }

    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        Sheet.Redo();
        Sheet.FocusGrid();
    }

    private void OnCut(object? sender, RoutedEventArgs e)
    {
        Sheet.Cut();
        Sheet.FocusGrid();
    }

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        Sheet.Copy();
        Sheet.FocusGrid();
    }

    private void OnPaste(object? sender, RoutedEventArgs e)
    {
        Sheet.Paste();
        Sheet.FocusGrid();
    }

    // ------------------------------------------------------------- arquivo

    private async void OnNew(object? sender, RoutedEventArgs e) => await NewAsync();

    private async void OnOpen(object? sender, RoutedEventArgs e) => await OpenAsync();

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await SaveAsAsync();

    private async Task NewAsync()
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        LoadWorkbook(Workbook.CreateDefault(), path: null);
    }

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        string? path = await PromptOpenPathAsync();

        if (path is null)
        {
            return;
        }

        try
        {
            Workbook workbook = WorkbookSerializer.DeserializeFromFile(path);
            LoadWorkbook(workbook, path);
        }
        catch (Exception exception) when (exception is WorkbookFormatException or IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Não foi possível abrir o arquivo", exception.Message, "OK");
        }
    }

    private async Task SaveAsync()
    {
        string? path = _currentFilePath ?? await PromptSavePathAsync();

        if (path is not null)
        {
            await SaveToPathAsync(path);
        }
    }

    private async Task SaveAsAsync()
    {
        string? path = await PromptSavePathAsync();

        if (path is not null)
        {
            await SaveToPathAsync(path);
        }
    }

    private async Task SaveToPathAsync(string path)
    {
        try
        {
            WorkbookSerializer.SerializeToFile(_engine.Workbook, path);
            _currentFilePath = path;
            _isDirty = false;
            UpdateTitle();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Não foi possível salvar o arquivo", exception.Message, "OK");
        }
    }

    private async Task<string?> PromptSavePathAsync()
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar modelo",
            SuggestedFileName = _currentFilePath is null ? "Modelo" : Path.GetFileNameWithoutExtension(_currentFilePath),
            DefaultExtension = "nxcl",
            FileTypeChoices = [NordxcelFileType],
        });

        return file?.TryGetLocalPath();
    }

    private async Task<string?> PromptOpenPathAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir modelo",
            AllowMultiple = false,
            FileTypeFilter = [NordxcelFileType],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Se houver alteração não salva, pergunta o que fazer antes de prosseguir
    /// com uma ação que descartaria o modelo atual (novo, abrir, fechar).
    /// Devolve verdadeiro quando é seguro continuar.
    /// </summary>
    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!_isDirty)
        {
            return true;
        }

        string? choice = await MessageDialog.ShowAsync(
            this,
            "Alterações não salvas",
            "Este modelo tem alterações não salvas. O que você quer fazer?",
            "Salvar", "Descartar", "Cancelar");

        switch (choice)
        {
            case "Salvar":
                await SaveAsync();
                return !_isDirty; // se cancelou o diálogo de caminho, ainda está sujo
            case "Descartar":
                return true;
            default:
                return false;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || !_isDirty)
        {
            return;
        }

        e.Cancel = true;
        _ = CloseAfterConfirmationAsync();
    }

    private async Task CloseAfterConfirmationAsync()
    {
        if (await ConfirmDiscardChangesAsync())
        {
            _forceClose = true;
            Close();
        }
    }

    /// <summary>
    /// Substitui a pasta de trabalho inteira — usado por Novo e Abrir. Limpa o
    /// histórico de desfazer e a área de transferência: um passo de desfazer que
    /// aponta para uma aba de outro documento não faz sentido nenhum.
    /// </summary>
    private void LoadWorkbook(Workbook workbook, string? path)
    {
        _engine = new CalculationEngine(workbook);

        Sheet.Engine = _engine;
        Sheet.SheetName = _engine.Workbook.Worksheets[0].Name;
        Sheet.ResetHistory();

        Tabs.Workbook = _engine.Workbook;
        Tabs.ActiveSheet = Sheet.SheetName;

        _currentFilePath = path;
        _isDirty = false;

        UpdateFormulaBar();
        UpdateTitle();
        Sheet.FocusGrid();
    }

    private void UpdateTitle()
    {
        string name = _currentFilePath is null ? "Sem título" : Path.GetFileNameWithoutExtension(_currentFilePath);
        string dirtyMark = _isDirty ? " •" : string.Empty;

        Title = $"{name}{dirtyMark} — Nordxcel";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.N when control:
                _ = NewAsync();
                break;

            case Key.O when control:
                _ = OpenAsync();
                break;

            // A condição do "Salvar Como" precisa vir antes da do "Salvar" simples,
            // senão Ctrl+Shift+S bateria primeiro em "Key.S when control" — o mesmo
            // cuidado de ordem que Ctrl+Shift+Z exigiu no atalho de refazer.
            case Key.S when control && shift:
                _ = SaveAsAsync();
                break;

            case Key.S when control:
                _ = SaveAsync();
                break;

            default:
                return;
        }

        e.Handled = true;
    }
}
