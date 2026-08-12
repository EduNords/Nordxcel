using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation;

/// <summary>
/// De onde a fórmula está sendo avaliada. A aba resolve as referências sem
/// qualificação — <c>B5</c> significa <c>B5</c> desta aba — e a célula serve para
/// mensagens de erro e, mais adiante, para a detecção de ciclos.
/// </summary>
public readonly record struct EvaluationScope(string SheetName, CellAddress Cell)
{
    public EvaluationScope(string sheetName) : this(sheetName, CellAddress.Origin)
    {
    }

    /// <summary>Aba de uma referência: a dela própria, ou a atual quando não é qualificada.</summary>
    public string ResolveSheet(string? sheet) => sheet ?? SheetName;

    public override string ToString() => $"{SheetName}!{Cell}";
}
