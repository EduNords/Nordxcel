using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation;

/// <summary>Contexto que lê os valores direto de uma <see cref="Workbook"/>.</summary>
public sealed class WorkbookEvaluationContext : IEvaluationContext
{
    private readonly Workbook _workbook;

    public WorkbookEvaluationContext(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        _workbook = workbook;
    }

    public bool ContainsSheet(string sheetName) => _workbook.ContainsWorksheet(sheetName);

    public CellValue GetValue(string sheetName, CellAddress address) =>
        _workbook.TryGetWorksheet(sheetName, out Worksheet? worksheet)
            ? worksheet.GetValue(address)
            : CellValue.Error(CellErrorType.Reference);

    public IEnumerable<CellValue> GetValues(string sheetName, CellRange range)
    {
        if (!_workbook.TryGetWorksheet(sheetName, out Worksheet? worksheet))
        {
            yield return CellValue.Error(CellErrorType.Reference);
            yield break;
        }

        foreach (CellAddress address in range.Addresses())
        {
            yield return worksheet.GetValue(address);
        }
    }
}
