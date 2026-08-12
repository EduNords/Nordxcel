using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nordxcel.Core.Calculation;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Layout;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;
using Nordxcel.Desktop.Rendering;
using CoreHorizontalAlignment = Nordxcel.Core.Model.Styling.HorizontalAlignment;
using CoreVerticalAlignment = Nordxcel.Core.Model.Styling.VerticalAlignment;

// O Avalonia tem um NavigationDirection próprio, para foco entre controles.
using NavigationDirection = Nordxcel.Core.Layout.NavigationDirection;

namespace Nordxcel.Desktop.Controls;

/// <summary>Pedido de edição de célula disparado pelo teclado ou pelo duplo clique.</summary>
public sealed class CellEditRequestedEventArgs(CellAddress address, string? initialText) : EventArgs
{
    public CellAddress Address { get; } = address;

    /// <summary>
    /// Texto com que a edição começa. <c>null</c> significa abrir com o conteúdo
    /// atual da célula, como o F2 do Excel; qualquer outro valor substitui.
    /// </summary>
    public string? InitialText { get; } = initialText;

    /// <summary>Verdadeiro quando a edição começou por digitação, e não por F2.</summary>
    public bool StartedByTyping => InitialText is not null;
}

/// <summary>
/// Desenha a planilha e trata a interação de teclado e mouse. Só percorre as
/// células visíveis, então o custo de um quadro depende do tamanho da janela e
/// não do tamanho do modelo.
/// <para>
/// Quando a aba tem painéis congelados, a área de dados é dividida em até quatro
/// quadrantes — canto, faixa superior, faixa esquerda e o principal — cada um
/// desenhado com seu próprio recorte e sua própria origem de rolagem. O quadrante
/// principal sempre existe; os outros três só quando há linha ou coluna congelada.
/// </para>
/// </summary>
public sealed class SpreadsheetCanvas : Control
{
    /// <summary>Menor largura de coluna aceita no redimensionamento manual.</summary>
    private const double MinColumnWidth = 20d;

    /// <summary>Menor altura de linha aceita no redimensionamento manual.</summary>
    private const double MinRowHeight = 8d;

    /// <summary>Distância em pixels, de cada lado da borda, que ainda conta como "pegar a borda".</summary>
    private const double ResizeHandleTolerance = 4d;

    /// <summary>
    /// Área mínima que o quadrante rolável mantém mesmo com muitas linhas ou colunas
    /// congeladas, para o painel congelado nunca engolir a janela inteira.
    /// </summary>
    private const double MinScrollableExtent = 60d;

    private static readonly Cursor DefaultCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor CellCursor = new(StandardCursorType.Cross);
    private static readonly Cursor ColumnResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor RowResizeCursor = new(StandardCursorType.SizeNorthSouth);

    private readonly GridTheme _theme = GridTheme.Default;
    private readonly TextCache _textCache = new();
    private readonly CellFormatter _formatter = new();

    private SheetGeometry? _geometry;
    private string _sheetName = string.Empty;
    private CalculationEngine? _engine;
    private double _scrollX;
    private double _scrollY;
    private bool _draggingSelection;

    private int? _resizingColumn;
    private double _resizeAnchorX;

    private int? _resizingRow;
    private double _resizeAnchorY;

