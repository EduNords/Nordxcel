namespace Nordxcel.Core.Editing;

/// <summary>
/// Um passo de desfazer: um lote de células que o usuário percebe como uma ação
/// só — digitar um valor, colar um bloco de cinquenta células, aplicar negrito
/// numa seleção inteira. Desfazer o passo reverte o lote de uma vez, não célula
/// a célula.
/// </summary>
public sealed class UndoStep
{
    public UndoStep(string description, IReadOnlyList<CellEdit> edits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(edits);

        Description = description;
        Edits = edits;
    }

    /// <summary>Rótulo curto para mostrar em "Desfazer: {Description}", como o Excel faz.</summary>
    public string Description { get; }

    public IReadOnlyList<CellEdit> Edits { get; }
}

/// <summary>
/// Pilha de desfazer/refazer sobre células. Não sabe nada sobre <c>Worksheet</c>
/// nem <c>CalculationEngine</c> — só guarda os passos; quem aplica de volta
/// <see cref="CellEdit.Before"/> ou <see cref="CellEdit.After"/> em cada célula é
/// quem gerencia o motor de cálculo, porque desfazer uma fórmula precisa
/// recalcular o que depende dela.
/// </summary>
public sealed class UndoManager
{
    /// <summary>
    /// Limite do histórico. Cada passo guarda referências a <c>Cell</c>, que são
    /// baratas (records imutáveis, reaproveitados), mas um histórico sem limite
    /// ainda cresceria para sempre numa sessão longa.
    /// </summary>
    public const int DefaultMaxSteps = 200;

    private readonly List<UndoStep> _undoStack = [];
    private readonly List<UndoStep> _redoStack = [];
    private readonly int _maxSteps;

    public UndoManager(int maxSteps = DefaultMaxSteps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxSteps, 0);
        _maxSteps = maxSteps;
    }

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Descrição do próximo passo a desfazer, para um item de menu tipo "Desfazer Colar".</summary>
    public string? NextUndoDescription => _undoStack.Count > 0 ? _undoStack[^1].Description : null;

    public string? NextRedoDescription => _redoStack.Count > 0 ? _redoStack[^1].Description : null;

    /// <summary>Quantidade de passos disponíveis para desfazer.</summary>
    public int UndoCount => _undoStack.Count;

    /// <summary>
    /// Empilha um passo novo. Uma ação nova sempre invalida o que dava para
    /// refazer, como no Excel: não existe "refazer" depois de editar algo.
    /// </summary>
    public void Push(UndoStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.Edits.Count == 0)
        {
            // Nada mudou de fato (ex.: aplicar o mesmo estilo que já estava lá);
            // registrar um passo vazio só criaria um "desfazer" que não faz nada.
            return;
        }

        _undoStack.Add(step);
        _redoStack.Clear();

        if (_undoStack.Count > _maxSteps)
        {
            _undoStack.RemoveAt(0);
        }
    }

    /// <summary>Move o próximo passo do topo do desfazer para o refazer, e o devolve para quem for aplicá-lo.</summary>
    public UndoStep? Undo()
    {
        if (_undoStack.Count == 0)
        {
            return null;
        }

        UndoStep step = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(step);

        return step;
    }

    /// <inheritdoc cref="Undo"/>
    public UndoStep? Redo()
    {
        if (_redoStack.Count == 0)
        {
            return null;
        }

        UndoStep step = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(step);

        return step;
    }

    /// <summary>Esvazia os dois lados — usado ao abrir um arquivo novo, por exemplo.</summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
