using System;
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
        _editor = new TextBox
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            AcceptsReturn = false,
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Color.FromRgb(33, 115, 70)).ToImmutable(),
            Background = Brushes.White,
            Padding = new Thickness(2, 0, 2, 0),
            FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"),
            FontSize = 11,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

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

    /// <summary>Conteúdo editável da célula ativa, do jeito que a barra de fórmulas mostra.</summary>
    public string ActiveCellText => CellInput.ToEditText(_canvas.ActiveCell);

    public void FocusGrid() => _canvas.Focus();

    /// <inheritdoc cref="SpreadsheetCanvas.NotifySheetRenamed"/>
    public void NotifySheetRenamed(string oldName, string newName) => _canvas.NotifySheetRenamed(oldName, newName);

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
    public void ApplyStyle(Func<CellStyle, CellStyle> transform)
    {
        Worksheet? sheet = CurrentSheet();

        if (sheet is null)
        {
            return;
        }

        RangeEditing.ApplyStyle(sheet, _canvas.Selection.Range, transform);
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
        ApplyStyle(s => s with { Bold = target });
    }

    public void ToggleItalic()
    {
        bool target = !ActiveStyle.Italic;
        ApplyStyle(s => s with { Italic = target });
    }

    public void ToggleUnderline()
    {
        bool target = !ActiveStyle.Underline;
        ApplyStyle(s => s with { Underline = target });
    }

    /// <summary><c>null</c> volta para a cor automática do sistema azul/preto/verde.</summary>
    public void SetFontColor(RgbColor? color) => ApplyStyle(s => s with { FontColor = color });

    /// <summary><c>null</c> remove o preenchimento.</summary>
    public void SetFillColor(RgbColor? color) => ApplyStyle(s => s with { BackgroundColor = color });

    public void SetFontFamily(string family) => ApplyStyle(s => s with { FontFamily = family });

    public void SetFontSize(double size) => ApplyStyle(s => s with { FontSize = size });

    public void SetHorizontalAlignment(CoreHorizontalAlignment alignment) =>
        ApplyStyle(s => s with { HorizontalAlignment = alignment });

    public void SetVerticalAlignment(CoreVerticalAlignment alignment) =>
        ApplyStyle(s => s with { VerticalAlignment = alignment });

    public void ApplyBorderPreset(BorderPreset preset, BorderLineStyle style, RgbColor color)
    {
        Worksheet? sheet = CurrentSheet();

        if (sheet is null)
        {
            return;
        }

        BorderEditing.Apply(sheet, _canvas.Selection.Range, preset, style, color);
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

        RangeEditing.ApplyNumberFormat(sheet, _canvas.Selection.Range, mask);
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

        RangeEditing.Apply(sheet, _canvas.Selection.Range, cell => cell with
        {
            NumberFormat = increase
                ? StandardNumberFormats.IncreaseDecimals(cell.NumberFormat)
                : StandardNumberFormats.DecreaseDecimals(cell.NumberFormat),
        });

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

    // -------------------------------------------------------------- edição

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
        // A borda de 2px do editor cobre a célula por igual dos dois lados.
        _editor.Margin = new Thickness(cell.X - 1d, cell.Y - 1d, 0d, 0d);
        _editor.Width = Math.Max(cell.Width + 2d, 60d);
        _editor.Height = cell.Height + 2d;
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
        Cell existing = engine.Workbook[_canvas.SheetName].GetCell(address);

        try
        {
            engine.SetCell(location, CellInput.Parse(text, existing));
        }
        catch (Nordxcel.Core.Formulas.FormulaSyntaxException)
        {
            // Fórmula malformada não altera a planilha; guarda o texto como está para
            // o usuário ver o que digitou e corrigir.
            engine.SetCell(location, existing with { Formula = null, Value = CellValue.Text(text) });
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
