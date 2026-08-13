using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Nordxcel.Core.Calculation;
using Nordxcel.Core.Export;
using Nordxcel.Core.Model;
using Nordxcel.Core.Persistence;
using Nordxcel.Desktop.Controls;

namespace Nordxcel.Desktop;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType NordxcelFileType = new("Nordxcel") { Patterns = ["*.nxcl"] };
    private static readonly FilePickerFileType XlsxFileType = new("Excel") { Patterns = ["*.xlsx"] };

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

    // ------------------------------------------------------- tabela de dados

    private async void OnDataTableOneVariable(object? sender, RoutedEventArgs e) => await RunDataTableOneVariableAsync();

    private async void OnDataTableTwoVariables(object? sender, RoutedEventArgs e) => await RunDataTableTwoVariablesAsync();

    private async Task RunDataTableOneVariableAsync()
    {
        if (!TryGetOneVariableLayout(out CellAddress[] valueAddresses, out CellAddress[] resultAddresses, out string layoutError))
        {
            await MessageDialog.ShowAsync(this, "Tabela de Dados", layoutError, "OK");
            return;
        }

        string[]? input = await TextInputDialog.ShowAsync(
            this,
            "Tabela de Dados (1 Variável)",
            $"{valueAddresses.Length} valor(es) selecionado(s). Cada um substitui a célula de entrada, " +
            "recalcula o modelo e grava o resultado ao lado.",
            "Célula de entrada (ex.: Premissas!B2)",
            "Célula de saída (ex.: DCF!B10)");

        if (input is null)
        {
            return;
        }

        if (!TryResolveCell(input[0], out CellLocation inputCell, out string inputError))
        {
            await MessageDialog.ShowAsync(this, "Tabela de Dados", inputError, "OK");
            return;
        }

        if (!TryResolveCell(input[1], out CellLocation outputCell, out string outputError))
        {
            await MessageDialog.ShowAsync(this, "Tabela de Dados", outputError, "OK");
            return;
        }

        Worksheet sheet = _engine.Workbook[Sheet.SheetName];
        List<CellValue> values = valueAddresses.Select(sheet.GetValue).ToList();

        IReadOnlyList<CellValue> results = DataTableEngine.EvaluateSingleVariable(
            _engine.Workbook, inputCell, outputCell, values);

        var written = new Dictionary<CellAddress, CellValue>();

        for (int i = 0; i < resultAddresses.Length; i++)
        {
            written[resultAddresses[i]] = results[i];
        }

        Sheet.ApplyDataTableResults("Tabela de Dados", written);
    }

    private async Task RunDataTableTwoVariablesAsync()
    {
        if (!TryGetTwoVariableLayout(
                out CellAddress[] horizontalHeaderAddresses,
                out CellAddress[] verticalHeaderAddresses,
                out (CellAddress Address, int Row, int Column)[] interior,
                out string layoutError))
        {
            await MessageDialog.ShowAsync(this, "Tabela de Dados", layoutError, "OK");
            return;
        }

        string[]? input = await TextInputDialog.ShowAsync(
            this,
            "Tabela de Dados (2 Variáveis)",
            $"{verticalHeaderAddresses.Length} valor(es) na primeira coluna, {horizontalHeaderAddresses.Length} na primeira linha da seleção.",
            "Célula de entrada para os valores da coluna à esquerda",
            "Célula de entrada para os valores da linha de cima",
            "Célula de saída (ex.: DCF!B10)");

        if (input is null)
        {
            return;
        }

        if (!TryResolveCell(input[0], out CellLocation verticalInputCell, out string verticalError))
        {
            await MessageDialog.ShowAsync(this, "Tabela de Dados", verticalError, "OK");
            return;
        }

        if (!TryResolveCell(input[1], out CellLocation horizontalInputCell, out string horizontalError))
        {
            await MessageDialog.ShowAsync(this, "Tabela de Dados", horizontalError, "OK");
            return;
        }

        if (!TryResolveCell(input[2], out CellLocation outputCell, out string outputError))
        {
            await MessageDialog.ShowAsync(this, "Tabela de Dados", outputError, "OK");
            return;
        }

        Worksheet sheet = _engine.Workbook[Sheet.SheetName];
        List<CellValue> verticalValues = verticalHeaderAddresses.Select(sheet.GetValue).ToList();
        List<CellValue> horizontalValues = horizontalHeaderAddresses.Select(sheet.GetValue).ToList();

        CellValue[,] results = DataTableEngine.EvaluateTwoVariable(
            _engine.Workbook,
            verticalInputCell, verticalValues,
            horizontalInputCell, horizontalValues,
            outputCell);

        var written = new Dictionary<CellAddress, CellValue>();

        foreach ((CellAddress address, int row, int column) in interior)
        {
            written[address] = results[row, column];
        }

        Sheet.ApplyDataTableResults("Tabela de Dados", written);
    }

    /// <summary>
    /// Aceita seleção de exatamente 2 colunas (valores à esquerda, resultado à
    /// direita, uma linha por valor) ou exatamente 2 linhas (valores em cima,
    /// resultado embaixo, uma coluna por valor). Colunas tem prioridade quando os
    /// dois se aplicam, como numa seleção 2×2.
    /// </summary>
    private bool TryGetOneVariableLayout(out CellAddress[] valueAddresses, out CellAddress[] resultAddresses, out string error)
    {
        CellRange range = Sheet.SelectionRange;
        error = string.Empty;

        // Clique num cabeçalho de coluna/linha seleciona a planilha inteira num
        // eixo (mais de um milhão de células) — sem essa guarda, "2 colunas" ou
        // "2 linhas" bateria certo e a Tabela de Dados tentaria materializar
        // e recalcular um valor por linha/coluna da planilha inteira.
        if (IsUnboundedSelection(range))
        {
            valueAddresses = [];
            resultAddresses = [];
            error = "A seleção não pode ser uma coluna ou linha inteira. Selecione só o intervalo com os valores.";
            return false;
        }

        if (range.ColumnCount == 2)
        {
            valueAddresses = new CellAddress[range.RowCount];
            resultAddresses = new CellAddress[range.RowCount];

            for (int i = 0; i < range.RowCount; i++)
            {
                int row = range.Start.Row + i;
                valueAddresses[i] = new CellAddress(row, range.Start.Column);
                resultAddresses[i] = new CellAddress(row, range.Start.Column + 1);
            }

            return true;
        }

        if (range.RowCount == 2)
        {
            valueAddresses = new CellAddress[range.ColumnCount];
            resultAddresses = new CellAddress[range.ColumnCount];

            for (int i = 0; i < range.ColumnCount; i++)
            {
                int column = range.Start.Column + i;
                valueAddresses[i] = new CellAddress(range.Start.Row, column);
                resultAddresses[i] = new CellAddress(range.Start.Row + 1, column);
            }

            return true;
        }

        valueAddresses = [];
        resultAddresses = [];
        error = "Selecione um intervalo com exatamente 2 colunas (valores à esquerda, resultado à direita) " +
                "ou exatamente 2 linhas (valores em cima, resultado embaixo) antes de abrir a Tabela de Dados.";
        return false;
    }

    /// <summary>
    /// Aceita seleção retangular com pelo menos 2 linhas e 2 colunas: a primeira
    /// linha (menos o canto) tem os valores horizontais, a primeira coluna (menos
    /// o canto) tem os valores verticais, e o interior recebe o resultado.
    /// </summary>
    private bool TryGetTwoVariableLayout(
        out CellAddress[] horizontalHeaderAddresses,
        out CellAddress[] verticalHeaderAddresses,
        out (CellAddress Address, int Row, int Column)[] interior,
        out string error)
    {
        CellRange range = Sheet.SelectionRange;
        error = string.Empty;

        if (IsUnboundedSelection(range))
        {
            horizontalHeaderAddresses = [];
            verticalHeaderAddresses = [];
            interior = [];
            error = "A seleção não pode ser uma coluna ou linha inteira. Selecione só o intervalo da tabela.";
            return false;
        }

        if (range.ColumnCount < 2 || range.RowCount < 2)
        {
            horizontalHeaderAddresses = [];
            verticalHeaderAddresses = [];
            interior = [];
            error = "Selecione um intervalo retangular com pelo menos 2 linhas e 2 colunas: valores da linha de cima " +
                    "na primeira linha, valores da coluna à esquerda na primeira coluna, e o resultado no interior.";
            return false;
        }

        int columnCount = range.ColumnCount - 1;
        int rowCount = range.RowCount - 1;

        horizontalHeaderAddresses = new CellAddress[columnCount];

        for (int c = 0; c < columnCount; c++)
        {
            horizontalHeaderAddresses[c] = new CellAddress(range.Start.Row, range.Start.Column + 1 + c);
        }

        verticalHeaderAddresses = new CellAddress[rowCount];

        for (int r = 0; r < rowCount; r++)
        {
            verticalHeaderAddresses[r] = new CellAddress(range.Start.Row + 1 + r, range.Start.Column);
        }

        interior = new (CellAddress, int, int)[rowCount * columnCount];
        int index = 0;

        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < columnCount; c++)
            {
                interior[index++] = (new CellAddress(range.Start.Row + 1 + r, range.Start.Column + 1 + c), r, c);
            }
        }

        return true;
    }

    /// <summary>Verdadeiro para seleção de coluna ou linha inteira — mesmo teste que <see cref="Nordxcel.Core.Editing.RangeEditing"/> usa para não materializar a planilha inteira.</summary>
    private static bool IsUnboundedSelection(CellRange range) =>
        range.RowCount >= CellAddress.MaxRows || range.ColumnCount >= CellAddress.MaxColumns;

    /// <summary>Interpreta uma referência de célula digitada no diálogo, usando a aba ativa quando nenhuma é indicada.</summary>
    private bool TryResolveCell(string text, out CellLocation location, out string error)
    {
        location = default;
        error = string.Empty;

        if (!CellReference.TryParse(text, out CellReference reference))
        {
            error = $"'{text}' não é uma referência de célula válida (ex.: B2 ou Premissas!B2).";
            return false;
        }

        string sheetName = reference.Sheet ?? Sheet.SheetName;

        if (!_engine.Workbook.ContainsWorksheet(sheetName))
        {
            error = $"A aba '{sheetName}' não existe.";
            return false;
        }

        location = new CellLocation(sheetName, reference.Address);
        return true;
    }

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

    // -------------------------------------------------- exportação para .xlsx

    private async void OnExportXlsx(object? sender, RoutedEventArgs e) => await ExportXlsxAsync();

    private async Task ExportXlsxAsync()
    {
        string? path = await PromptExportPathAsync();

        if (path is null)
        {
            return;
        }

        try
        {
            XlsxExporter.Export(_engine.Workbook, path);
        }
        catch (XlsxExportException exception)
        {
            await MessageDialog.ShowAsync(this, "Não foi possível exportar", exception.Message, "OK");
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Não foi possível exportar", exception.Message, "OK");
            return;
        }

        await MessageDialog.ShowAsync(this, "Exportação concluída", $"Modelo exportado para {Path.GetFileName(path)}.", "OK");
    }

    private async Task<string?> PromptExportPathAsync()
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar para .xlsx",
            SuggestedFileName = _currentFilePath is null ? "Modelo" : Path.GetFileNameWithoutExtension(_currentFilePath),
            DefaultExtension = "xlsx",
            FileTypeChoices = [XlsxFileType],
        });

        return file?.TryGetLocalPath();
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
