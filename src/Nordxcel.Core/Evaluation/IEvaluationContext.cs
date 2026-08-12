using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation;

/// <summary>
/// De onde o avaliador lê os valores das células. Fica atrás de uma interface para
/// que o Data Table possa avaliar o modelo sobre um estado substituído — inputs
/// trocados, planilha real intacta — sem que o avaliador saiba disso.
/// </summary>
public interface IEvaluationContext
{
    /// <summary>Verdadeiro se a aba existe. Referência a aba inexistente vira <c>#REF!</c>.</summary>
    bool ContainsSheet(string sheetName);

    /// <summary>Valor de uma célula. Célula vazia devolve <see cref="CellValue.Blank"/>.</summary>
    CellValue GetValue(string sheetName, CellAddress address);

    /// <summary>
    /// Valores de um intervalo, em varredura por linha. A ordem importa: <c>VPL</c> e
    /// <c>TIR</c> interpretam a sequência como períodos consecutivos.
    /// </summary>
    IEnumerable<CellValue> GetValues(string sheetName, CellRange range);
}
