using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nordxcel.Core.Calculation;
using Nordxcel.Core.Editing;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Layout;
using Nordxcel.Core.Model;

// O Avalonia tem um NavigationDirection, HorizontalAlignment e VerticalAlignment
// próprios (de layout de UI), diferentes dos de estilo de célula do Core.
using NavigationDirection = Nordxcel.Core.Layout.NavigationDirection;
using CoreHorizontalAlignment = Nordxcel.Core.Model.Styling.HorizontalAlignment;
using CoreVerticalAlignment = Nordxcel.Core.Model.Styling.VerticalAlignment;
using RgbColor = Nordxcel.Core.Model.Styling.RgbColor;
using CellStyle = Nordxcel.Core.Model.Styling.CellStyle;
using BorderLineStyle = Nordxcel.Core.Model.Styling.BorderLineStyle;

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// A planilha completa: grade, barras de rolagem e o editor que aparece por cima
/// da célula durante a digitação.
/// </summary>
public sealed class SpreadsheetView : UserControl
{
    private readonly SpreadsheetCanvas _canvas = new();
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical };
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _editor;
    private readonly Panel _surface = new();

    private bool _syncing;
    private bool _editing;
    private bool _editStartedByTyping;

    /// <summary>Só depois que o editor recebe o foco é que perder o foco significa confirmar.</summary>
    private bool _editorHasFocus;

    private CellAddress _editingAddress;

    public SpreadsheetView()
    {
        IBrush editorBorderBrush = new SolidColorBrush(Color.FromRgb(33, 115, 70)).ToImmutable();
        var editorBorderThickness = new Thickness(2);

        _editor = new TextBox
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            AcceptsReturn = false,
            BorderThickness = editorBorderThickness,
            BorderBrush = editorBorderBrush,
            Background = Brushes.White,
            Padding = new Thickness(2, 0, 2, 0),
            FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"),
            FontSize = 11,
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusAdorner = null,
            // O tema também define um tamanho mínimo (pensado para toque/acessibilidade,
            // maior que uma linha de planilha) via MinWidth/MinHeight — sem zerar os
            // dois, o controle não encolhe abaixo desse mínimo por mais que Width/Height
            // sejam ajustados para caber exatamente na célula.
            MinWidth = 0,
            MinHeight = 0,
        };

        // O TextBox do FluentTheme troca cor e espessura da borda por dentro do
        // próprio template a cada pseudo-classe (foco, passar o mouse, etc.) —
        // não lê a propriedade BorderBrush/BorderThickness de novo nesses estados,
        // troca pelo valor destes recursos. Sem sobrescrever aqui, a borda muda
        // pra azul (cor de destaque do tema) e de espessura assim que o editor
        // ganha foco, que é o tempo todo enquanto se digita.
        foreach (string key in new[]
                 {
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
                     "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
                 })
        {
            _editor.Resources[key] = editorBorderBrush;
        }

        foreach (string key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver",
                     "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                 })
        {
            _editor.Resources[key] = Brushes.White;
        }

        _editor.Resources["TextControlBorderThemeThickness"] = editorBorderThickness;
        _editor.Resources["TextControlBorderThemeThicknessFocused"] = editorBorderThickness;

        _editor.KeyDown += OnEditorKeyDown;
        _editor.GotFocus += (_, _) => _editorHasFocus = true;
        _editor.LostFocus += (_, _) =>
        {
            if (!_editorHasFocus)
            {
                return;
            }

            _editorHasFocus = false;
            Commit(NavigationDirection.Down, moveAfterCommit: false);
        };

        _canvas.ScrollChanged += (_, _) => SyncScrollBars();
        _canvas.SelectionChanged += OnCanvasSelectionChanged;
        _canvas.EditRequested += OnEditRequested;
        _canvas.ContentChanged += (_, _) => RaiseChanged();
        _canvas.CommitRequested += (_, direction) => Commit(direction);
        _canvas.CancelRequested += (_, _) => CancelEdit();
        _canvas.LayoutChanged += (_, _) => SyncScrollBars();

        _canvas.CopyRequested += (_, _) => Copy();
        _canvas.CutRequested += (_, _) => Cut();
        _canvas.PasteRequested += (_, _) => Paste();
        _canvas.UndoRequested += (_, _) => Undo();
        _canvas.RedoRequested += (_, _) => Redo();
        _canvas.CellsCleared += (_, edits) => PushUndo("Excluir", edits);

        _vertical.Scroll += OnVerticalScroll;
        _horizontal.Scroll += OnHorizontalScroll;

        _surface.Children.Add(_canvas);
        _surface.Children.Add(_editor);

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("*,Auto"),
        };

        layout.Children.Add(_surface);
        Grid.SetRow(_surface, 0);
        Grid.SetColumn(_surface, 0);

        layout.Children.Add(_vertical);
        Grid.SetRow(_vertical, 0);
        Grid.SetColumn(_vertical, 1);

        layout.Children.Add(_horizontal);
        Grid.SetRow(_horizontal, 1);
        Grid.SetColumn(_horizontal, 0);

        Content = layout;
    }

    /// <summary>Disparado quando a seleção muda, para a barra de fórmulas acompanhar.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Disparado quando o conteúdo da planilha muda.</summary>
    public event EventHandler? ContentChanged;

    public SpreadsheetCanvas Canvas => _canvas;

    public CalculationEngine? Engine
    {
        get => _canvas.Engine;
        set
        {
            _canvas.Engine = value;
            SyncScrollBars();
            OnCanvasSelectionChanged(this, EventArgs.Empty);
        }
    }

    public string SheetName
    {
        get => _canvas.SheetName;
        set
        {
            CancelEdit();
            _canvas.SheetName = value;
            _canvas.ScrollX = 0d;
            _canvas.ScrollY = 0d;
            SyncScrollBars();
            OnCanvasSelectionChanged(this, EventArgs.Empty);
        }
    }

    public bool IsEditing => _editing;

    /// <summary>Referência da seleção, como aparece na caixa de nome: <c>B7</c> ou <c>B2:D10</c>.</summary>
    public string SelectionReference => _canvas.Selection.ToReferenceText();

    /// <summary>Retângulo selecionado, já normalizado. É o que a Tabela de Dados lê para achar cabeçalho e área de resultado.</summary>
    public CellRange SelectionRange => _canvas.Selection.Range;

    /// <summary>Conteúdo editável da célula ativa, do jeito que a barra de fórmulas mostra.</summary>
    public string ActiveCellText => CellInput.ToEditText(_canvas.ActiveCell);

    public void FocusGrid() => _canvas.Focus();

    /// <inheritdoc cref="SpreadsheetCanvas.NotifySheetRenamed"/>
    public void NotifySheetRenamed(string oldName, string newName) => _canvas.NotifySheetRenamed(oldName, newName);

    /// <summary>
    /// Limpa o histórico de desfazer/refazer e a área de transferência — usado ao
    /// trocar de documento inteiro (novo/abrir). Um passo de desfazer ou um
    /// recorte pendente que aponta para a aba de outro documento não faz
    /// sentido nenhum depois da troca.
    /// </summary>
    public void ResetHistory()
    {
        _canvas.Undo.Clear();
        _clipboard = null;
    }

    public void Refresh()
    {
        _canvas.Refresh();
        SyncScrollBars();
    }

    /// <summary>Grava um texto na célula ativa. É por onde a barra de fórmulas confirma.</summary>
    public void CommitActiveCell(string text)
    {
        Write(_canvas.Selection.Active, text);
        MoveAfterCommit(NavigationDirection.Down);
        RaiseChanged();
    }

    // ------------------------------------------------------ painéis congelados

    public bool HasFrozenPanes => _canvas.HasFrozenPanes;

    /// <summary>Congela as linhas acima e as colunas à esquerda da célula ativa.</summary>
    public void FreezeAtSelection()
    {
        _canvas.FreezeAtSelection();
        FocusGrid();
    }

    public void FreezeTopRow()
    {
        _canvas.FreezeTopRow();
        FocusGrid();
    }

    public void FreezeFirstColumn()
    {
        _canvas.FreezeFirstColumn();
        FocusGrid();
    }

    public void UnfreezePanes()
    {
        _canvas.UnfreezePanes();
        FocusGrid();
    }

    // ----------------------------------------------------------- formatação

    /// <summary>Estilo da célula ativa — o que a barra de formatação reflete ao trocar de seleção.</summary>
    public CellStyle ActiveStyle => _canvas.ActiveCell.Style;

    /// <summary>Formato de número da célula ativa.</summary>
    public string? ActiveNumberFormat => _canvas.ActiveCell.NumberFormat;

    /// <summary>Aplica uma mudança de estilo a toda a seleção.</summary>
    public void ApplyStyle(Func<CellStyle, CellStyle> transform) => ApplyStyle("Formatar", transform);

    private void ApplyStyle(string description, Func<CellStyle, CellStyle> transform)
    {
        Worksheet? sheet = CurrentSheet();

        if (sheet is null)
        {
            return;
        }

        IReadOnlyList<CellEdit> edits = RangeEditing.ApplyStyle(sheet, _canvas.Selection.Range, transform);
        PushUndo(description, edits);
        AfterFormatChange();
    }

    /// <summary>
    /// Alterna negrito/itálico/sublinhado com base no estado da célula ativa: se
    /// ela já está, a seleção toda desliga; senão, a seleção toda liga — igual ao
    /// Excel, que não trata cada célula da seleção de forma independente.
    /// <para>
    /// O valor alvo é lido <b>antes</b> de entrar no laço de células, não dentro
    /// do transform. A célula ativa costuma ser a primeira do intervalo a ser
    /// processada; se <c>ActiveStyle</c> fosse reavaliado a cada célula, a partir
    /// da segunda ele já enxergaria o valor que a própria primeira célula acabou
    /// de gravar, invertendo o alvo de volta ao original a cada duas células.
    /// </para>
    /// </summary>
    public void ToggleBold()
    {
        bool target = !ActiveStyle.Bold;
        ApplyStyle("Negrito", s => s with { Bold = target });
    }

    public void ToggleItalic()
    {
        bool target = !ActiveStyle.Italic;
        ApplyStyle("Itálico", s => s with { Italic = target });
    }

    public void ToggleUnderline()
    {
        bool target = !ActiveStyle.Underline;
        ApplyStyle("Sublinhado", s => s with { Underline = target });
    }

    /// <summary><c>null</c> volta para a cor automática do sistema azul/preto/verde.</summary>
    public void SetFontColor(RgbColor? color) => ApplyStyle("Cor da fonte", s => s with { FontColor = color });

    /// <summary><c>null</c> remove o preenchimento.</summary>
    public void SetFillColor(RgbColor? color) => ApplyStyle("Cor de preenchimento", s => s with { BackgroundColor = color });

    public void SetFontFamily(string family) => ApplyStyle("Fonte", s => s with { FontFamily = family });

    public void SetFontSize(double size) => ApplyStyle("Tamanho da fonte", s => s with { FontSize = size });

    public void SetHorizontalAlignment(CoreHorizontalAlignment alignment) =>
        ApplyStyle("Alinhamento", s => s with { HorizontalAlignment = alignment });

    public void SetVerticalAlignment(CoreVerticalAlignment alignment) =>
        ApplyStyle("Alinhamento", s => s with { VerticalAlignment = alignment });

    public void ApplyBorderPreset(BorderPreset preset, BorderLineStyle style, RgbColor color)
    {
        Worksheet? sheet = CurrentSheet();

        if (sheet is null)
        {
            return;
        }

        IReadOnlyList<CellEdit> edits = BorderEditing.Apply(sheet, _canvas.Selection.Range, preset, style, color);
        PushUndo("Bordas", edits);
        AfterFormatChange();
    }

    /// <summary><c>null</c> mask volta ao formato geral.</summary>
    public void SetNumberFormat(string? mask)
    {
        Worksheet? sheet = CurrentSheet();

        if (sheet is null)
        {
            return;
        }

        IReadOnlyList<CellEdit> edits = RangeEditing.ApplyNumberFormat(sheet, _canvas.Selection.Range, mask);
        PushUndo("Formato de número", edits);
        AfterFormatChange();
    }

    /// <summary>Botão de aumentar/diminuir casas decimais — cada célula mantém a própria máscara, só muda a precisão.</summary>
    public void StepDecimals(bool increase)
    {
        Worksheet? sheet = CurrentSheet();

        if (sheet is null)
        {
            return;
        }

        IReadOnlyList<CellEdit> edits = RangeEditing.Apply(sheet, _canvas.Selection.Range, cell => cell with
        {
            NumberFormat = increase
                ? StandardNumberFormats.IncreaseDecimals(cell.NumberFormat)
                : StandardNumberFormats.DecreaseDecimals(cell.NumberFormat),
        });

        PushUndo("Casas decimais", edits);
        AfterFormatChange();
    }

    private Worksheet? CurrentSheet() =>
        _canvas.Engine is { } engine && !string.IsNullOrEmpty(_canvas.SheetName) && engine.Workbook.TryGetWorksheet(_canvas.SheetName, out Worksheet? sheet)
            ? sheet
            : null;

    /// <summary>
    /// Formatação não muda valor calculado nenhum, então não passa pelo motor de
    /// recálculo — só redesenha e avisa que o documento mudou.
    /// </summary>
    private void AfterFormatChange()
    {
        _canvas.Refresh();
        SyncScrollBars();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PushUndo(string description, IReadOnlyList<CellEdit> edits)
    {
        if (edits.Count > 0)
        {
            _canvas.Undo.Push(new UndoStep(description, edits));
        }
    }

    // --------------------------------------------------- recortar, copiar, colar

    private ClipboardContent? _clipboard;

    /// <summary>Verdadeiro quando há algo para colar — útil para habilitar/desabilitar um item de menu.</summary>
    public bool HasClipboardContent => _clipboard is not null;

    public void Copy()
    {
        Worksheet? sheet = CurrentSheet();

        if (sheet is null)
        {
            return;
        }

        _clipboard = ClipboardContent.Capture(sheet, _canvas.Selection.Range, isCut: false);
    }

    public void Cut()
    {
        Worksheet? sheet = CurrentSheet();

        if (sheet is null)
        {
            return;
        }

        _clipboard = ClipboardContent.Capture(sheet, _canvas.Selection.Range, isCut: true);
    }

    public void Paste()
    {
        CalculationEngine? engine = _canvas.Engine;

        if (_clipboard is null || engine is null || string.IsNullOrEmpty(_canvas.SheetName))
        {
            return;
        }

        ClipboardContent clipboard = _clipboard;
        CellAddress targetAnchor = _canvas.Selection.Active;

        IReadOnlyList<CellEdit> pasted = clipboard.ComputePaste(engine.Workbook, _canvas.SheetName, targetAnchor);

        // A célula pode ter fórmula: grava através do motor, e não direto na aba,
        // para ela entrar no grafo de dependências e ser calculada. ComputePaste
        // não escreve nada sozinho — é responsabilidade de quem aplica o resultado.
        engine.AutoRecalculate = false;

        foreach (CellEdit edit in pasted)
        {
            engine.SetCell(edit.Location, edit.After);
        }

        engine.AutoRecalculate = true;
        engine.Recalculate();

        PushUndo(clipboard.IsCut ? "Recortar" : "Colar", pasted);

        if (clipboard.IsCut)
        {
            FinishCut(clipboard, engine, targetAnchor);
        }

        if (pasted.Count > 0)
        {
            SelectPastedRange(clipboard, targetAnchor);
        }

        AfterFormatChange();
    }

    /// <summary>
    /// Termina o recorte: apaga a origem e esvazia a área de transferência, para
    /// um segundo Ctrl+V não colar de novo — igual ao Excel, que só deixa colar
    /// um recorte uma vez.
    /// </summary>
    private void FinishCut(ClipboardContent clipboard, CalculationEngine engine, CellAddress targetAnchor)
    {
        Worksheet? sourceSheet = engine.Workbook.TryGetWorksheet(clipboard.SourceSheet, out Worksheet? sheet) ? sheet : null;

        if (sourceSheet is not null)
        {
            // Se colou na mesma aba de onde recortou e o destino encosta na
            // origem, as células que já receberam o conteúdo colado não podem
            // ser apagadas de novo — senão o "mover" perderia o próprio conteúdo
            // que acabou de chegar.
            var pastedOver = string.Equals(clipboard.SourceSheet, _canvas.SheetName, StringComparison.OrdinalIgnoreCase)
                ? new HashSet<CellAddress>(clipboard.DestinationAddresses(targetAnchor))
                : [];

            engine.AutoRecalculate = false;

            foreach (CellAddress address in clipboard.SourceRange.Addresses())
            {
                if (!pastedOver.Contains(address) && !sourceSheet.GetCell(address).IsEmpty)
                {
                    engine.ClearCell(new CellLocation(clipboard.SourceSheet, address));
                }
            }

            engine.AutoRecalculate = true;
            engine.Recalculate();
        }

        _clipboard = null;
    }

    private void SelectPastedRange(ClipboardContent clipboard, CellAddress targetAnchor)
    {
        var farCorner = new CellAddress(
            Math.Min(targetAnchor.Row + clipboard.RowCount - 1, CellAddress.MaxRows - 1),
            Math.Min(targetAnchor.Column + clipboard.ColumnCount - 1, CellAddress.MaxColumns - 1));

        _canvas.Selection.MoveTo(targetAnchor);
        _canvas.Selection.ExtendTo(farCorner);
    }

    /// <summary>
    /// "Colar Valores": mesmo clipboard interno do Ctrl+V, mas cada célula colada
    /// vira valor literal — nunca fórmula. Ainda passa pelo motor (não pelo
    /// <see cref="RangeEditing"/> direto): uma célula colada pode estar
    /// substituindo uma fórmula antiga, que precisa sair do grafo de
    /// dependências, e só <c>CalculationEngine.SetCell</c> faz esse desligamento.
    /// <para>
    /// Ao contrário de <see cref="Paste"/>, não termina um recorte pendente — colar
    /// só o valor de uma célula recortada não "move" nada, então a origem fica
    /// intacta de propósito, o lado mais seguro de errar.
    /// </para>
    /// </summary>
    public void PasteValues()
    {
        CalculationEngine? engine = _canvas.Engine;

        if (_clipboard is null || engine is null || string.IsNullOrEmpty(_canvas.SheetName))
        {
            return;
        }

        CellAddress targetAnchor = _canvas.Selection.Active;
        IReadOnlyList<CellEdit> pasted = _clipboard.ComputePasteValues(engine.Workbook, _canvas.SheetName, targetAnchor);

        engine.AutoRecalculate = false;

        foreach (CellEdit edit in pasted)
        {
            engine.SetCell(edit.Location, edit.After);
        }

        engine.AutoRecalculate = true;
        engine.Recalculate();

        PushUndo("Colar Valores", pasted);
        AfterFormatChange();
    }

    /// <summary>
    /// "Colar Formatação": só estilo e formato de número viajam do clipboard, o
    /// conteúdo do destino não muda — não muda valor calculado nenhum, então
    /// (como <see cref="ApplyStyle(string,System.Func{CellStyle,CellStyle})"/>)
    /// não precisa passar pelo motor de cálculo.
    /// </summary>
    public void PasteFormat()
    {
        CalculationEngine? engine = _canvas.Engine;
        Worksheet? sheet = CurrentSheet();

        if (_clipboard is null || engine is null || sheet is null)
        {
            return;
        }

        IReadOnlyList<CellEdit> edits = _clipboard.ComputePasteFormat(engine.Workbook, _canvas.SheetName, _canvas.Selection.Active);

        foreach (CellEdit edit in edits)
        {
            sheet.SetCell(edit.Location.Address, edit.After);
        }

        PushUndo("Colar Formatação", edits);
        AfterFormatChange();
    }

    // -------------------------------------------------- pincel de formatação

    private CellStyle? _copiedFormatStyle;
    private string? _copiedFormatNumberFormat;
    private bool _pendingFormatPaint;

    /// <summary>Verdadeiro enquanto o pincel está armado, esperando o próximo clique na grade.</summary>
    public bool HasPendingFormatPaint => _pendingFormatPaint;

    /// <summary>
    /// Arma o pincel de formatação com o estilo da célula ativa. A aplicação em
    /// si acontece na próxima mudança de seleção — ver <see cref="OnCanvasSelectionChanged"/>
    /// — não precisa de nenhuma captura de mouse nova, só reage ao evento que a
    /// grade já dispara sozinha a cada clique.
    /// </summary>
    public void CopyFormat()
    {
        _copiedFormatStyle = ActiveStyle;
        _copiedFormatNumberFormat = ActiveNumberFormat;
        _pendingFormatPaint = true;
    }

    private void ApplyPendingFormatPaint()
    {
        _pendingFormatPaint = false;

        if (_copiedFormatStyle is not { } style)
        {
            return;
        }

        Worksheet? sheet = CurrentSheet();

        if (sheet is null)
        {
            return;
        }

        string? numberFormat = _copiedFormatNumberFormat;
        IReadOnlyList<CellEdit> edits = RangeEditing.Apply(
            sheet,
            _canvas.Selection.Range,
            cell => cell with { Style = style, NumberFormat = numberFormat });

        PushUndo("Pincel de Formatação", edits);
        AfterFormatChange();
    }

    // -------------------------------------------------------- tabela de dados

    /// <summary>
    /// Grava os resultados calculados por <see cref="DataTableEngine"/> na aba
    /// atual, como um único passo de desfazer.
    /// <para>
    /// A célula recebe um valor literal, não uma fórmula viva — é a mesma opção
    /// que a maioria dos modelistas de IB/PE já usa na prática no Excel real
    /// ("Automático exceto tabelas de dados"), porque uma tabela de sensibilidade
    /// recalculando a cada tecla junto com o modelo inteiro fica lenta demais em
    /// planilhas grandes. A cor da fonte é travada em preto — convenção de
    /// "calculado" — só quando a célula ainda não tem cor manual, para não
    /// herdar o azul automático de entrada manual.
    /// </para>
    /// </summary>
    public void ApplyDataTableResults(string description, IReadOnlyDictionary<CellAddress, CellValue> results)
    {
        CalculationEngine? engine = _canvas.Engine;

        if (engine is null || string.IsNullOrEmpty(_canvas.SheetName) || results.Count == 0)
        {
            return;
        }

        Worksheet sheet = engine.Workbook[_canvas.SheetName];
        var edits = new List<CellEdit>();

        engine.AutoRecalculate = false;

        foreach ((CellAddress address, CellValue value) in results)
        {
            Cell before = sheet.GetCell(address);
            Cell after = before with
            {
                Formula = null,
                Value = value,
                Style = before.Style.FontColor is null
                    ? before.Style with { FontColor = CellColorClassifier.FormulaColor }
                    : before.Style,
            };

            if (after == before)
            {
                continue;
            }

            var location = new CellLocation(_canvas.SheetName, address);
            engine.SetCell(location, after);
            edits.Add(new CellEdit(location, before, after));
        }

        engine.AutoRecalculate = true;
        engine.Recalculate();

        PushUndo(description, edits);
        AfterFormatChange();
    }

    // ------------------------------------------------------------ desfazer/refazer

    public bool CanUndo => _canvas.Undo.CanUndo;

    public bool CanRedo => _canvas.Undo.CanRedo;

    public string? NextUndoDescription => _canvas.Undo.NextUndoDescription;

    public string? NextRedoDescription => _canvas.Undo.NextRedoDescription;

    public void Undo()
    {
        UndoStep? step = _canvas.Undo.Undo();

        if (step is null)
        {
            return;
        }

        GoToStepSheet(step);
        _canvas.ApplyEdits(step.Edits, useAfter: false);
        AfterFormatChange();
    }

    public void Redo()
    {
        UndoStep? step = _canvas.Undo.Redo();

        if (step is null)
        {
            return;
        }

        GoToStepSheet(step);
        _canvas.ApplyEdits(step.Edits, useAfter: true);
        AfterFormatChange();
    }

    /// <summary>
    /// Troca para a aba de onde o passo veio, caso o usuário tenha navegado para
    /// outro lugar entre a ação original e o Ctrl+Z — senão desfazer aconteceria
    /// "invisível", numa aba que não é a que está na tela.
    /// </summary>
    private void GoToStepSheet(UndoStep step)
    {
        if (step.Edits.Count == 0)
        {
            return;
        }

        string sheetName = step.Edits[0].Location.SheetName;

        if (!string.Equals(sheetName, _canvas.SheetName, StringComparison.OrdinalIgnoreCase))
        {
            SheetName = sheetName;
        }
    }

    // -------------------------------------------------------------- edição

    /// <summary>
    /// Começa a editar a célula ativa já com <c>=NOMEFUNÇÃO(</c> digitado, cursor
    /// pronto pra continuar — o que a aba Fórmulas do ribbon usa em cada botão de
    /// função, igual ao Excel abrir o assistente de função. Reaproveita
    /// <see cref="OnEditRequested"/> por inteiro; só monta o evento sintético que
    /// a digitação normal dispararia sozinha.
    /// </summary>
    public void StartFunctionEntry(string functionName) =>
        OnEditRequested(this, new CellEditRequestedEventArgs(_canvas.Selection.Active, $"={functionName}("));

    private void OnEditRequested(object? sender, CellEditRequestedEventArgs e)
    {
        if (_editing)
        {
            // Tecla digitada antes de o editor pegar o foco: em vez de perdê-la,
            // acrescenta ao que já está escrito.
            if (e.InitialText is { Length: > 0 })
            {
                _editor.Text += e.InitialText;
                _editor.CaretIndex = _editor.Text.Length;
            }

            return;
        }

        Rect? rect = _canvas.GetCellScreenRect(e.Address);

        if (rect is null)
        {
            return;
        }

        _editing = true;
        _canvas.IsEditing = true;
        _editStartedByTyping = e.StartedByTyping;
        _editingAddress = e.Address;

        _editor.Text = e.InitialText ?? CellInput.ToEditText(_canvas.ActiveCell);

        PositionEditor(rect.Value);
        _editor.IsVisible = true;

        // O foco precisa esperar o controle existir de fato na árvore visual. Pedir
        // foco no mesmo passo em que ele fica visível não pega, e a tecla seguinte
        // voltaria para a grade em vez de entrar no editor.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_editing)
                {
                    return;
                }

                _editor.Focus();
                _editor.CaretIndex = _editor.Text?.Length ?? 0;
            },
            DispatcherPriority.Input);
    }

    private void PositionEditor(Rect cell)
    {
        // A borda interna de um TextBox é um Border comum, desenhado inteiro
        // dentro da área alocada ao controle (ao contrário de um traço desenhado
        // no Canvas, que fica centralizado em cima do limite e vaza pra fora) —
        // então basta encaixar exatamente no retângulo da célula.
        _editor.Margin = new Thickness(cell.X, cell.Y, 0d, 0d);
        _editor.Width = Math.Max(cell.Width, 60d);
        _editor.Height = cell.Height;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CancelEdit();
                e.Handled = true;
                break;

            case Key.Enter:
                Commit(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NavigationDirection.Up : NavigationDirection.Down);
                e.Handled = true;
                break;

            case Key.Tab:
                Commit(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NavigationDirection.Left : NavigationDirection.Right);
                e.Handled = true;
                break;

            // Setas confirmam quando a edição começou por digitação; depois de F2 elas
            // andam com o cursor dentro do texto. É exatamente a distinção do Excel.
            case Key.Up when _editStartedByTyping:
                Commit(NavigationDirection.Up);
                e.Handled = true;
                break;

            case Key.Down when _editStartedByTyping:
                Commit(NavigationDirection.Down);
                e.Handled = true;
                break;

            case Key.Left when _editStartedByTyping:
                Commit(NavigationDirection.Left);
                e.Handled = true;
                break;

            case Key.Right when _editStartedByTyping:
                Commit(NavigationDirection.Right);
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void Commit(NavigationDirection direction, bool moveAfterCommit = true)
    {
        if (!_editing)
        {
            return;
        }

        string text = _editor.Text ?? string.Empty;

        _editing = false;
        _canvas.IsEditing = false;
        _editorHasFocus = false;
        _editor.IsVisible = false;

        Write(_editingAddress, text);

        if (moveAfterCommit)
        {
            MoveAfterCommit(direction);
        }

        _canvas.Focus();
        RaiseChanged();
    }

    private void CancelEdit()
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        _canvas.IsEditing = false;
        _editorHasFocus = false;
        _editor.IsVisible = false;
        _canvas.Focus();
    }

    private void Write(CellAddress address, string text)
    {
        CalculationEngine? engine = _canvas.Engine;

        if (engine is null || string.IsNullOrEmpty(_canvas.SheetName))
        {
            return;
        }

        var location = new CellLocation(_canvas.SheetName, address);
        Cell before = engine.Workbook[_canvas.SheetName].GetCell(address);
        Cell after;

        try
        {
            after = CellInput.Parse(text, before);
            engine.SetCell(location, after);
        }
        catch (Nordxcel.Core.Formulas.FormulaSyntaxException)
        {
            // Fórmula malformada não altera a planilha; guarda o texto como está para
            // o usuário ver o que digitou e corrigir.
            after = before with { Formula = null, Value = CellValue.Text(text) };
            engine.SetCell(location, after);
        }

        if (after != before)
        {
            PushUndo("Digitar", [new CellEdit(location, before, after)]);
        }

        _canvas.Refresh();
    }

    private void MoveAfterCommit(NavigationDirection direction)
    {
        CellAddress next = SelectionNavigator.Step(_canvas.Selection.Active, direction);

        _canvas.Selection.MoveTo(next);
        _canvas.ScrollIntoView(next);
        _canvas.Refresh();

        OnCanvasSelectionChanged(this, EventArgs.Empty);
    }

    private void RaiseChanged()
    {
        SyncScrollBars();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCanvasSelectionChanged(object? sender, EventArgs e)
    {
        if (_editing)
        {
            CancelEdit();
        }

        if (_pendingFormatPaint)
        {
            ApplyPendingFormatPaint();
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------- rolagem

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size result = base.ArrangeOverride(finalSize);
        SyncScrollBars();

        return result;
    }

    private void OnVerticalScroll(object? sender, ScrollEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _canvas.ScrollY = _vertical.Value;
        RepositionEditor();
    }

    private void OnHorizontalScroll(object? sender, ScrollEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _canvas.ScrollX = _horizontal.Value;
        RepositionEditor();
    }

    private void RepositionEditor()
    {
        if (!_editing)
        {
            return;
        }

        Rect? rect = _canvas.GetCellScreenRect(_editingAddress);

        if (rect is not null)
        {
            PositionEditor(rect.Value);
        }
    }

    private void SyncScrollBars()
    {
        (double extentWidth, double extentHeight) = _canvas.GetScrollExtent();

        _syncing = true;

        try
        {
            // Com painel congelado, a barra não rola de volta o suficiente para
            // reexibir o que já está sempre visível no painel fixo.
            Configure(_vertical, _canvas.MinScrollY, extentHeight, _canvas.ViewportHeight, _canvas.ScrollY);
            Configure(_horizontal, _canvas.MinScrollX, extentWidth, _canvas.ViewportWidth, _canvas.ScrollX);
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void Configure(ScrollBar bar, double minimum, double extent, double viewport, double value)
    {
        double maximum = Math.Max(minimum, extent - viewport);

        bar.Minimum = minimum;
        bar.Maximum = maximum;
        bar.ViewportSize = viewport;
        bar.LargeChange = Math.Max(viewport, 1d);
        bar.SmallChange = 20d;
        bar.Value = Math.Clamp(value, minimum, maximum);
        bar.IsEnabled = maximum > minimum;
    }
}
