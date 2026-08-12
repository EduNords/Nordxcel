using System;
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

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// Desenha a planilha. Só percorre as células que estão na área visível, então o
/// custo de um quadro depende do tamanho da janela e não do tamanho do modelo:
/// uma aba com um milhão de linhas desenha tão rápido quanto uma com dez.
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

    /// <summary>Disparado quando a rolagem muda por dentro, como na roda do mouse.</summary>
    public event EventHandler? ScrollChanged;

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
            InvalidateVisual();
        }
    }

    public double ScrollX
    {
        get => _scrollX;
        set
        {
            double clamped = Math.Max(0d, value);

            if (Math.Abs(clamped - _scrollX) < 0.01d)
            {
                return;
            }

            _scrollX = clamped;
            InvalidateVisual();
        }
    }

    public double ScrollY
    {
        get => _scrollY;
        set
        {
            double clamped = Math.Max(0d, value);

            if (Math.Abs(clamped - _scrollY) < 0.01d)
            {
                return;
            }

            _scrollY = clamped;
            InvalidateVisual();
        }
    }

    /// <summary>Largura da área de dados, já sem o cabeçalho de linhas.</summary>
    public double ViewportWidth => Math.Max(0d, Bounds.Width - _theme.RowHeaderWidth);

    /// <summary>Altura da área de dados, já sem o cabeçalho de colunas.</summary>
    public double ViewportHeight => Math.Max(0d, Bounds.Height - _theme.ColumnHeaderHeight);

    /// <summary>Não se chama <c>Theme</c> porque o Avalonia já usa esse nome em <c>StyledElement</c>.</summary>
    public GridTheme GridTheme => _theme;

    /// <summary>Tamanho total rolável da aba atual.</summary>
    public (double Width, double Height) GetScrollExtent()
    {
        SheetGeometry? geometry = ResolveGeometry();

        return geometry is null
            ? (ViewportWidth, ViewportHeight)
            : geometry.GetScrollExtent(_scrollX, _scrollY, ViewportWidth, ViewportHeight);
    }

    /// <summary>Avisa que o conteúdo mudou e a grade precisa ser redesenhada.</summary>
    public void Refresh()
    {
        _geometry?.Synchronize();
        InvalidateVisual();
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

        (double extentWidth, double extentHeight) = geometry.GetScrollExtent(_scrollX, _scrollY, ViewportWidth, ViewportHeight);

        ScrollY = Math.Clamp(ScrollY - (e.Delta.Y * verticalStep), 0d, Math.Max(0d, extentHeight - ViewportHeight));
        ScrollX = Math.Clamp(ScrollX - (e.Delta.X * horizontalStep), 0d, Math.Max(0d, extentWidth - ViewportWidth));

        ScrollChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

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
            DrawGridLines(context, geometry, range);
            DrawContents(context, geometry, range);
            DrawBorders(context, geometry, range);
        }

        DrawColumnHeaders(context, geometry, range);
        DrawRowHeaders(context, geometry, range);
        DrawCorner(context);
    }

    // ------------------------------------------------------------- conteúdo

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
            text = _textCache.Get(
                new string('#', Math.Max(1, (int)(available / Math.Max(1d, text.Width / formatted.Text.Length)))),
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

        using (context.PushClip(header))
        {
            context.FillRectangle(_theme.HeaderBackground, header);

            for (int column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                double left = ColumnLeft(geometry, column);
                double width = geometry.Columns.SizeOf(column);

                FormattedText text = HeaderText(CellAddress.ColumnToName(column));

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

        using (context.PushClip(header))
        {
            context.FillRectangle(_theme.HeaderBackground, header);

            for (int row = range.FirstRow; row <= range.LastRow; row++)
            {
                double top = RowTop(geometry, row);
                double height = geometry.Rows.SizeOf(row);

                FormattedText text = HeaderText((row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

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

    private FormattedText HeaderText(string text) =>
        _textCache.Get(text, _theme.FontFamily, _theme.HeaderFontSize, false, false, ToColor(HeaderColor));

    // --------------------------------------------------------------- apoio

    private static readonly RgbColor HeaderColor = new(68, 68, 68);

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
