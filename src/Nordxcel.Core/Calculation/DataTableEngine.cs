using Nordxcel.Core.Model;

namespace Nordxcel.Core.Calculation;

/// <summary>
/// Análise de sensibilidade (Data Table): recalcula o modelo dezenas de vezes com
/// um ou dois inputs substituídos, sem alterar a pasta de trabalho real.
/// <para>
/// A técnica é o "shadow calculation" descrito no roadmap: clona a pasta inteira
/// uma única vez, sobe um <see cref="CalculationEngine"/> por cima da cópia, e
/// reaproveita esse motor (e as fórmulas já interpretadas por ele) para cada
/// combinação de valores — só o recálculo incremental de cada rodada é repetido,
/// nunca o parse.
/// </para>
/// </summary>
public static class DataTableEngine
{
    /// <summary>
    /// Tabela de uma variável: recalcula <paramref name="outputCell"/> uma vez para
    /// cada valor de <paramref name="inputValues"/>, substituído em
    /// <paramref name="inputCell"/>.
    /// </summary>
    public static IReadOnlyList<CellValue> EvaluateSingleVariable(
        Workbook workbook,
        CellLocation inputCell,
        CellLocation outputCell,
        IReadOnlyList<CellValue> inputValues)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(inputValues);

        Workbook shadow = workbook.Clone();
        var engine = new CalculationEngine(shadow) { AutoRecalculate = false };
        var results = new CellValue[inputValues.Count];

        for (int i = 0; i < inputValues.Count; i++)
        {
            engine.SetValue(inputCell, inputValues[i]);
            engine.Recalculate();
            results[i] = ReadValue(shadow, outputCell);
        }

        return results;
    }

    /// <summary>
    /// Tabela de duas variáveis: recalcula <paramref name="outputCell"/> para cada
    /// combinação de <paramref name="rowValues"/> (substituído em
    /// <paramref name="rowInputCell"/>) e <paramref name="columnValues"/>
    /// (substituído em <paramref name="columnInputCell"/>), produzindo uma matriz
    /// <c>[linha, coluna]</c> do tamanho <c>rowValues × columnValues</c>.
    /// </summary>
    public static CellValue[,] EvaluateTwoVariable(
        Workbook workbook,
        CellLocation rowInputCell,
        IReadOnlyList<CellValue> rowValues,
        CellLocation columnInputCell,
        IReadOnlyList<CellValue> columnValues,
        CellLocation outputCell)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(rowValues);
        ArgumentNullException.ThrowIfNull(columnValues);

        Workbook shadow = workbook.Clone();
        var engine = new CalculationEngine(shadow) { AutoRecalculate = false };
        var results = new CellValue[rowValues.Count, columnValues.Count];

        for (int row = 0; row < rowValues.Count; row++)
        {
            engine.SetValue(rowInputCell, rowValues[row]);

            for (int column = 0; column < columnValues.Count; column++)
            {
                engine.SetValue(columnInputCell, columnValues[column]);
                engine.Recalculate();
                results[row, column] = ReadValue(shadow, outputCell);
            }
        }

        return results;
    }

    private static CellValue ReadValue(Workbook workbook, CellLocation location) =>
        workbook.TryGetWorksheet(location.SheetName, out Worksheet? sheet)
            ? sheet.GetValue(location.Address)
            : CellValue.Error(CellErrorType.Reference);
}
