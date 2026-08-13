using Nordxcel.Core.Formulas;
using Nordxcel.Core.Formulas.Ast;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Editing;

/// <summary>
/// Um bloco de células copiado ou recortado, pronto para colar em outro lugar.
/// <para>
/// É um clipboard interno do Nordxcel — não passa pela área de transferência do
/// sistema operacional, então só dá para colar de volta dentro do próprio
/// aplicativo. Levar isso à área de transferência real do Windows/macOS/Linux
/// fica para depois.
/// </para>
/// </summary>
public sealed class ClipboardContent
{
    private readonly Cell[,] _cells;

    private ClipboardContent(string sourceSheet, CellAddress anchor, Cell[,] cells, bool isCut)
    {
        SourceSheet = sourceSheet;
        Anchor = anchor;
        _cells = cells;
        IsCut = isCut;
    }

    /// <summary>Aba de onde as células foram copiadas.</summary>
    public string SourceSheet { get; }

    /// <summary>Canto superior esquerdo do intervalo copiado.</summary>
    public CellAddress Anchor { get; }

    /// <summary>Verdadeiro quando veio de recortar (Ctrl+X), e não copiar.</summary>
    public bool IsCut { get; }

    public int RowCount => _cells.GetLength(0);

    public int ColumnCount => _cells.GetLength(1);

    /// <summary>Intervalo de origem, reconstruído a partir da âncora e do tamanho.</summary>
    public CellRange SourceRange => new(
        Anchor,
        new CellAddress(Anchor.Row + RowCount - 1, Anchor.Column + ColumnCount - 1));

    /// <summary>Tira uma foto do intervalo — os <see cref="Cell"/> são imutáveis, então não precisa clonar.</summary>
    public static ClipboardContent Capture(Worksheet sheet, CellRange range, bool isCut)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var cells = new Cell[range.RowCount, range.ColumnCount];

        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColumnCount; c++)
            {
                cells[r, c] = sheet.GetCell(new CellAddress(range.Start.Row + r, range.Start.Column + c));
            }
        }

        return new ClipboardContent(sheet.Name, range.Start, cells, isCut);
    }

    /// <summary>
    /// Calcula o que colar o bloco em <paramref name="targetAnchor"/> produziria,
    /// célula por célula, <b>sem gravar nada</b>. Devolve o antes/depois de cada
    /// célula que mudaria, pronto para virar um passo de desfazer.
    /// <para>
    /// Não escreve na aba de propósito: uma célula com fórmula precisa passar
    /// pelo <c>CalculationEngine.SetCell</c> para entrar no grafo de dependências
    /// e ser calculada — gravar direto no <see cref="Worksheet"/> aqui deixaria
    /// a fórmula colada com o texto certo, mas nunca avaliada. Quem aplica o
    /// resultado decide como gravar: um teste no Core pode escrever direto na
    /// aba, e o aplicativo grava através do motor de cálculo.
    /// </para>
    /// <para>
    /// Fórmulas copiadas têm as referências relativas deslocadas pela mesma
    /// distância entre a âncora de origem e a de destino — a mesma distância para
    /// o bloco inteiro, não recalculada célula a célula, para que uma fórmula que
    /// aponta para outra célula do próprio bloco copiado continue apontando para
    /// a célula correspondente no bloco colado. Fórmulas recortadas colam com as
    /// referências internas <b>intactas</b>: no Excel, cortar move a célula sem
    /// mudar para onde ela aponta — só copiar desloca. O Nordxcel não reescreve,
    /// porém, as fórmulas de <i>outras</i> células que apontavam para o bloco
    /// recortado; esse acompanhamento pelo resto da pasta de trabalho fica para
    /// uma versão futura.
    /// </para>
    /// </summary>
    public IReadOnlyList<CellEdit> ComputePaste(
        Workbook workbook,
        string targetSheetName,
        CellAddress targetAnchor,
        FormulaSyntax? syntax = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSheetName);

        Worksheet target = workbook[targetSheetName];
        var parser = new FormulaParser(syntax);
        var edits = new List<CellEdit>();

        int rowDelta = targetAnchor.Row - Anchor.Row;
        int columnDelta = targetAnchor.Column - Anchor.Column;

        for (int r = 0; r < RowCount; r++)
        {
            for (int c = 0; c < ColumnCount; c++)
            {
                if (!targetAnchor.TryOffset(r, c, out CellAddress destination))
                {
                    // Colar perto da borda da planilha pode empurrar parte do
                    // bloco para fora; essas células são ignoradas, como no Excel.
                    continue;
                }

                Cell source = _cells[r, c];
                Cell pasted = TranslateForPaste(source, rowDelta, columnDelta, parser, syntax);
                Cell before = target.GetCell(destination);

                if (pasted == before)
                {
                    continue;
                }

                edits.Add(new CellEdit(new CellLocation(targetSheetName, destination), before, pasted));
            }
        }

        return edits;
    }

    /// <summary>
    /// Endereços de destino que colar a partir da âncora ocuparia — inclusive os
    /// que não mudariam de conteúdo. Quem termina um recorte usa isso para saber
    /// quais células da origem <b>não</b> devem ser apagadas por coincidirem com
    /// o próprio destino colado, no caso de recortar e colar em cima (ou perto)
    /// de onde já estava.
    /// </summary>
    public IEnumerable<CellAddress> DestinationAddresses(CellAddress targetAnchor)
    {
        for (int r = 0; r < RowCount; r++)
        {
            for (int c = 0; c < ColumnCount; c++)
            {
                if (targetAnchor.TryOffset(r, c, out CellAddress destination))
                {
                    yield return destination;
                }
            }
        }
    }

    private Cell TranslateForPaste(
        Cell source,
        int rowDelta,
        int columnDelta,
        FormulaParser parser,
        FormulaSyntax? syntax)
    {
        if (!source.HasFormula || IsCut || (rowDelta == 0 && columnDelta == 0))
        {
            return source;
        }

        FormulaNode tree;

        try
        {
            tree = parser.Parse(source.Formula!);
        }
        catch (FormulaSyntaxException)
        {
            // Não deveria acontecer — a fórmula já foi validada quando foi
            // digitada — mas colar não é o lugar de travar por causa disso.
            return source;
        }

        FormulaNode translated = FormulaTranslator.Translate(tree, rowDelta, columnDelta);
        string formulaText = FormulaWriter.Write(translated, syntax ?? FormulaSyntax.Default);

        return source with { Formula = formulaText };
    }
}
