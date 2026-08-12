using Nordxcel.Core.Model;

namespace Nordxcel.Core.Calculation;

/// <summary>
/// Quem depende de quem. Guarda, para cada célula com fórmula, as células e os
/// intervalos que ela lê, e o caminho inverso: dada uma célula alterada, quem
/// precisa ser recalculado.
/// <para>
/// Intervalos não são expandidos em arestas célula a célula — <c>SOMA(B2:B1000)</c>
/// vira uma aresta só. Em troca, descobrir os dependentes de uma célula alterada
/// exige varrer os intervalos registrados naquela aba, o que é barato porque a
/// quantidade de fórmulas com intervalo é pequena perto da quantidade de células.
/// </para>
/// </summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<CellLocation, Precedents> _precedents = [];
    private readonly Dictionary<CellLocation, HashSet<CellLocation>> _dependentsByCell = [];
    private readonly Dictionary<string, List<RangeEdge>> _rangeEdges = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Quantidade de células com dependências registradas.</summary>
    public int TrackedCellCount => _precedents.Count;

    /// <summary>
    /// Substitui as dependências de uma célula. Chamado toda vez que a fórmula dela
    /// muda; as arestas antigas são removidas antes.
    /// </summary>
    public void SetPrecedents(
        CellLocation cell,
        IReadOnlyList<CellLocation> precedentCells,
        IReadOnlyList<RangeLocation> precedentRanges)
    {
        ArgumentNullException.ThrowIfNull(precedentCells);
        ArgumentNullException.ThrowIfNull(precedentRanges);

        RemoveCell(cell);

        if (precedentCells.Count == 0 && precedentRanges.Count == 0)
        {
            // Fórmula sem referência nenhuma, como =1+1: nada a rastrear.
            _precedents[cell] = Precedents.Empty;
            return;
        }

        _precedents[cell] = new Precedents(precedentCells, precedentRanges);

        foreach (CellLocation precedent in precedentCells)
        {
            if (!_dependentsByCell.TryGetValue(precedent, out HashSet<CellLocation>? dependents))
            {
                dependents = [];
                _dependentsByCell[precedent] = dependents;
            }

            dependents.Add(cell);
        }

        foreach (RangeLocation range in precedentRanges)
        {
            if (!_rangeEdges.TryGetValue(range.SheetName, out List<RangeEdge>? edges))
            {
                edges = [];
                _rangeEdges[range.SheetName] = edges;
            }

            edges.Add(new RangeEdge(range.Range, cell));
        }
    }

    /// <summary>Tira a célula do grafo, junto com todas as arestas que saem dela.</summary>
    public bool RemoveCell(CellLocation cell)
    {
        if (!_precedents.Remove(cell, out Precedents? previous))
        {
            return false;
        }

        foreach (CellLocation precedent in previous.Cells)
        {
            if (_dependentsByCell.TryGetValue(precedent, out HashSet<CellLocation>? dependents) &&
                dependents.Remove(cell) &&
                dependents.Count == 0)
            {
                _dependentsByCell.Remove(precedent);
            }
        }

        foreach (RangeLocation range in previous.Ranges)
        {
            if (_rangeEdges.TryGetValue(range.SheetName, out List<RangeEdge>? edges))
            {
                edges.RemoveAll(edge => edge.Dependent.Equals(cell));

                if (edges.Count == 0)
                {
                    _rangeEdges.Remove(range.SheetName);
                }
            }
        }

        return true;
    }

    public void Clear()
    {
        _precedents.Clear();
        _dependentsByCell.Clear();
        _rangeEdges.Clear();
    }

    public IReadOnlyList<CellLocation> GetPrecedentCells(CellLocation cell) =>
        _precedents.TryGetValue(cell, out Precedents? precedents) ? precedents.Cells : [];

    public IReadOnlyList<RangeLocation> GetPrecedentRanges(CellLocation cell) =>
        _precedents.TryGetValue(cell, out Precedents? precedents) ? precedents.Ranges : [];

    /// <summary>
    /// Acrescenta ao conjunto informado as células que leem esta diretamente, seja
    /// por referência direta ou por estarem dentro de um intervalo referenciado.
    /// </summary>
    public void CollectDirectDependents(CellLocation cell, ISet<CellLocation> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (_dependentsByCell.TryGetValue(cell, out HashSet<CellLocation>? dependents))
        {
            foreach (CellLocation dependent in dependents)
            {
                into.Add(dependent);
            }
        }

        if (!_rangeEdges.TryGetValue(cell.SheetName, out List<RangeEdge>? edges))
        {
            return;
        }

        foreach (RangeEdge edge in edges)
        {
            if (edge.Range.Contains(cell.Address))
            {
                into.Add(edge.Dependent);
            }
        }
    }

    /// <summary>Versão de conveniência de <see cref="CollectDirectDependents"/>, sem repetições.</summary>
    public IReadOnlySet<CellLocation> GetDirectDependents(CellLocation cell)
    {
        var dependents = new HashSet<CellLocation>();
        CollectDirectDependents(cell, dependents);

        return dependents;
    }

    private sealed record Precedents(IReadOnlyList<CellLocation> Cells, IReadOnlyList<RangeLocation> Ranges)
    {
        public static readonly Precedents Empty = new([], []);
    }

    private readonly record struct RangeEdge(CellRange Range, CellLocation Dependent);
}