    public SpreadsheetCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = CellCursor;
    }

    /// <summary>Disparado quando a rolagem muda por dentro, como na roda do mouse.</summary>
    public event EventHandler? ScrollChanged;

    public event EventHandler? SelectionChanged;

    public event EventHandler<CellEditRequestedEventArgs>? EditRequested;

    /// <summary>Disparado quando o conteúdo da planilha muda por aqui, como no Delete.</summary>
    public event EventHandler? ContentChanged;

    /// <summary>Disparado quando largura de coluna, altura de linha ou painéis congelados mudam.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Enter ou Tab chegando na grade enquanto o editor ainda não pegou o foco.</summary>
    public event EventHandler<NavigationDirection>? CommitRequested;

    /// <summary>Esc chegando na grade enquanto o editor ainda não pegou o foco.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>
    /// Ligado pela view enquanto o editor está aberto. Existe porque o foco do editor
    /// só chega no passo seguinte do laço de interface, e nesse intervalo as teclas
    /// ainda caem aqui — sem isso, um Enter rápido moveria o cursor sem gravar nada.
    /// </summary>
    public bool IsEditing { get; set; }

    public SheetSelection Selection { get; } = new();

    public CalculationEngine? Engine
    {
        get => _engine;
        set
        {
            _engine = value;
            _geometry = null;
            InvalidateVisual();
        }
    }

    public string SheetName
    {
        get => _sheetName;
        set
        {
            _sheetName = value;
            _geometry = null;
            Selection.MoveTo(CellAddress.Origin);
            InvalidateVisual();
        }
    }

    public double ScrollX
    {
        get => _scrollX;
        set => SetScrollX(value);
    }

    public double ScrollY
    {
        get => _scrollY;
        set => SetScrollY(value);
    }

    /// <summary>Largura da área de dados, já sem o cabeçalho de linhas.</summary>
    public double ViewportWidth => Math.Max(0d, Bounds.Width - _theme.RowHeaderWidth);

    /// <summary>Altura da área de dados, já sem o cabeçalho de colunas.</summary>
    public double ViewportHeight => Math.Max(0d, Bounds.Height - _theme.ColumnHeaderHeight);

    /// <summary>Não se chama <c>Theme</c> porque o Avalonia já usa esse nome em <c>StyledElement</c>.</summary>
    public GridTheme GridTheme => _theme;

    /// <summary>Célula ativa da aba atual, qualificada pela aba.</summary>
    public CellLocation ActiveLocation => new(_sheetName, Selection.Active);

    public Cell ActiveCell =>
        ResolveGeometry()?.Sheet.GetCell(Selection.Active) ?? Cell.Empty;

    /// <summary>Menor posição horizontal de rolagem permitida — o fim da faixa de colunas congeladas.</summary>
    public double MinScrollX => ResolveGeometry() is { } geometry ? FrozenWidth(geometry) : 0d;

    /// <summary>Menor posição vertical de rolagem permitida — o fim da faixa de linhas congeladas.</summary>
    public double MinScrollY => ResolveGeometry() is { } geometry ? FrozenHeight(geometry) : 0d;

    /// <summary>Tamanho total rolável da aba atual.</summary>
    public (double Width, double Height) GetScrollExtent()
    {
        SheetGeometry? geometry = ResolveGeometry();

        return geometry is null
            ? (ViewportWidth, ViewportHeight)
            : geometry.GetScrollExtent(_scrollX, _scrollY, ViewportWidth, ViewportHeight);
    }

    /// <summary>Retângulo de uma célula em coordenadas de tela, já descontada a rolagem.</summary>
    public Rect? GetCellScreenRect(CellAddress address)
    {
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return null;
        }

        double scrollX = address.Column < geometry.Sheet.FrozenColumns ? 0d : ScrollableOffsetX(geometry);
        double scrollY = address.Row < geometry.Sheet.FrozenRows ? 0d : ScrollableOffsetY(geometry);

        return CellRect(geometry, address.Row, address.Column, scrollX, scrollY);
    }

    /// <summary>Avisa que o conteúdo mudou e a grade precisa ser redesenhada.</summary>
    public void Refresh()
    {
        _geometry?.Synchronize();
        InvalidateVisual();
    }

    /// <summary>
    /// Atualiza o nome guardado quando a aba exibida é renomeada por fora (pela
    /// faixa de abas), sem resetar seleção nem rolagem — a <see cref="Worksheet"/>
    /// por trás continua sendo o mesmo objeto, só o rótulo mudou. Sem isso, a
    /// busca por <c>_sheetName</c> antigo no <see cref="Workbook"/> falharia e a
    /// grade pararia de desenhar.
    /// </summary>
    public void NotifySheetRenamed(string oldName, string newName)
    {
        if (!string.Equals(_sheetName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _sheetName = newName;
        InvalidateVisual();
    }

    // ------------------------------------------------------------ painéis congelados

    /// <summary>
    /// Congela as linhas acima e as colunas à esquerda da célula ativa, como o
    /// comando "Congelar Painéis" do Excel a partir da seleção.
    /// </summary>
    public void FreezeAtSelection()
    {
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return;
        }

        geometry.Sheet.FrozenRows = Selection.Active.Row;
        geometry.Sheet.FrozenColumns = Selection.Active.Column;

        ClampScrollToFrozenBounds(geometry);
        NotifyLayoutChanged();
    }

    /// <summary>Congela só a primeira linha, mantendo as colunas congeladas como estavam.</summary>
    public void FreezeTopRow()
    {
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return;
        }

        geometry.Sheet.FrozenRows = 1;

        ClampScrollToFrozenBounds(geometry);
        NotifyLayoutChanged();
    }

    /// <summary>Congela só a primeira coluna, mantendo as linhas congeladas como estavam.</summary>
    public void FreezeFirstColumn()
    {
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return;
        }

        geometry.Sheet.FrozenColumns = 1;

        ClampScrollToFrozenBounds(geometry);
        NotifyLayoutChanged();
    }

    public void UnfreezePanes()
    {
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return;
        }

        geometry.Sheet.FrozenRows = 0;
        geometry.Sheet.FrozenColumns = 0;

        NotifyLayoutChanged();
    }

    public bool HasFrozenPanes => ResolveGeometry() is { } geometry &&
                                   (geometry.Sheet.FrozenRows > 0 || geometry.Sheet.FrozenColumns > 0);

    private void NotifyLayoutChanged()
    {
        _geometry?.Synchronize();
        InvalidateVisual();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClampScrollToFrozenBounds(SheetGeometry geometry)
    {
        double minX = FrozenWidth(geometry);
        double minY = FrozenHeight(geometry);

        if (_scrollX < minX)
        {
            _scrollX = minX;
        }

        if (_scrollY < minY)
        {
            _scrollY = minY;
        }
    }

    /// <summary>Largura ocupada pelas colunas congeladas, com uma folga mínima para a área rolável.</summary>
    private double FrozenWidth(SheetGeometry geometry) =>
        FrozenPaneMath.FrozenExtent(geometry.Columns, geometry.Sheet.FrozenColumns, ViewportWidth, MinScrollableExtent);

    /// <summary>Altura ocupada pelas linhas congeladas, com uma folga mínima para a área rolável.</summary>
    private double FrozenHeight(SheetGeometry geometry) =>
        FrozenPaneMath.FrozenExtent(geometry.Rows, geometry.Sheet.FrozenRows, ViewportHeight, MinScrollableExtent);

    /// <summary>Rola o necessário para a célula ficar inteira dentro da janela.</summary>
    public void ScrollIntoView(CellAddress address)
    {
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null || ViewportWidth <= 0d || ViewportHeight <= 0d)
        {
            return;
        }

        double frozenWidth = FrozenWidth(geometry);
        double frozenHeight = FrozenHeight(geometry);

        double x = _scrollX;
        double y = _scrollY;

        // Célula dentro do painel congelado: sempre visível, não precisa rolar.
        if (address.Column >= geometry.Sheet.FrozenColumns)
        {
            double left = geometry.Columns.OffsetOf(address.Column);
            double right = left + geometry.Columns.SizeOf(address.Column);
            double scrollableWidth = ViewportWidth - frozenWidth;

            if (left < x)
            {
                x = left;
            }
            else if (right > x + scrollableWidth)
            {
                x = right - scrollableWidth;
            }
        }

        if (address.Row >= geometry.Sheet.FrozenRows)
        {
            double top = geometry.Rows.OffsetOf(address.Row);
            double bottom = top + geometry.Rows.SizeOf(address.Row);
            double scrollableHeight = ViewportHeight - frozenHeight;

            if (top < y)
            {
                y = top;
            }
            else if (bottom > y + scrollableHeight)
            {
                y = bottom - scrollableHeight;
            }
        }

        if (Math.Abs(x - _scrollX) < 0.01d && Math.Abs(y - _scrollY) < 0.01d)
        {
            return;
        }

        _scrollX = Math.Max(frozenWidth, x);
        _scrollY = Math.Max(frozenHeight, y);

        InvalidateVisual();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ interação

    public SpreadsheetHit HitTest(Point point)
    {
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return SpreadsheetHit.Outside;
        }

        bool inRowHeader = point.X < _theme.RowHeaderWidth;
        bool inColumnHeader = point.Y < _theme.ColumnHeaderHeight;

        if (inRowHeader && inColumnHeader)
        {
            return new SpreadsheetHit(SpreadsheetHitKind.Corner, CellAddress.Origin);
        }

        int column = geometry.Columns.IndexAt(ToSheetX(geometry, point.X));
        int row = geometry.Rows.IndexAt(ToSheetY(geometry, point.Y));

        if (inColumnHeader)
        {
            return new SpreadsheetHit(SpreadsheetHitKind.ColumnHeader, new CellAddress(0, column));
        }

        if (inRowHeader)
        {
            return new SpreadsheetHit(SpreadsheetHitKind.RowHeader, new CellAddress(row, 0));
        }

        return new SpreadsheetHit(SpreadsheetHitKind.Cell, new CellAddress(row, column));
    }

    /// <summary>Converte a coordenada X da tela para o espaço de deslocamento da planilha (o de <see cref="AxisMetrics.OffsetOf"/>).</summary>
    private double ToSheetX(SheetGeometry geometry, double screenX)
    {
        double localX = screenX - _theme.RowHeaderWidth;
        double frozenWidth = FrozenWidth(geometry);

        return localX < frozenWidth ? localX : localX + FrozenPaneMath.ScrollableOffset(_scrollX, frozenWidth);
    }

    /// <summary>Converte a coordenada Y da tela para o espaço de deslocamento da planilha.</summary>
    private double ToSheetY(SheetGeometry geometry, double screenY)
    {
        double localY = screenY - _theme.ColumnHeaderHeight;
        double frozenHeight = FrozenHeight(geometry);

        return localY < frozenHeight ? localY : localY + FrozenPaneMath.ScrollableOffset(_scrollY, frozenHeight);
    }

    /// <summary>
    /// Deslocamento de rolagem a usar no eixo X do quadrante rolável.
    /// <para>
    /// <c>_scrollX</c> vive no espaço de deslocamento absoluto da planilha (o mesmo
    /// de <see cref="AxisMetrics.OffsetOf"/>) — é o que <see cref="ScrollIntoView"/> e
    /// o mínimo de rolagem esperam. Já <see cref="ColumnLeft"/> soma a origem do
    /// quadrante rolável (que começa depois do painel congelado); sem subtrair a
    /// largura congelada aqui, esse trecho seria contado duas vezes e o conteúdo
    /// apareceria deslocado para a esquerda do recorte do quadrante.
    /// </para>
    /// </summary>
    private double ScrollableOffsetX(SheetGeometry geometry) => FrozenPaneMath.ScrollableOffset(_scrollX, FrozenWidth(geometry));

    /// <inheritdoc cref="ScrollableOffsetX"/>
    private double ScrollableOffsetY(SheetGeometry geometry) => FrozenPaneMath.ScrollableOffset(_scrollY, FrozenHeight(geometry));

    /// <summary>Coluna cuja borda (esquerda ou direita) está sob o ponto, ou <c>null</c>.</summary>
    private int? HitTestColumnBorder(SheetGeometry geometry, Point point)
    {
        if (point.Y >= _theme.ColumnHeaderHeight || point.X < _theme.RowHeaderWidth)
        {
            return null;
        }

        double sheetX = ToSheetX(geometry, point.X);
        int column = geometry.Columns.IndexAt(sheetX);
        double left = geometry.Columns.OffsetOf(column);
        double right = left + geometry.Columns.SizeOf(column);

        if (Math.Abs(sheetX - right) <= ResizeHandleTolerance)
        {
            return column;
        }

        if (column > 0 && Math.Abs(sheetX - left) <= ResizeHandleTolerance)
        {
            return column - 1;
        }

        return null;
    }

    /// <summary>Linha cuja borda (superior ou inferior) está sob o ponto, ou <c>null</c>.</summary>
    private int? HitTestRowBorder(SheetGeometry geometry, Point point)
    {
        if (point.X >= _theme.RowHeaderWidth || point.Y < _theme.ColumnHeaderHeight)
        {
            return null;
        }

        double sheetY = ToSheetY(geometry, point.Y);
        int row = geometry.Rows.IndexAt(sheetY);
        double top = geometry.Rows.OffsetOf(row);
        double bottom = top + geometry.Rows.SizeOf(row);

        if (Math.Abs(sheetY - bottom) <= ResizeHandleTolerance)
        {
            return row;
        }

        if (row > 0 && Math.Abs(sheetY - top) <= ResizeHandleTolerance)
        {
            return row - 1;
        }

        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        PointerPoint point = e.GetCurrentPoint(this);

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return;
        }

        if (e.ClickCount == 2 && HitTestColumnBorder(geometry, point.Position) is int columnToFit)
        {
            AutoFitColumn(geometry, columnToFit);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2 && HitTestRowBorder(geometry, point.Position) is int rowToFit)
        {
            AutoFitRow(geometry, rowToFit);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 1 && HitTestColumnBorder(geometry, point.Position) is int columnToResize)
        {
            _resizingColumn = columnToResize;
            _resizeAnchorX = geometry.Columns.OffsetOf(columnToResize);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 1 && HitTestRowBorder(geometry, point.Position) is int rowToResize)
        {
            _resizingRow = rowToResize;
            _resizeAnchorY = geometry.Rows.OffsetOf(rowToResize);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        SpreadsheetHit hit = HitTest(point.Position);
        bool extending = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (hit.Kind)
        {
            case SpreadsheetHitKind.Cell:
                if (e.ClickCount >= 2)
                {
                    EditRequested?.Invoke(this, new CellEditRequestedEventArgs(hit.Address, null));
                    e.Handled = true;
                    return;
                }

                if (extending)
                {
                    Selection.ExtendTo(hit.Address);
                }
                else
                {
                    Selection.MoveTo(hit.Address);
                    _draggingSelection = true;
                    e.Pointer.Capture(this);
                }

                break;

            case SpreadsheetHitKind.ColumnHeader:
                Selection.SelectColumns(
                    extending ? Selection.Range.Start.Column : hit.Address.Column,
                    hit.Address.Column);
                break;

            case SpreadsheetHitKind.RowHeader:
                Selection.SelectRows(
                    extending ? Selection.Range.Start.Row : hit.Address.Row,
                    hit.Address.Row);
                break;

            case SpreadsheetHitKind.Corner:
                Selection.SelectAll();
                break;

            default:
                return;
        }

        NotifySelection();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Point position = e.GetPosition(this);
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return;
        }

        if (_resizingColumn is int resizingColumn)
        {
            double sheetX = ToSheetX(geometry, position.X);
            double width = Math.Max(MinColumnWidth, sheetX - _resizeAnchorX);

            geometry.Sheet.SetColumnWidth(resizingColumn, width);
            Refresh();
            return;
        }

        if (_resizingRow is int resizingRow)
        {
            double sheetY = ToSheetY(geometry, position.Y);
            double height = Math.Max(MinRowHeight, sheetY - _resizeAnchorY);

            geometry.Sheet.SetRowHeight(resizingRow, height);
            Refresh();
            return;
        }

        if (_draggingSelection)
        {
            SpreadsheetHit hit = HitTest(position);

            if (hit.IsCell)
            {
                Selection.ExtendTo(hit.Address);
                NotifySelection();
            }

            return;
        }

        UpdateHoverCursor(geometry, position);
    }

    private void UpdateHoverCursor(SheetGeometry geometry, Point position)
    {
        if (HitTestColumnBorder(geometry, position) is not null)
        {
            Cursor = ColumnResizeCursor;
            return;
        }

        if (HitTestRowBorder(geometry, position) is not null)
        {
            Cursor = RowResizeCursor;
            return;
        }

        SpreadsheetHit hit = HitTest(position);

        Cursor = hit.Kind is SpreadsheetHitKind.Cell ? CellCursor : DefaultCursor;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_resizingColumn is not null)
        {
            _resizingColumn = null;
            e.Pointer.Capture(null);
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_resizingRow is not null)
        {
            _resizingRow = null;
            e.Pointer.Capture(null);
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_draggingSelection)
        {
            return;
        }

        _draggingSelection = false;
        e.Pointer.Capture(null);
    }

    /// <summary>Ajusta a largura da coluna ao maior texto que ela contém.</summary>
    private void AutoFitColumn(SheetGeometry geometry, int column)
    {
        double widest = 0d;

        foreach ((CellAddress address, Cell cell) in geometry.Sheet.Cells)
        {
            if (address.Column != column)
            {
                continue;
            }

            FormattedValue formatted = _formatter.Format(cell);

            if (formatted.Text.Length == 0)
            {
                continue;
            }

            FormattedText text = _textCache.Get(
                formatted.Text,
                cell.Style.FontFamily,
                cell.Style.FontSize,
                cell.Style.Bold,
                cell.Style.Italic,
                Colors.Black);

            widest = Math.Max(widest, text.Width);
        }

        double newWidth = widest <= 0d
            ? Worksheet.DefaultColumnWidth
            : widest + (_theme.CellPadding * 2d) + 2d;

        geometry.Sheet.SetColumnWidth(column, Math.Max(MinColumnWidth, newWidth));
        Refresh();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ajusta a altura da linha ao maior texto que ela contém.</summary>
    private void AutoFitRow(SheetGeometry geometry, int row)
    {
        double tallest = 0d;

        foreach ((CellAddress address, Cell cell) in geometry.Sheet.Cells)
        {
            if (address.Row != row)
            {
                continue;
            }

            FormattedValue formatted = _formatter.Format(cell);

            if (formatted.Text.Length == 0)
            {
                continue;
            }

            FormattedText text = _textCache.Get(
                formatted.Text,
                cell.Style.FontFamily,
                cell.Style.FontSize,
                cell.Style.Bold,
                cell.Style.Italic,
                Colors.Black);

            tallest = Math.Max(tallest, text.Height);
        }

        double newHeight = tallest <= 0d
            ? Worksheet.DefaultRowHeight
            : tallest + 6d;

        geometry.Sheet.SetRowHeight(row, Math.Max(MinRowHeight, newHeight));
        Refresh();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return;
        }

        // Três linhas por entalhe da roda, como no Excel.
        double verticalStep = geometry.Rows.DefaultSize * 3d;
        double horizontalStep = geometry.Columns.DefaultSize;

        (double extentWidth, double extentHeight) =
            geometry.GetScrollExtent(_scrollX, _scrollY, ViewportWidth, ViewportHeight);

        ScrollY = Math.Clamp(ScrollY - (e.Delta.Y * verticalStep), MinScrollY, Math.Max(MinScrollY, extentHeight - ViewportHeight));
        ScrollX = Math.Clamp(ScrollX - (e.Delta.X * horizontalStep), MinScrollX, Math.Max(MinScrollX, extentWidth - ViewportWidth));

        ScrollChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (IsEditing)
        {
            HandleKeyWhileEditing(e, shift);
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                Navigate(geometry, NavigationDirection.Up, shift, control);
                break;
            case Key.Down:
                Navigate(geometry, NavigationDirection.Down, shift, control);
                break;
            case Key.Left:
                Navigate(geometry, NavigationDirection.Left, shift, control);
                break;
            case Key.Right:
                Navigate(geometry, NavigationDirection.Right, shift, control);
                break;

            case Key.Tab:
                MoveActive(SelectionNavigator.Step(
                    Selection.Active,
                    shift ? NavigationDirection.Left : NavigationDirection.Right));
                break;

            case Key.Enter:
                MoveActive(SelectionNavigator.Step(
                    Selection.Active,
                    shift ? NavigationDirection.Up : NavigationDirection.Down));
                break;

            case Key.Home:
                MoveActive(control ? SelectionNavigator.HomeOfSheet() : SelectionNavigator.HomeOfRow(Selection.Active));
                break;

            case Key.End:
                if (control)
                {
                    MoveActive(SelectionNavigator.EndOfContent(geometry.Sheet));
                }

                break;

            case Key.PageDown:
                MoveActive(SelectionNavigator.Step(Selection.Active, NavigationDirection.Down, VisibleRowCount(geometry)));
                break;

            case Key.PageUp:
                MoveActive(SelectionNavigator.Step(Selection.Active, NavigationDirection.Up, VisibleRowCount(geometry)));
                break;

            case Key.F2:
                EditRequested?.Invoke(this, new CellEditRequestedEventArgs(Selection.Active, null));
                break;

            case Key.Delete:
            case Key.Back:
                ClearSelection();
                break;

            case Key.A when control:
                Selection.SelectAll();
                NotifySelection();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Teclas que chegam aqui durante a janela em que o editor abriu mas ainda não
    /// tem o foco. Só as que confirmam ou cancelam importam; as demais são ignoradas
    /// para não mexer na seleção por baixo da edição em curso.
    /// </summary>
    private void HandleKeyWhileEditing(KeyEventArgs e, bool shift)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitRequested?.Invoke(this, shift ? NavigationDirection.Up : NavigationDirection.Down);
                break;

            case Key.Tab:
                CommitRequested?.Invoke(this, shift ? NavigationDirection.Left : NavigationDirection.Right);
                break;

            case Key.Escape:
                CancelRequested?.Invoke(this, EventArgs.Empty);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        // Vale também durante a edição: a view acrescenta ao que já está no editor,
        // e assim nenhuma tecla se perde enquanto o foco não chega.
        EditRequested?.Invoke(this, new CellEditRequestedEventArgs(Selection.Active, e.Text));
        e.Handled = true;
    }

    private void Navigate(SheetGeometry geometry, NavigationDirection direction, bool extend, bool jump)
    {
        CellAddress origin = extend ? Selection.Focus : Selection.Active;

        CellAddress target = jump
            ? SelectionNavigator.JumpToEdge(geometry.Sheet, origin, direction)
            : SelectionNavigator.Step(origin, direction);

        if (extend)
        {
            Selection.ExtendTo(target);
            ScrollIntoView(target);
            NotifySelection();
            return;
        }

        MoveActive(target);
    }

    private void MoveActive(CellAddress address)
    {
        Selection.MoveTo(address);
        ScrollIntoView(address);
        NotifySelection();
    }

    private void ClearSelection()
    {
        if (_engine is null)
        {
            return;
        }

        CellRange range = Selection.Range;
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null)
        {
            return;
        }

        // Limpa só o que existe: varrer uma coluna inteira célula a célula seria inútil.
        var targets = new List<CellAddress>();

        foreach (CellAddress address in geometry.Sheet.Cells.Keys)
        {
            if (range.Contains(address))
            {
                targets.Add(address);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        _engine.AutoRecalculate = false;

        foreach (CellAddress address in targets)
        {
            _engine.ClearCell(new CellLocation(_sheetName, address));
        }

        _engine.AutoRecalculate = true;
        _engine.Recalculate();

        ContentChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private int VisibleRowCount(SheetGeometry geometry) =>
        Math.Max(1, (int)(ViewportHeight / geometry.Rows.DefaultSize) - 1);

    private void NotifySelection()
    {
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetScrollX(double value)
    {
        double minimum = ResolveGeometry() is { } geometry ? FrozenWidth(geometry) : 0d;
        double clamped = Math.Max(minimum, value);

        if (Math.Abs(clamped - _scrollX) < 0.01d)
        {
            return;
        }

        _scrollX = clamped;
        InvalidateVisual();
    }

    private void SetScrollY(double value)
    {
        double minimum = ResolveGeometry() is { } geometry ? FrozenHeight(geometry) : 0d;
        double clamped = Math.Max(minimum, value);

        if (Math.Abs(clamped - _scrollY) < 0.01d)
        {
            return;
        }

        _scrollY = clamped;
        InvalidateVisual();
    }

    // ------------------------------------------------------------- desenho

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var full = new Rect(Bounds.Size);
        context.FillRectangle(_theme.OutsideBackground, full);

        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null || Bounds.Width <= 0d || Bounds.Height <= 0d)
        {
            return;
        }

        geometry.Synchronize();

        int frozenRows = geometry.Sheet.FrozenRows;
        int frozenColumns = geometry.Sheet.FrozenColumns;
        double frozenWidth = FrozenWidth(geometry);
        double frozenHeight = FrozenHeight(geometry);

        double mainOriginX = _theme.RowHeaderWidth + frozenWidth;
        double mainOriginY = _theme.ColumnHeaderHeight + frozenHeight;
        double mainWidth = Math.Max(0d, ViewportWidth - frozenWidth);
        double mainHeight = Math.Max(0d, ViewportHeight - frozenHeight);

        VisibleRange mainRange = geometry.GetVisibleRange(_scrollX, _scrollY, mainWidth, mainHeight);

        double scrollableX = ScrollableOffsetX(geometry);
        double scrollableY = ScrollableOffsetY(geometry);

        // O quadrante principal (linhas e colunas roláveis) sempre existe.
        DrawQuadrant(
            context, geometry,
            new Rect(mainOriginX, mainOriginY, mainWidth, mainHeight),
            mainRange, scrollableX, scrollableY);

        if (frozenRows > 0)
        {
            var topRange = new VisibleRange(0, frozenRows - 1, mainRange.FirstColumn, mainRange.LastColumn);

            DrawQuadrant(
                context, geometry,
                new Rect(mainOriginX, _theme.ColumnHeaderHeight, mainWidth, frozenHeight),
                topRange, scrollableX, 0d);
        }

        if (frozenColumns > 0)
        {
            var leftRange = new VisibleRange(mainRange.FirstRow, mainRange.LastRow, 0, frozenColumns - 1);

            DrawQuadrant(
                context, geometry,
                new Rect(_theme.RowHeaderWidth, mainOriginY, frozenWidth, mainHeight),
                leftRange, 0d, scrollableY);
        }

        if (frozenRows > 0 && frozenColumns > 0)
        {
            var cornerRange = new VisibleRange(0, frozenRows - 1, 0, frozenColumns - 1);

            DrawQuadrant(
                context, geometry,
                new Rect(_theme.RowHeaderWidth, _theme.ColumnHeaderHeight, frozenWidth, frozenHeight),
                cornerRange, 0d, 0d);
        }

        DrawFreezeDividers(context, frozenWidth, frozenHeight);

        DrawColumnHeaderBand(
            context, geometry,
            new Rect(mainOriginX, 0d, mainWidth, _theme.ColumnHeaderHeight),
            mainRange.FirstColumn, mainRange.LastColumn, scrollableX);

        if (frozenColumns > 0)
        {
            DrawColumnHeaderBand(
                context, geometry,
                new Rect(_theme.RowHeaderWidth, 0d, frozenWidth, _theme.ColumnHeaderHeight),
                0, frozenColumns - 1, 0d);
        }

        DrawRowHeaderBand(
            context, geometry,
            new Rect(0d, mainOriginY, _theme.RowHeaderWidth, mainHeight),
            mainRange.FirstRow, mainRange.LastRow, scrollableY);

        if (frozenRows > 0)
        {
            DrawRowHeaderBand(
                context, geometry,
                new Rect(0d, _theme.ColumnHeaderHeight, _theme.RowHeaderWidth, frozenHeight),
                0, frozenRows - 1, 0d);
        }

        DrawCorner(context);
    }

    private void DrawQuadrant(
        DrawingContext context,
        SheetGeometry geometry,
        Rect clip,
        VisibleRange range,
        double scrollX,
        double scrollY)
    {
        if (clip.Width <= 0d || clip.Height <= 0d || range.RowCount <= 0 || range.ColumnCount <= 0)
        {
            return;
        }

        using (context.PushClip(clip))
        {
            context.FillRectangle(_theme.CellBackground, clip);
            DrawFills(context, geometry, range, scrollX, scrollY);
            DrawSelectionFill(context, geometry, range, scrollX, scrollY);
            DrawGridLines(context, geometry, range, clip, scrollX, scrollY);
            DrawContents(context, geometry, range, scrollX, scrollY);
            DrawBorders(context, geometry, range, scrollX, scrollY);
            DrawSelectionOutline(context, geometry, scrollX, scrollY);
        }
    }

    /// <summary>
    /// Linha de destaque entre o painel congelado e a área rolável — mais forte que
    /// a grade normal, para não deixar dúvida de onde a rolagem para de mexer.
    /// </summary>
    private void DrawFreezeDividers(DrawingContext context, double frozenWidth, double frozenHeight)
    {
        if (frozenWidth <= 0d && frozenHeight <= 0d)
        {
            return;
        }

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 150)).ToImmutable(), 1.5);

        if (frozenWidth > 0d)
        {
            double x = Snap(_theme.RowHeaderWidth + frozenWidth);

            context.DrawLine(pen, new Point(x, 0d), new Point(x, Bounds.Height));
        }

        if (frozenHeight > 0d)
        {
            double y = Snap(_theme.ColumnHeaderHeight + frozenHeight);

            context.DrawLine(pen, new Point(0d, y), new Point(Bounds.Width, y));
        }
    }

    private void DrawFills(DrawingContext context, SheetGeometry geometry, VisibleRange range, double scrollX, double scrollY)
    {
        Worksheet sheet = geometry.Sheet;

        for (int row = range.FirstRow; row <= range.LastRow; row++)
        {
            for (int column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                RgbColor? background = sheet.GetCell(new CellAddress(row, column)).Style.BackgroundColor;

                if (background is null)
                {
                    continue;
                }

                context.FillRectangle(
                    new SolidColorBrush(ToColor(background.Value)).ToImmutable(),
                    CellRect(geometry, row, column, scrollX, scrollY));
            }
        }
    }

    private void DrawSelectionFill(DrawingContext context, SheetGeometry geometry, VisibleRange range, double scrollX, double scrollY)
    {
        if (Selection.IsSingleCell)
        {
            return;
        }

        CellRange selected = Selection.Range;

        int firstRow = Math.Max(selected.Start.Row, range.FirstRow);
        int lastRow = Math.Min(selected.End.Row, range.LastRow);
        int firstColumn = Math.Max(selected.Start.Column, range.FirstColumn);
        int lastColumn = Math.Min(selected.End.Column, range.LastColumn);

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                // A célula ativa fica sem véu, como no Excel, para se destacar do resto.
                if (row == Selection.Active.Row && column == Selection.Active.Column)
                {
                    continue;
                }

                context.FillRectangle(_theme.SelectionFill, CellRect(geometry, row, column, scrollX, scrollY));
            }
        }
    }

    private void DrawSelectionOutline(DrawingContext context, SheetGeometry geometry, double scrollX, double scrollY)
    {
        CellRange selected = Selection.Range;

        Rect start = CellRect(geometry, selected.Start.Row, selected.Start.Column, scrollX, scrollY);
        Rect end = CellRect(geometry, selected.End.Row, selected.End.Column, scrollX, scrollY);

        var outline = new Rect(start.X, start.Y, end.Right - start.X, end.Bottom - start.Y);

        context.DrawRectangle(null, _theme.SelectionBorder, outline);

        if (!Selection.IsSingleCell)
        {
            context.DrawRectangle(
                null,
                _theme.GridLine,
                CellRect(geometry, Selection.Active.Row, Selection.Active.Column, scrollX, scrollY));
        }
    }

    private void DrawGridLines(
        DrawingContext context,
        SheetGeometry geometry,
        VisibleRange range,
        Rect clip,
        double scrollX,
        double scrollY)
    {
        for (int column = range.FirstColumn; column <= range.LastColumn + 1; column++)
        {
            double x = Snap(ColumnLeft(geometry, column, scrollX));

            context.DrawLine(_theme.GridLine, new Point(x, clip.Top), new Point(x, clip.Bottom));
        }

        for (int row = range.FirstRow; row <= range.LastRow + 1; row++)
        {
            double y = Snap(RowTop(geometry, row, scrollY));

            context.DrawLine(_theme.GridLine, new Point(clip.Left, y), new Point(clip.Right, y));
        }
    }

    private void DrawContents(DrawingContext context, SheetGeometry geometry, VisibleRange range, double scrollX, double scrollY)
    {
        Worksheet sheet = geometry.Sheet;

        for (int row = range.FirstRow; row <= range.LastRow; row++)
        {
            for (int column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                var address = new CellAddress(row, column);
                Cell cell = sheet.GetCell(address);

                if (cell.Value.IsBlank)
                {
                    continue;
                }

                DrawCellText(context, geometry, sheet, address, cell, range, scrollX, scrollY);
            }
        }
    }

    private void DrawCellText(
        DrawingContext context,
        SheetGeometry geometry,
        Worksheet sheet,
        CellAddress address,
        Cell cell,
        VisibleRange range,
        double scrollX,
        double scrollY)
    {
        FormattedValue formatted = _formatter.Format(cell);

        if (formatted.Text.Length == 0)
        {
            return;
        }

        CellColorRole role = CellColorClassifier.Classify(
            cell,
            _engine?.GetFormula(new CellLocation(sheet.Name, address)),
            sheet.Name);

        RgbColor color = formatted.Color ?? CellColorClassifier.ResolveFontColor(cell.Style, role);

        FormattedText text = _textCache.Get(
            formatted.Text,
            cell.Style.FontFamily,
            cell.Style.FontSize,
            cell.Style.Bold,
            cell.Style.Italic,
            ToColor(color));

        Rect cellRect = CellRect(geometry, address.Row, address.Column, scrollX, scrollY);
        double available = cellRect.Width - (_theme.CellPadding * 2d);

        CoreHorizontalAlignment alignment = ResolveAlignment(cell);

        // Número que não cabe vira ###, como no Excel: melhor avisar que mentir.
        if (text.Width > available && cell.Value.IsNumber)
        {
            double perCharacter = Math.Max(1d, text.Width / formatted.Text.Length);

            text = _textCache.Get(
                new string('#', Math.Max(1, (int)(available / perCharacter))),
                cell.Style.FontFamily,
                cell.Style.FontSize,
                cell.Style.Bold,
                cell.Style.Italic,
                ToColor(color));
        }

        Rect clip = cellRect;

        // Texto maior que a célula transborda para as vizinhas vazias, como no Excel.
        if (text.Width > available && !cell.Value.IsNumber && alignment is CoreHorizontalAlignment.Left)
        {
            clip = ExtendOverEmptyNeighbours(geometry, sheet, address, cellRect, text.Width, range, scrollX, scrollY);
        }

        double x = alignment switch
        {
            CoreHorizontalAlignment.Right => cellRect.Right - _theme.CellPadding - text.Width,
            CoreHorizontalAlignment.Center => cellRect.X + ((cellRect.Width - text.Width) / 2d),
            _ => cellRect.X + _theme.CellPadding,
        };

        double y = cell.Style.VerticalAlignment switch
        {
            CoreVerticalAlignment.Top => cellRect.Y + 1d,
            CoreVerticalAlignment.Center => cellRect.Y + ((cellRect.Height - text.Height) / 2d),
            _ => cellRect.Bottom - text.Height - 1d,
        };

        using (context.PushClip(clip))
        {
            context.DrawText(text, new Point(x, y));
        }
    }

    private Rect ExtendOverEmptyNeighbours(
        SheetGeometry geometry,
        Worksheet sheet,
        CellAddress address,
        Rect cellRect,
        double needed,
        VisibleRange range,
        double scrollX,
        double scrollY)
    {
        double width = cellRect.Width;
        int column = address.Column + 1;

        while (width < needed + (_theme.CellPadding * 2d) &&
               column <= range.LastColumn + 1 &&
               column < CellAddress.MaxColumns)
        {
            if (!sheet.GetValue(new CellAddress(address.Row, column)).IsBlank)
            {
                break;
            }

            width += geometry.Columns.SizeOf(column);
            column++;
        }

        return cellRect.WithWidth(width);
    }

    private void DrawBorders(DrawingContext context, SheetGeometry geometry, VisibleRange range, double scrollX, double scrollY)
    {
        Worksheet sheet = geometry.Sheet;

        for (int row = range.FirstRow; row <= range.LastRow; row++)
        {
            for (int column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                CellBorders borders = sheet.GetCell(new CellAddress(row, column)).Style.Borders;

                if (!borders.HasAny)
                {
                    continue;
                }

                Rect rect = CellRect(geometry, row, column, scrollX, scrollY);

                DrawEdge(context, borders.Top, rect.TopLeft, rect.TopRight);
                DrawEdge(context, borders.Bottom, rect.BottomLeft, rect.BottomRight);
                DrawEdge(context, borders.Left, rect.TopLeft, rect.BottomLeft);
                DrawEdge(context, borders.Right, rect.TopRight, rect.BottomRight);
            }
        }
    }

    private static void DrawEdge(DrawingContext context, BorderEdge edge, Point from, Point to)
    {
        if (!edge.IsVisible)
        {
            return;
        }

        var brush = new SolidColorBrush(ToColor(edge.Color)).ToImmutable();

        if (edge.Style is BorderLineStyle.Double)
        {
            // Linha dupla: convenção de total geral em modelo financeiro.
            var pen = new Pen(brush, 1).ToImmutable();
            bool horizontal = Math.Abs(from.Y - to.Y) < 0.01d;
            var shift = horizontal ? new Point(0, 2) : new Point(2, 0);

            context.DrawLine(pen, Snap(from), Snap(to));
            context.DrawLine(pen, Snap(from + shift), Snap(to + shift));
            return;
        }

        double thickness = edge.Style switch
        {
            BorderLineStyle.Medium => 2d,
            BorderLineStyle.Thick => 3d,
            _ => 1d,
        };

        context.DrawLine(new Pen(brush, thickness).ToImmutable(), Snap(from), Snap(to));
    }

    // ------------------------------------------------------------ cabeçalhos

    private void DrawColumnHeaderBand(
        DrawingContext context,
        SheetGeometry geometry,
        Rect band,
        int firstColumn,
        int lastColumn,
        double scrollX)
    {
        if (band.Width <= 0d)
        {
            return;
        }

        CellRange selected = Selection.Range;

        using (context.PushClip(band))
        {
            context.FillRectangle(_theme.HeaderBackground, band);

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                double left = ColumnLeft(geometry, column, scrollX);
                double width = geometry.Columns.SizeOf(column);

                bool highlighted = column >= selected.Start.Column && column <= selected.End.Column;

                if (highlighted)
                {
                    context.FillRectangle(
                        _theme.HeaderSelectedBackground,
                        new Rect(left, band.Y, width, _theme.ColumnHeaderHeight));
                }

                FormattedText text = HeaderText(CellAddress.ColumnToName(column), highlighted);

                context.DrawText(text, new Point(
                    left + ((width - text.Width) / 2d),
                    band.Y + ((_theme.ColumnHeaderHeight - text.Height) / 2d)));

                double edge = Snap(left + width);
                context.DrawLine(_theme.HeaderLine, new Point(edge, band.Top), new Point(edge, band.Bottom));
            }

            double bottom = Snap(band.Bottom);
            context.DrawLine(_theme.HeaderLine, new Point(band.Left, bottom), new Point(band.Right, bottom));
        }
    }

    private void DrawRowHeaderBand(
        DrawingContext context,
        SheetGeometry geometry,
        Rect band,
        int firstRow,
        int lastRow,
        double scrollY)
    {
        if (band.Height <= 0d)
        {
            return;
        }

        CellRange selected = Selection.Range;

        using (context.PushClip(band))
        {
            context.FillRectangle(_theme.HeaderBackground, band);

            for (int row = firstRow; row <= lastRow; row++)
            {
                double top = RowTop(geometry, row, scrollY);
                double height = geometry.Rows.SizeOf(row);

                bool highlighted = row >= selected.Start.Row && row <= selected.End.Row;

                if (highlighted)
                {
                    context.FillRectangle(
                        _theme.HeaderSelectedBackground,
                        new Rect(band.X, top, _theme.RowHeaderWidth, height));
                }

                FormattedText text = HeaderText(
                    (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    highlighted);

                context.DrawText(text, new Point(
                    _theme.RowHeaderWidth - text.Width - 6d,
                    top + ((height - text.Height) / 2d)));

                double edge = Snap(top + height);
                context.DrawLine(_theme.HeaderLine, new Point(band.Left, edge), new Point(band.Right, edge));
            }

            double right = Snap(band.Right);
            context.DrawLine(_theme.HeaderLine, new Point(right, band.Top), new Point(right, band.Bottom));
        }
    }

    private void DrawCorner(DrawingContext context)
    {
        var corner = new Rect(0d, 0d, _theme.RowHeaderWidth, _theme.ColumnHeaderHeight);

        context.FillRectangle(_theme.HeaderBackground, corner);
        context.DrawLine(
            _theme.HeaderLine,
            new Point(Snap(_theme.RowHeaderWidth), 0d),
            new Point(Snap(_theme.RowHeaderWidth), Snap(_theme.ColumnHeaderHeight)));
        context.DrawLine(
            _theme.HeaderLine,
            new Point(0d, Snap(_theme.ColumnHeaderHeight)),
            new Point(Snap(_theme.RowHeaderWidth), Snap(_theme.ColumnHeaderHeight)));
    }

    private FormattedText HeaderText(string text, bool highlighted) => _textCache.Get(
        text,
        _theme.FontFamily,
        _theme.HeaderFontSize,
        highlighted,
        false,
        highlighted ? Color.FromRgb(20, 70, 43) : Color.FromRgb(68, 68, 68));

    // --------------------------------------------------------------- apoio

    private SheetGeometry? ResolveGeometry()
    {
        if (_engine is null || string.IsNullOrEmpty(_sheetName))
        {
            return null;
        }

        if (_geometry is not null && string.Equals(_geometry.Sheet.Name, _sheetName, StringComparison.OrdinalIgnoreCase))
        {
            return _geometry;
        }

        if (!_engine.Workbook.TryGetWorksheet(_sheetName, out Worksheet? sheet))
        {
            return null;
        }

        _geometry = new SheetGeometry(sheet);

        return _geometry;
    }

    private double ColumnLeft(SheetGeometry geometry, int column, double scrollX) =>
        _theme.RowHeaderWidth + geometry.Columns.OffsetOf(column) - scrollX;

    private double RowTop(SheetGeometry geometry, int row, double scrollY) =>
        _theme.ColumnHeaderHeight + geometry.Rows.OffsetOf(row) - scrollY;

    private Rect CellRect(SheetGeometry geometry, int row, int column, double scrollX, double scrollY) => new(
        ColumnLeft(geometry, column, scrollX),
        RowTop(geometry, row, scrollY),
        geometry.Columns.SizeOf(column),
        geometry.Rows.SizeOf(row));

    private static CoreHorizontalAlignment ResolveAlignment(Cell cell)
    {
        if (cell.Style.HorizontalAlignment is not CoreHorizontalAlignment.General)
        {
            return cell.Style.HorizontalAlignment;
        }

        // Padrão do Excel: número à direita, texto à esquerda, o resto centralizado.
        return cell.Value.Kind switch
        {
            CellValueKind.Number => CoreHorizontalAlignment.Right,
            CellValueKind.Text => CoreHorizontalAlignment.Left,
            _ => CoreHorizontalAlignment.Center,
        };
    }

    private static Color ToColor(RgbColor color) => Color.FromRgb(color.R, color.G, color.B);

    /// <summary>Alinha a coordenada ao meio do pixel, para a linha de 1px sair nítida.</summary>
    private static double Snap(double value) => Math.Floor(value) + 0.5d;

    private static Point Snap(Point point) => new(Snap(point.X), Snap(point.Y));
}
