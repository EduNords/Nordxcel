using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Model;

/// <summary>
/// Conteúdo e aparência de uma célula. Imutável: alterar uma célula produz uma
/// nova instância, o que dá desfazer/refazer praticamente de graça e evita que o
/// motor de recálculo enxergue estado pela metade.
/// </summary>
public sealed record Cell
{
    /// <summary>Célula sem conteúdo, sem formato e com estilo padrão.</summary>
    public static readonly Cell Empty = new();

    /// <summary>
    /// Expressão da fórmula <b>sem</b> o <c>=</c> inicial (ex.: <c>SOMA(B2:B10)</c>).
    /// <c>null</c> indica entrada literal — é esse teste que o sistema de cores usa
    /// para pintar de azul as células de premissa digitadas à mão.
    /// </summary>
    public string? Formula { get; init; }

    /// <summary>
    /// Valor literal da célula ou, quando há <see cref="Formula"/>, o último resultado calculado.
    /// </summary>
    public CellValue Value { get; init; } = CellValue.Blank;

    /// <summary>
    /// Máscara de formato de número na sintaxe do Excel (ex.: <c>#,##0;(#,##0)</c>).
    /// <c>null</c> significa formato geral. Guardar a máscara nesse dialeto deixa a
    /// exportação para .xlsx direta, sem tradução.
    /// </summary>
    public string? NumberFormat { get; init; }

    public CellStyle Style { get; init; } = CellStyle.Default;

    public bool HasFormula => Formula is not null;

    /// <summary>
    /// Verdadeiro quando a célula não guarda nada que valha a pena persistir.
    /// A planilha usa isso para não armazenar células em branco.
    /// </summary>
    public bool IsEmpty =>
        Formula is null &&
        Value.IsBlank &&
        NumberFormat is null &&
        Style.IsDefault;

    public static Cell FromNumber(double value) => new() { Value = CellValue.Number(value) };

    public static Cell FromText(string value) => new() { Value = CellValue.Text(value) };

    public static Cell FromLogical(bool value) => new() { Value = CellValue.Logical(value) };

    /// <summary>
    /// Cria uma célula de fórmula ainda não calculada. O valor fica em branco até
    /// o motor de recálculo passar por ela.
    /// </summary>
    public static Cell FromFormula(string formula)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);

        return new Cell { Formula = formula.StartsWith('=') ? formula[1..] : formula };
    }

    /// <summary>Devolve a célula com o resultado do cálculo atualizado.</summary>
    public Cell WithValue(CellValue value) => this with { Value = value };
}
