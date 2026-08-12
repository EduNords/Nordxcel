using System.Linq;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Editing;

/// <summary>
/// Aplica uma mudança de formatação — fonte, cor, alinhamento, formato de número —
/// a cada célula de um intervalo. É o que a barra de formatação usa por trás de
/// cada botão.
/// <para>
/// Não passa pelo <c>CalculationEngine</c> de propósito: formatação não muda o
/// valor calculado de nada, então não há por que registrar dependência nem
/// recalcular. Um <c>Refresh()</c> de tela já é suficiente depois de aplicar.
/// </para>
/// </summary>
public static class RangeEditing
{
    /// <summary>
    /// Aplica a transformação a cada célula do intervalo.
    /// <para>
    /// Para seleção de linha ou coluna inteira — o que um clique no cabeçalho
    /// produz — só toca nas células que já têm algum conteúdo. Sem essa guarda,
    /// aplicar negrito numa coluna inteira materializaria mais de um milhão de
    /// células só para guardar o estilo, o que é exatamente o tipo de armadilha
    /// de desempenho que o armazenamento esparso do <see cref="Worksheet"/> existe
    /// para evitar.
    /// </para>
    /// </summary>
    public static void Apply(Worksheet sheet, CellRange range, Func<Cell, Cell> transform)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(transform);

        if (IsUnbounded(range))
        {
            // ToList: SetCell pode remover a entrada (célula fica vazia de novo),
            // o que não pode acontecer enquanto ainda se está enumerando o dicionário.
            foreach (CellAddress address in sheet.Cells.Keys.Where(range.Contains).ToList())
            {
                ApplyToCell(sheet, address, transform);
            }

            return;
        }

        foreach (CellAddress address in range.Addresses())
        {
            ApplyToCell(sheet, address, transform);
        }
    }

    /// <summary>Atalho para mudanças que só mexem no estilo, preservando o resto da célula.</summary>
    public static void ApplyStyle(Worksheet sheet, CellRange range, Func<CellStyle, CellStyle> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        Apply(sheet, range, cell => cell with { Style = transform(cell.Style) });
    }

    /// <summary>Atalho para trocar o formato de número, preservando o resto da célula.</summary>
    public static void ApplyNumberFormat(Worksheet sheet, CellRange range, string? mask) =>
        Apply(sheet, range, cell => cell with { NumberFormat = mask });

    private static void ApplyToCell(Worksheet sheet, CellAddress address, Func<Cell, Cell> transform)
    {
        Cell current = sheet.GetCell(address);
        Cell updated = transform(current);

        if (updated != current)
        {
            sheet.SetCell(address, updated);
        }
    }

    /// <summary>
    /// Verdadeiro para seleção de linha ou coluna inteira — o intervalo cobre a
    /// planilha inteira num dos eixos, o mesmo teste que <see cref="Layout.SheetSelection"/>
    /// usa para decidir <c>IsWholeColumns</c>/<c>IsWholeRows</c>.
    /// </summary>
    private static bool IsUnbounded(CellRange range) =>
        range.RowCount >= CellAddress.MaxRows || range.ColumnCount >= CellAddress.MaxColumns;
}
