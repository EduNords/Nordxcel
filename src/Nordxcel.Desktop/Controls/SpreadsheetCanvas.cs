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
/// </summary>
public sealed class SpreadsheetCanvas : Control
{
    private readonly GridTheme _theme = GridTheme.Default;
    private readonly TextCache _textCache = new();
    private readonly CellFormatter _formatter = new();

    private SheetGeometry? _geometry;
    private string _sheetName = string.Empty;
    private CalculationEngine? _engine;
    private double _scrollX;
    private double _scrollY;
    private bool _draggingSelection;

    public SpreadsheetCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    /// <summary>Disparado quando a rolagem muda por dentro, como na roda do mouse.</summary>
    public event EventHandler? ScrollChanged;

    public event EventHandler? SelectionChanged;

    public event EventHandler<CellEditRequestedEventArgs>? EditRequested;

    /// <summary>Disparado quando o conteúdo da planilha muda por aqui, como no Delete.</summary>
    public event EventHandler? ContentChanged;

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
        set => SetScroll(ref _scrollX, value);
    }

    public double ScrollY
    {
        get => _scrollY;
        set => SetScroll(ref _scrollY, value);
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

        return geometry is null ? null : CellRect(geometry, address.Row, address.Column);
    }

    /// <summary>Avisa que o conteúdo mudou e a grade precisa ser redesenhada.</summary>
    public void Refresh()
    {
        _geometry?.Synchronize();
        InvalidateVisual();
    }

    /// <summary>Rola o necessário para a célula ficar inteira dentro da janela.</summary>
    public void ScrollIntoView(CellAddress address)
    {
        SheetGeometry? geometry = ResolveGeometry();

        if (geometry is null || ViewportWidth <= 0d || ViewportHeight <= 0d)
        {
            return;
        }

        double left = geometry.Columns.OffsetOf(address.Column);
        double right = left + geometry.Columns.SizeOf(address.Column);
        double top = geometry.Rows.OffsetOf(address.Row);
        double bottom = top + geometry.Rows.SizeOf(address.Row);

        double x = _scrollX;
        double y = _scrollY;

        if (left < x)
        {
            x = left;
        }
        else if (right > x + ViewportWidth)
        {
            x = right - ViewportWidth;
        }

        if (top < y)
        {
            y = top;
        }
        else if (bottom > y + ViewportHeight)
        {
            y = bottom - ViewportHeight;
        }

        if (Math.Abs(x - _scrollX) < 0.01d && Math.Abs(y - _scrollY) < 0.01d)
        {
            return;
        }

        _scrollX = Math.Max(0d, x);
        _scrollY = Math.Max(0d, y);

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

        int column = geometry.Columns.IndexAt(point.X - _theme.RowHeaderWidth + _scrollX);
        int row = geometry.Rows.IndexAt(point.Y - _theme.ColumnHeaderHeight + _scrollY);

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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        PointerPoint point = e.GetCurrentPoint(this);

        if (!point.Properties.IsLeftButtonPressed)
        {
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

        if (!_draggingSelection)
        {
            return;
        }

        SpreadsheetHit hit = HitTest(e.GetPosition(this));

        if (!hit.IsCell)
        {
            return;
        }

        Selection.ExtendTo(hit.Address);
        NotifySelection();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_draggingSelection)
        {
            return;
        }

        _draggingSelection = false;
        e.Pointer.Capture(null);
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

        ScrollY = Math.Clamp(ScrollY - (e.Delta.Y * verticalStep), 0d, Math.Max(0d, extentHeight - ViewportHeight));
        ScrollX = Math.Clamp(ScrollX - (e.Delta.X * horizontalStep), 0d, Math.Max(0d, extentWidth - ViewportWidth));

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

    private void SetScroll(ref double field, double value)
    {
        double clamped = Math.Max(0d, value);

        if (Math.Abs(clamped - field) < 0.01d)
        {
            return;
        }

        field = clamped;
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

        var dataArea = new Rect(
            _theme.RowHeaderWidth,
            _theme.ColumnHeaderHeight,
            ViewportWidth,
            ViewportHeight);

        VisibleRange range = geometry.GetVisibleRange(_scrollX, _scrollY, ViewportWidth, ViewportHeight);

        using (context.PushClip(dataArea))
        {
            context.FillRectangle(_theme.CellBackground, dataArea);
            DrawFills(context, geometry, range);
            DrawSelectionFill(context, geometry, range);
            DrawGridLines(context, geometry, range);
            DrawContents(context, geometry, range);
            DrawBorders(context, geometry, range);
            DrawSelectionOutline(context, geometry);
        }

        DrawColumnHeaders(context, geometry, range);
        DrawRowHeaders(context, geometry, range);
        DrawCorner(context);
    }

    private void DrawFills(DrawingContext context, SheetGeometry geometry, VisibleRange range)
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
                    CellRect(geometry, row, column));
            }
        }
    }

    private void DrawSelectionFill(DrawingContext context, SheetGeometry geometry, VisibleRange range)
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

                context.FillRectangle(_theme.SelectionFill, CellRect(geometry, row, column));
            }
        }
    }

    private void DrawSelectionOutline(DrawingContext context, SheetGeometry geometry)
    {
        CellRange selected = Selection.Range;

        Rect start = CellRect(geometry, selected.Start.Row, selected.Start.Column);
        Rect end = CellRect(geometry, selected.End.Row, selected.End.Column);

        var outline = new Rect(start.X, start.Y, end.Right - start.X, end.Bottom - start.Y);

        context.DrawRectangle(null, _theme.SelectionBorder, outline);

        if (!Selection.IsSingleCell)
        {
            context.DrawRectangle(null, _theme.GridLine, CellRect(geometry, Selection.Active.Row, Selection.Active.Column));
        }
    }

    private void DrawGridLines(DrawingContext context, SheetGeometry geometry, VisibleRange range)
    {
        double right = _theme.RowHeaderWidth + ViewportWidth;
        double bottom = _theme.ColumnHeaderHeight + ViewportHeight;

        for (int column = range.FirstColumn; column <= range.LastColumn + 1; column++)
        {
            double x = Snap(ColumnLeft(geometry, column));

            if (x < _theme.RowHeaderWidth || x > right)
            {
                continue;
            }

            context.DrawLine(_theme.GridLine, new Point(x, _theme.ColumnHeaderHeight), new Point(x, bottom));
        }

        for (int row = range.FirstRow; row <= range.LastRow + 1; row++)
        {
            double y = Snap(RowTop(geometry, row));

            if (y < _theme.ColumnHeaderHeight || y > bottom)
            {
                continue;
            }

            context.DrawLine(_theme.GridLine, new Point(_theme.RowHeaderWidth, y), new Point(right, y));
        }
    }

    private void DrawContents(DrawingContext context, SheetGeometry geometry, VisibleRange range)
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

                DrawCellText(context, geometry, sheet, address, cell, range);
            }
        }
    }

    private void DrawCellText(
        DrawingContext context,
        SheetGeometry geometry,
        Worksheet sheet,
        CellAddress address,
        Cell cell,
        VisibleRange range)
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

        Rect cellRect = CellRect(geometry, address.Row, address.Column);
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
            clip = ExtendOverEmptyNeighbours(geometry, sheet, address, cellRect, text.Width, range);
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
        VisibleRange range)
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

    private void DrawBorders(DrawingContext context, SheetGeometry geometry, VisibleRange range)
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

                Rect rect = CellRect(geometry, row, column);

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

    private void DrawColumnHeaders(DrawingContext context, SheetGeometry geometry, VisibleRange range)
    {
        var header = new Rect(_theme.RowHeaderWidth, 0d, ViewportWidth, _theme.ColumnHeaderHeight);
        CellRange selected = Selection.Range;

        using (context.PushClip(header))
        {
            context.FillRectangle(_theme.HeaderBackground, header);

            for (int column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                double left = ColumnLeft(geometry, column);
                double width = geometry.Columns.SizeOf(column);

                bool highlighted = column >= selected.Start.Column && column <= selected.End.Column;

                if (highlighted)
                {
                    context.FillRectangle(
                        _theme.HeaderSelectedBackground,
                        new Rect(left, 0d, width, _theme.ColumnHeaderHeight));
                }

                FormattedText text = HeaderText(CellAddress.ColumnToName(column), highlighted);

                context.DrawText(text, new Point(
                    left + ((width - text.Width) / 2d),
                    (_theme.ColumnHeaderHeight - text.Height) / 2d));

                double edge = Snap(left + width);
                context.DrawLine(_theme.HeaderLine, new Point(edge, 0d), new Point(edge, _theme.ColumnHeaderHeight));
            }

            double bottom = Snap(_theme.ColumnHeaderHeight);
            context.DrawLine(_theme.HeaderLine, new Point(header.X, bottom), new Point(header.Right, bottom));
        }
    }

    private void DrawRowHeaders(DrawingContext context, SheetGeometry geometry, VisibleRange range)
    {
        var header = new Rect(0d, _theme.ColumnHeaderHeight, _theme.RowHeaderWidth, ViewportHeight);
        CellRange selected = Selection.Range;

        using (context.PushClip(header))
        {
            context.FillRectangle(_theme.HeaderBackground, header);

            for (int row = range.FirstRow; row <= range.LastRow; row++)
            {
                double top = RowTop(geometry, row);
                double height = geometry.Rows.SizeOf(row);

                bool highlighted = row >= selected.Start.Row && row <= selected.End.Row;

                if (highlighted)
                {
                    context.FillRectangle(
                        _theme.HeaderSelectedBackground,
                        new Rect(0d, top, _theme.RowHeaderWidth, height));
                }

                FormattedText text = HeaderText(
                    (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    highlighted);

                context.DrawText(text, new Point(
                    _theme.RowHeaderWidth - text.Width - 6d,
                    top + ((height - text.Height) / 2d)));

                double edge = Snap(top + height);
                context.DrawLine(_theme.HeaderLine, new Point(0d, edge), new Point(_theme.RowHeaderWidth, edge));
            }

            double right = Snap(_theme.RowHeaderWidth);
            context.DrawLine(_theme.HeaderLine, new Point(right, header.Y), new Point(right, header.Bottom));
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

    private double ColumnLeft(SheetGeometry geometry, int column) =>
        _theme.RowHeaderWidth + geometry.Columns.OffsetOf(column) - _scrollX;

    private double RowTop(SheetGeometry geometry, int row) =>
        _theme.ColumnHeaderHeight + geometry.Rows.OffsetOf(row) - _scrollY;

    private Rect CellRect(SheetGeometry geometry, int row, int column) => new(
        ColumnLeft(geometry, column),
        RowTop(geometry, row),
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
