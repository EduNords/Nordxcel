using Nordxcel.Core.Model;

namespace Nordxcel.Core.Layout;

/// <summary>Faixa de linhas e colunas que aparece na tela.</summary>
public readonly record struct VisibleRange(int FirstRow, int LastRow, int FirstColumn, int LastColumn)
{
    public int RowCount => LastRow - FirstRow + 1;

    public int ColumnCount => LastColumn - FirstColumn + 1;

    public bool Contains(CellAddress address) =>
        address.Row >= FirstRow && address.Row <= LastRow &&
        address.Column >= FirstColumn && address.Column <= LastColumn;
}

/// <summary>
/// Geometria de uma aba: onde cada linha e coluna começa, e quais delas cabem na
/// área visível. É o que permite desenhar só o que está na tela em vez de percorrer
/// um milhão de linhas.
/// </summary>
public sealed class SheetGeometry
{
    private readonly Worksheet _sheet;
    private int _observedLayoutVersion = -1;

    public SheetGeometry(Worksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        _sheet = sheet;

        Columns = new AxisMetrics(Worksheet.DefaultColumnWidth, CellAddress.MaxColumns, sheet.ColumnWidths);
        Rows = new AxisMetrics(Worksheet.DefaultRowHeight, CellAddress.MaxRows, sheet.RowHeights);
    }

    public Worksheet Sheet => _sheet;

    public AxisMetrics Columns { get; }

    public AxisMetrics Rows { get; }

    /// <summary>Refaz os caches se alguma dimensão mudou desde a última consulta.</summary>
    public void Synchronize()
    {
        if (_observedLayoutVersion == _sheet.LayoutVersion)
        {
            return;
        }

        Columns.Invalidate();
        Rows.Invalidate();
        _observedLayoutVersion = _sheet.LayoutVersion;
    }

    /// <summary>Linhas e colunas que aparecem na janela de rolagem informada.</summary>
    public VisibleRange GetVisibleRange(double scrollX, double scrollY, double width, double height)
    {
        Synchronize();

        return new VisibleRange(
            Rows.IndexAt(scrollY),
            Rows.LastIndexIn(scrollY, height),
            Columns.IndexAt(scrollX),
            Columns.LastIndexIn(scrollX, width));
    }

    /// <summary>Retângulo de uma célula no espaço da planilha, antes de descontar a rolagem.</summary>
    public (double X, double Y, double Width, double Height) GetCellBounds(CellAddress address)
    {
        Synchronize();

        return (
            Columns.OffsetOf(address.Column),
            Rows.OffsetOf(address.Row),
            Columns.SizeOf(address.Column),
            Rows.SizeOf(address.Row));
    }

    /// <summary>
    /// Extensão rolável. Não é a planilha inteira: uma barra que cobrisse um milhão
    /// de linhas teria um cursor de um pixel numa planilha com dez linhas preenchidas.
    /// A regra é a do Excel — a área usada com folga, e sempre mais meia tela além de
    /// onde a rolagem já está, para nunca haver um fim artificial.
    /// </summary>
    public (double Width, double Height) GetScrollExtent(
        double scrollX,
        double scrollY,
        double viewportWidth,
        double viewportHeight)
    {
        Synchronize();

        const int columnMargin = 10;
        const int rowMargin = 50;

        CellRange? used = _sheet.GetUsedRange();

        int lastColumn = Math.Min((used?.End.Column ?? 0) + columnMargin, CellAddress.MaxColumns - 1);
        int lastRow = Math.Min((used?.End.Row ?? 0) + rowMargin, CellAddress.MaxRows - 1);

        double usedWidth = Columns.OffsetOf(lastColumn) + Columns.SizeOf(lastColumn);
        double usedHeight = Rows.OffsetOf(lastRow) + Rows.SizeOf(lastRow);

        double width = Math.Max(usedWidth, scrollX + (viewportWidth * 1.5d));
        double height = Math.Max(usedHeight, scrollY + (viewportHeight * 1.5d));

        return (
            Math.Clamp(width, viewportWidth, Columns.TotalSize),
            Math.Clamp(height, viewportHeight, Rows.TotalSize));
    }
}
