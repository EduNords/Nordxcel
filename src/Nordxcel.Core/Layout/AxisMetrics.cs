namespace Nordxcel.Core.Layout;

/// <summary>
/// Converte entre índice de linha/coluna e posição em pixels.
/// <para>
/// Somar as larguras uma a uma seria O(n) por consulta, e a grade consulta isso
/// dezenas de vezes por quadro. Como quase toda coluna tem a largura padrão, a
/// posição sai de uma fórmula — <c>índice × padrão</c> — mais a soma acumulada das
/// poucas que fogem dela, encontrada por busca binária.
/// </para>
/// </summary>
public sealed class AxisMetrics
{
    private readonly double _defaultSize;
    private readonly int _count;
    private readonly IReadOnlyDictionary<int, double> _overrides;

    private int[] _indexes = [];
    private double[] _sizes = [];

    /// <summary>Soma dos desvios em relação ao padrão dos <c>i</c> primeiros ajustes.</summary>
    private double[] _cumulativeDelta = [];

    private bool _cacheValid;

    public AxisMetrics(double defaultSize, int count, IReadOnlyDictionary<int, double> overrides)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentNullException.ThrowIfNull(overrides);

        _defaultSize = defaultSize;
        _count = count;
        _overrides = overrides;
    }

    public double DefaultSize => _defaultSize;

    public int Count => _count;

    /// <summary>Avisa que as dimensões mudaram e o cache precisa ser refeito.</summary>
    public void Invalidate() => _cacheValid = false;

    public double SizeOf(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        return _overrides.TryGetValue(index, out double size) ? size : _defaultSize;
    }

    /// <summary>Posição da borda inicial do índice.</summary>
    public double OffsetOf(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        EnsureCache();

        return index * _defaultSize + _cumulativeDelta[CountBefore(index)];
    }

    /// <summary>Tamanho total do eixo, do índice zero até o fim.</summary>
    public double TotalSize => OffsetOf(_count);

    /// <summary>Índice que ocupa a posição informada, limitado ao intervalo válido.</summary>
    public int IndexAt(double position)
    {
        if (position <= 0d)
        {
            return 0;
        }

        EnsureCache();

        int index;

        if (_indexes.Length == 0)
        {
            index = (int)(position / _defaultSize);
        }
        else
        {
            int segment = LastSegmentStartingAtOrBefore(position);

            if (segment < 0)
            {
                index = (int)(position / _defaultSize);
            }
            else
            {
                double start = StartOfOverride(segment);

                if (position < start + _sizes[segment])
                {
                    return Math.Min(_indexes[segment], _count - 1);
                }

                double afterOverride = start + _sizes[segment];
                index = _indexes[segment] + 1 + (int)((position - afterOverride) / _defaultSize);
            }
        }

        return Math.Clamp(index, 0, _count - 1);
    }

    /// <summary>Último índice ainda visível a partir de uma posição e uma largura de viewport.</summary>
    public int LastIndexIn(double position, double extent) =>
        IndexAt(position + Math.Max(extent, 0d));

    private void EnsureCache()
    {
        if (_cacheValid)
        {
            return;
        }

        _indexes = new int[_overrides.Count];
        _sizes = new double[_overrides.Count];

        int i = 0;

        foreach (KeyValuePair<int, double> entry in _overrides)
        {
            _indexes[i] = entry.Key;
            _sizes[i] = entry.Value;
            i++;
        }

        Array.Sort(_indexes, _sizes);

        _cumulativeDelta = new double[_indexes.Length + 1];

        for (int j = 0; j < _indexes.Length; j++)
        {
            _cumulativeDelta[j + 1] = _cumulativeDelta[j] + (_sizes[j] - _defaultSize);
        }

        _cacheValid = true;
    }

    /// <summary>Quantos ajustes estão antes do índice informado.</summary>
    private int CountBefore(int index)
    {
        int position = Array.BinarySearch(_indexes, index);

        return position >= 0 ? position : ~position;
    }

    private double StartOfOverride(int segment) =>
        _indexes[segment] * _defaultSize + _cumulativeDelta[segment];

    /// <summary>Último ajuste cuja borda inicial não passa da posição, ou -1.</summary>
    private int LastSegmentStartingAtOrBefore(double position)
    {
        int low = 0;
        int high = _indexes.Length - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = (low + high) / 2;

            if (StartOfOverride(middle) <= position)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }
}
