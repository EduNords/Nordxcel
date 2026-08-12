using Nordxcel.Core.Evaluation;
using Nordxcel.Core.Evaluation.Functions;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Calculation;

/// <summary>
/// Junta pasta de trabalho, fórmulas compiladas, grafo de dependências e avaliador.
/// É por aqui que a interface altera células: o motor descobre o que ficou
/// desatualizado, ordena o recálculo e grava os resultados.
/// <para>
/// Recálculo incremental: alterar uma premissa não recalcula o modelo inteiro,
/// só o que descende dela. Num DCF com sensibilidade isso é a diferença entre
/// resposta instantânea e travar a cada tecla.
/// </para>
/// </summary>
public sealed class CalculationEngine
{
    private readonly Workbook _workbook;
    private readonly FormulaParser _parser;
    private readonly FormulaEvaluator _evaluator;
    private readonly DependencyGraph _graph = new();
    private readonly Dictionary<CellLocation, FormulaNode> _formulas = [];
    private readonly HashSet<CellLocation> _dirty = [];
    private readonly HashSet<CellLocation> _circular = [];

    public CalculationEngine(
        Workbook workbook,
        FormulaSyntax? syntax = null,
        IFunctionRegistry? functions = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        _workbook = workbook;
        _parser = new FormulaParser(syntax);
        _evaluator = new FormulaEvaluator(new WorkbookEvaluationContext(workbook), functions, syntax);

        Rebuild();
    }

    public Workbook Workbook => _workbook;

    public DependencyGraph Dependencies => _graph;

    /// <summary>
    /// Recalcular a cada alteração. Desligar é útil para carregar um arquivo ou
    /// colar um bloco grande, recalculando uma vez só no fim.
    /// </summary>
    public bool AutoRecalculate { get; set; } = true;

    /// <summary>Células que ficaram presas em referência circular no último recálculo.</summary>
    public IReadOnlyCollection<CellLocation> CircularCells => _circular;

    public bool HasCircularReferences => _circular.Count > 0;

    /// <summary>Quantas células com fórmula foram avaliadas no último recálculo.</summary>
    public int LastRecalculatedCount { get; private set; }

    /// <summary>Refaz fórmulas e grafo a partir do conteúdo atual da pasta.</summary>
    public void Rebuild()
    {
        _graph.Clear();
        _formulas.Clear();
        _dirty.Clear();
        _circular.Clear();

        foreach (Worksheet sheet in _workbook.Worksheets)
        {
            // Materializa antes de percorrer: registrar uma fórmula inválida grava na aba.
            var formulaCells = sheet.Cells
                .Where(entry => entry.Value.HasFormula)
                .Select(entry => (Location: new CellLocation(sheet.Name, entry.Key), entry.Value.Formula))
                .ToList();

            foreach ((CellLocation location, string? formula) in formulaCells)
            {
                if (!TryRegisterFormula(location, formula!))
                {
                    continue;
                }

                _dirty.Add(location);
            }
        }

        if (AutoRecalculate)
        {
            Recalculate();
        }
    }

    /// <summary>Marca todas as fórmulas para recálculo e recalcula.</summary>
    public void RecalculateAll()
    {
        foreach (CellLocation location in _formulas.Keys)
        {
            _dirty.Add(location);
        }

        Recalculate();
    }

    /// <summary>Grava a célula, atualiza o grafo e recalcula o que dependia dela.</summary>
    /// <exception cref="FormulaSyntaxException">Quando a fórmula da célula não é válida.</exception>
    public void SetCell(CellLocation location, Cell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        Worksheet sheet = GetWorksheet(location);

        if (cell.HasFormula)
        {
            // Interpreta antes de gravar: fórmula inválida não deve alterar a planilha.
            FormulaNode node = _parser.Parse(cell.Formula!);

            sheet.SetCell(location.Address, cell);
            RegisterParsedFormula(location, node);
        }
        else
        {
            sheet.SetCell(location.Address, cell);
            Unregister(location);
        }

        MarkDirty(location);

        if (AutoRecalculate)
        {
            Recalculate();
        }
    }

    /// <summary>Grava uma fórmula preservando formato e estilo já aplicados à célula.</summary>
    public void SetFormula(CellLocation location, string formula)
    {
        Cell current = GetWorksheet(location).GetCell(location.Address);

        SetCell(location, Cell.FromFormula(formula) with
        {
            NumberFormat = current.NumberFormat,
            Style = current.Style,
        });
    }

    /// <summary>Grava um valor literal preservando formato e estilo já aplicados à célula.</summary>
    public void SetValue(CellLocation location, CellValue value)
    {
        Cell current = GetWorksheet(location).GetCell(location.Address);

        SetCell(location, current with { Formula = null, Value = value });
    }

    /// <summary>Esvazia a célula, mantendo o que dependia dela marcado para recálculo.</summary>
    public void ClearCell(CellLocation location)
    {
        GetWorksheet(location).ClearCell(location.Address);
        Unregister(location);
        MarkDirty(location);

        if (AutoRecalculate)
        {
            Recalculate();
        }
    }

    public void MarkDirty(CellLocation location) => _dirty.Add(location);

