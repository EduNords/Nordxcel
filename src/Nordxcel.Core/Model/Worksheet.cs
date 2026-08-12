namespace Nordxcel.Core.Model;

/// <summary>
/// Uma aba da pasta de trabalho. O armazenamento é esparso: só existe entrada no
/// dicionário para célula com conteúdo, então uma aba de 16 mil colunas por 1 milhão
/// de linhas custa apenas o que o usuário de fato preencheu.
/// </summary>
public sealed class Worksheet
{
    /// <summary>Largura padrão de coluna, em pixels lógicos.</summary>
    public const double DefaultColumnWidth = 80d;

    /// <summary>Altura padrão de linha, em pixels lógicos.</summary>
    public const double DefaultRowHeight = 20d;

    private static readonly char[] InvalidNameChars = ['[', ']', ':', '*', '?', '/', '\\'];

    private readonly Dictionary<CellAddress, Cell> _cells = [];
    private readonly Dictionary<int, double> _columnWidths = [];
    private readonly Dictionary<int, double> _rowHeights = [];

    private int _frozenRows;
    private int _frozenColumns;

    public Worksheet(string name) => Name = ValidateName(name);

    /// <summary>
    /// Nome da aba. Só a própria biblioteca troca o nome, através de
    /// <see cref="Workbook.RenameWorksheet"/>, que garante a unicidade dentro da pasta.
    /// </summary>
    public string Name { get; internal set; }

    /// <summary>Células com conteúdo, sem ordem definida.</summary>
    public IReadOnlyDictionary<CellAddress, Cell> Cells => _cells;

    public int CellCount => _cells.Count;

    /// <summary>
    /// Linhas congeladas no topo. Em modelo DCF é o que trava o cabeçalho de anos
    /// enquanto se rola a projeção para baixo.
    /// </summary>
    public int FrozenRows
    {
        get => _frozenRows;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, CellAddress.MaxRows);
            _frozenRows = value;
        }
    }

    /// <summary>Colunas congeladas à esquerda, normalmente a coluna de rótulos das linhas.</summary>
    public int FrozenColumns
    {
        get => _frozenColumns;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, CellAddress.MaxColumns);
            _frozenColumns = value;
        }
    }

    /// <summary>Devolve a célula do endereço, ou <see cref="Cell.Empty"/> se estiver vazia.</summary>
    public Cell GetCell(CellAddress address) =>
        _cells.TryGetValue(address, out Cell? cell) ? cell : Cell.Empty;

    /// <summary>Devolve apenas o valor da célula, atalho para o avaliador de fórmulas.</summary>
    public CellValue GetValue(CellAddress address) =>
        _cells.TryGetValue(address, out Cell? cell) ? cell.Value : CellValue.Blank;

    /// <summary>
    /// Grava a célula. Gravar uma célula vazia remove a entrada, para que a planilha
    /// não acumule lixo depois de o usuário apagar conteúdo.
    /// </summary>
    public void SetCell(CellAddress address, Cell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (cell.IsEmpty)
        {
            _cells.Remove(address);
        }
        else
        {
            _cells[address] = cell;
        }
    }

    /// <summary>Grava um valor literal preservando formato e estilo já aplicados à célula.</summary>
    public void SetValue(CellAddress address, CellValue value) =>
        SetCell(address, GetCell(address) with { Formula = null, Value = value });

    public bool ClearCell(CellAddress address) => _cells.Remove(address);

    public void Clear() => _cells.Clear();

    public double GetColumnWidth(int column) =>
        _columnWidths.TryGetValue(column, out double width) ? width : DefaultColumnWidth;

    public void SetColumnWidth(int column, double width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, CellAddress.MaxColumns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        _columnWidths[column] = width;
    }

    public double GetRowHeight(int row) =>
        _rowHeights.TryGetValue(row, out double height) ? height : DefaultRowHeight;

    public void SetRowHeight(int row, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, CellAddress.MaxRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _rowHeights[row] = height;
    }

    /// <summary>
    /// Menor retângulo que contém todas as células preenchidas, ou <c>null</c> se a aba
    /// estiver vazia. Serve para dimensionar barras de rolagem e delimitar a exportação.
    /// </summary>
    public CellRange? GetUsedRange()
    {
        if (_cells.Count == 0)
        {
            return null;
        }

        int minRow = int.MaxValue, minColumn = int.MaxValue;
        int maxRow = int.MinValue, maxColumn = int.MinValue;

        foreach (CellAddress address in _cells.Keys)
        {
            minRow = Math.Min(minRow, address.Row);
            maxRow = Math.Max(maxRow, address.Row);
            minColumn = Math.Min(minColumn, address.Column);
            maxColumn = Math.Max(maxColumn, address.Column);
        }

        return new CellRange(new CellAddress(minRow, minColumn), new CellAddress(maxRow, maxColumn));
    }

    /// <summary>Aplica as regras de nome de aba do Excel, que a exportação para .xlsx exige.</summary>
    internal static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Length > 31)
        {
            throw new ArgumentException("O nome da aba não pode passar de 31 caracteres.", nameof(name));
        }

        if (name.IndexOfAny(InvalidNameChars) >= 0)
        {
            throw new ArgumentException(@"O nome da aba não pode conter [ ] : * ? / \.", nameof(name));
        }

        if (name.StartsWith('\'') || name.EndsWith('\''))
        {
            throw new ArgumentException("O nome da aba não pode começar nem terminar com apóstrofo.", nameof(name));
        }

        return name;
    }

    public override string ToString() => Name;
}