    /// <summary>
    /// Recalcula o que está desatualizado, em ordem topológica. Células que ficam
    /// presas em ciclo — ou que dependem de um — recebem <c>#CIRC!</c>.
    /// </summary>
    public void Recalculate()
    {
        LastRecalculatedCount = 0;

        if (_dirty.Count == 0)
        {
            return;
        }

        HashSet<CellLocation> affected = CollectAffected(_dirty);
        _dirty.Clear();

        Dictionary<CellLocation, HashSet<CellLocation>> dependents = MapDependentsWithin(affected);
        Dictionary<CellLocation, int> pending = CountPrecedentsWithin(affected, dependents);

        var ready = new Queue<CellLocation>();

        foreach (CellLocation location in affected)
        {
            if (pending[location] == 0)
            {
                ready.Enqueue(location);
            }
        }

        int ordered = 0;

        while (ready.Count > 0)
        {
            CellLocation location = ready.Dequeue();
            ordered++;

            Evaluate(location);

            foreach (CellLocation dependent in dependents[location])
            {
                if (--pending[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        if (ordered == affected.Count)
        {
            return;
        }

        // O que sobrou não pôde ser ordenado: está num ciclo ou depende de um.
        foreach (CellLocation location in affected)
        {
            if (pending[location] > 0 && _formulas.ContainsKey(location))
            {
                _circular.Add(location);
                Write(location, CellValue.Error(CellErrorType.Circular));
            }
        }
    }

    private void Evaluate(CellLocation location)
    {
        if (!_formulas.TryGetValue(location, out FormulaNode? node))
        {
            return;
        }

        _circular.Remove(location);

        CellValue value = _evaluator.Evaluate(node, new EvaluationScope(location.SheetName, location.Address));

        Write(location, value);
        LastRecalculatedCount++;
    }

    private void Write(CellLocation location, CellValue value)
    {
        if (!_workbook.TryGetWorksheet(location.SheetName, out Worksheet? sheet))
        {
            return;
        }

        sheet.SetCell(location.Address, sheet.GetCell(location.Address).WithValue(value));
    }

    /// <summary>Células alteradas mais tudo que descende delas.</summary>
    private HashSet<CellLocation> CollectAffected(IEnumerable<CellLocation> seeds)
    {
        var affected = new HashSet<CellLocation>();
        var pending = new Stack<CellLocation>();

        foreach (CellLocation seed in seeds)
        {
            if (affected.Add(seed))
            {
                pending.Push(seed);
            }
        }

        var buffer = new HashSet<CellLocation>();

        while (pending.Count > 0)
        {
            CellLocation current = pending.Pop();

            buffer.Clear();
            _graph.CollectDirectDependents(current, buffer);

            foreach (CellLocation dependent in buffer)
            {
                // O conjunto de visitados também é o que impede um ciclo de girar aqui.
                if (affected.Add(dependent))
                {
                    pending.Push(dependent);
                }
            }
        }

        return affected;
    }

    private Dictionary<CellLocation, HashSet<CellLocation>> MapDependentsWithin(HashSet<CellLocation> affected)
    {
        var map = new Dictionary<CellLocation, HashSet<CellLocation>>(affected.Count);

        foreach (CellLocation location in affected)
        {
            var dependents = new HashSet<CellLocation>();
            _graph.CollectDirectDependents(location, dependents);
            dependents.IntersectWith(affected);

            map[location] = dependents;
        }

        return map;
    }

    private static Dictionary<CellLocation, int> CountPrecedentsWithin(
        HashSet<CellLocation> affected,
        Dictionary<CellLocation, HashSet<CellLocation>> dependents)
    {
        var pending = new Dictionary<CellLocation, int>(affected.Count);

        foreach (CellLocation location in affected)
        {
            pending[location] = 0;
        }

        foreach (HashSet<CellLocation> targets in dependents.Values)
        {
            foreach (CellLocation target in targets)
            {
                pending[target]++;
            }
        }

        return pending;
    }

    /// <summary>
    /// Interpreta e registra a fórmula. Se ela não for válida — o que só acontece
    /// com arquivo adulterado, já que a entrada normal recusa antes de gravar — a
    /// célula fica com <c>#NOME?</c> em vez de derrubar o carregamento.
    /// </summary>
    private bool TryRegisterFormula(CellLocation location, string formula)
    {
        try
        {
            RegisterParsedFormula(location, _parser.Parse(formula));
            return true;
        }
        catch (FormulaSyntaxException)
        {
            Write(location, CellValue.Error(CellErrorType.Name));
            return false;
        }
    }

    private void RegisterParsedFormula(CellLocation location, FormulaNode node)
    {
        _formulas[location] = node;

        var cells = new List<CellLocation>();
        var ranges = new List<RangeLocation>();

        FormulaDependencyScanner.Collect(node, location.SheetName, cells, ranges);

        _graph.SetPrecedents(location, cells, ranges);
    }

    private void Unregister(CellLocation location)
    {
        _formulas.Remove(location);
        _graph.RemoveCell(location);
        _circular.Remove(location);
    }

    private Worksheet GetWorksheet(CellLocation location) => _workbook[location.SheetName];
}
