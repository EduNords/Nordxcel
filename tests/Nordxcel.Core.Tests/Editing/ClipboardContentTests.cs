using System.Linq;
using Nordxcel.Core.Editing;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Tests.Editing;

public class ClipboardContentTests
{
    private const string Sheet = "DCF";

    private static CellAddress At(string address) => CellAddress.Parse(address);

    private static Workbook CreateWorkbook(params string[] extraSheets)
    {
        var workbook = new Workbook();
        workbook.AddWorksheet(Sheet);

        foreach (string name in extraSheets)
        {
            workbook.AddWorksheet(name);
        }

        return workbook;
    }

    /// <summary>
    /// O que um chamador real faz com o resultado de <c>ComputePaste</c>: grava
    /// cada célula. No aplicativo isso passa pelo <c>CalculationEngine.SetCell</c>;
    /// aqui, direto na aba, já que o teste não precisa do motor de cálculo.
    /// </summary>
    private static IReadOnlyList<CellEdit> Paste(
        ClipboardContent clip,
        Workbook workbook,
        string targetSheet,
        CellAddress targetAnchor)
    {
        IReadOnlyList<CellEdit> edits = clip.ComputePaste(workbook, targetSheet, targetAnchor);

        foreach (CellEdit edit in edits)
        {
            workbook[edit.Location.SheetName].SetCell(edit.Location.Address, edit.After);
        }

        return edits;
    }

    // ------------------------------------------------------------------ captura

    [Fact]
    public void Capture_GuardaAAncoraEOTamanho()
    {
        var workbook = CreateWorkbook();

        ClipboardContent clip = ClipboardContent.Capture(workbook[Sheet], CellRange.Parse("B2:D4"), isCut: false);

        Assert.Equal(Sheet, clip.SourceSheet);
        Assert.Equal(At("B2"), clip.Anchor);
        Assert.Equal(3, clip.RowCount);
        Assert.Equal(3, clip.ColumnCount);
        Assert.Equal(CellRange.Parse("B2:D4"), clip.SourceRange);
        Assert.False(clip.IsCut);
    }

    // ------------------------------------------------------------------- colar

    [Fact]
    public void Paste_EscreveOValorNoDestino()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(42));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        Paste(clip, workbook, Sheet, At("C3"));

        Assert.Equal(42d, sheet.GetValue(At("C3")).AsNumber());
    }

    [Fact]
    public void Paste_PreservaEstiloEFormato()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetCell(At("A1"), new Cell
        {
            Value = CellValue.Number(1234),
            NumberFormat = StandardNumberFormats.Thousands,
            Style = CellStyle.Default with { Bold = true },
        });

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        Paste(clip, workbook, Sheet, At("B2"));

        Cell pasted = sheet.GetCell(At("B2"));

        Assert.Equal(StandardNumberFormats.Thousands, pasted.NumberFormat);
        Assert.True(pasted.Style.Bold);
    }

    [Fact]
    public void Paste_ColaOBlocoInteiroPreservandoAsPosicoesRelativas()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(1));
        sheet.SetValue(At("B1"), CellValue.Number(2));
        sheet.SetValue(At("A2"), CellValue.Number(3));
        sheet.SetValue(At("B2"), CellValue.Number(4));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1:B2"), isCut: false);
        Paste(clip, workbook, Sheet, At("D5"));

        Assert.Equal(1d, sheet.GetValue(At("D5")).AsNumber());
        Assert.Equal(2d, sheet.GetValue(At("E5")).AsNumber());
        Assert.Equal(3d, sheet.GetValue(At("D6")).AsNumber());
        Assert.Equal(4d, sheet.GetValue(At("E6")).AsNumber());
    }

    [Fact]
    public void Paste_ForaDaBordaDaPlanilha_IgnoraAsCelulasQueEstourariam()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(1));
        sheet.SetValue(At("B1"), CellValue.Number(2));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1:B1"), isCut: false);

        // Ancorar na última coluna da planilha: só a primeira célula do bloco cabe.
        var lastColumn = new CellAddress(0, CellAddress.MaxColumns - 1);

        IReadOnlyList<CellEdit> edits = Paste(clip, workbook, Sheet, lastColumn);

        Assert.Single(edits);
        Assert.Equal(1d, sheet.GetValue(lastColumn).AsNumber());
    }

    [Fact]
    public void Paste_EmOutraAba()
    {
        var workbook = CreateWorkbook("Resumo");
        Worksheet source = workbook[Sheet];
        source.SetValue(At("A1"), CellValue.Number(99));

        ClipboardContent clip = ClipboardContent.Capture(source, CellRange.Parse("A1"), isCut: false);
        Paste(clip, workbook, "Resumo", At("B2"));

        Assert.Equal(99d, workbook["Resumo"].GetValue(At("B2")).AsNumber());
    }

    // --------------------------------------------------------- edições devolvidas

    [Fact]
    public void ComputePaste_NaoEscreveNada()
    {
        // O ponto central da mudança de arquitetura: calcular não deve gravar.
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(1));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        clip.ComputePaste(workbook, Sheet, At("C3"));

        Assert.True(sheet.GetValue(At("C3")).IsBlank);
    }

    [Fact]
    public void Paste_DevolveAntesEDepoisDeCadaCelulaQueMudou()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(1));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        IReadOnlyList<CellEdit> edits = Paste(clip, workbook, Sheet, At("C3"));

        CellEdit edit = Assert.Single(edits);
        Assert.Equal(new CellLocation(Sheet, At("C3")), edit.Location);
        Assert.True(edit.Before.Value.IsBlank);
        Assert.Equal(1d, edit.After.Value.AsNumber());
    }

    [Fact]
    public void Paste_SemMudarNada_NaoDevolveEdicoes()
    {
        // Colar em cima de uma célula idêntica ao que já está lá não é uma mudança.
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(5));
        sheet.SetValue(At("B1"), CellValue.Number(5));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        IReadOnlyList<CellEdit> edits = clip.ComputePaste(workbook, Sheet, At("B1"));

        Assert.Empty(edits);
    }

    // --------------------------------------------------- tradução de fórmula (copiar)

    [Fact]
    public void Copiar_TraduzReferenciaRelativaPelaDistanciaDoColar()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetCell(At("B2"), Cell.FromFormula("A1*2"));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("B2"), isCut: false);
        Paste(clip, workbook, Sheet, At("D5"));

        Assert.Equal("C4*2", sheet.GetCell(At("D5")).Formula);
    }

    [Fact]
    public void Copiar_ReferenciaAbsoluta_NaoSeMove()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetCell(At("B2"), Cell.FromFormula("$A$1*2"));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("B2"), isCut: false);
        Paste(clip, workbook, Sheet, At("D5"));

        Assert.Equal("$A$1*2", sheet.GetCell(At("D5")).Formula);
    }

    [Fact]
    public void Copiar_BlocoComReferenciaInternaAoProprioBloco_MantemACorrespondencia()
    {
        // A2 depende de A1, os dois dentro do bloco copiado — depois de colar, o
        // equivalente de A2 tem que continuar apontando para o equivalente de A1.
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(10));
        sheet.SetCell(At("A2"), Cell.FromFormula("A1*2"));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1:A2"), isCut: false);
        Paste(clip, workbook, Sheet, At("C1"));

        Assert.Equal("C1*2", sheet.GetCell(At("C2")).Formula);
    }

    [Fact]
    public void Copiar_ReferenciaEntreAbas_PreservaAAbaEDeslocaOEndereco()
    {
        var workbook = CreateWorkbook("Premissas");
        Worksheet sheet = workbook[Sheet];
        sheet.SetCell(At("B2"), Cell.FromFormula("Premissas!A1*2"));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("B2"), isCut: false);
        Paste(clip, workbook, Sheet, At("D5"));

        Assert.Equal("Premissas!C4*2", sheet.GetCell(At("D5")).Formula);
    }

    [Fact]
    public void Copiar_ReferenciaQueSaiDaPlanilha_ViraErroDeReferenciaNaFormula()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetCell(At("B2"), Cell.FromFormula("A1*2"));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("B2"), isCut: false);

        // Colar uma coluna à esquerda de onde estava empurra A1 para fora da planilha.
        Paste(clip, workbook, Sheet, At("A2"));

        Assert.Equal("#REF!*2", sheet.GetCell(At("A2")).Formula);
    }

    [Fact]
    public void Copiar_NoMesmoLugar_NaoAlteraOTextoDaFormula()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetCell(At("B2"), Cell.FromFormula("A1*2"));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("B2"), isCut: false);
        IReadOnlyList<CellEdit> edits = clip.ComputePaste(workbook, Sheet, At("B2"));

        // Nada mudou: nem o valor nem a fórmula, então não gera edição nenhuma.
        Assert.Empty(edits);
    }

    // -------------------------------------------------------- colar valores

    [Fact]
    public void ComputePasteValues_CelulaDeFormulaColaComoValorLiteral()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];

        // Cell.Value é o valor já calculado da fórmula, exatamente como o
        // CalculationEngine deixaria depois de recalcular.
        sheet.SetCell(At("A1"), new Cell { Formula = "1+1", Value = CellValue.Number(2) });

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        IReadOnlyList<CellEdit> edits = clip.ComputePasteValues(workbook, Sheet, At("C3"));

        foreach (CellEdit edit in edits)
        {
            workbook[edit.Location.SheetName].SetCell(edit.Location.Address, edit.After);
        }

        Cell pasted = sheet.GetCell(At("C3"));
        Assert.Null(pasted.Formula);
        Assert.Equal(2d, pasted.Value.AsNumber());
    }

    [Fact]
    public void ComputePasteValues_PreservaEstiloEFormatoDoDestino()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(10));
        sheet.SetCell(At("C3"), new Cell
        {
            NumberFormat = StandardNumberFormats.Percent,
            Style = CellStyle.Default with { Bold = true },
        });

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);

        foreach (CellEdit edit in clip.ComputePasteValues(workbook, Sheet, At("C3")))
        {
            sheet.SetCell(edit.Location.Address, edit.After);
        }

        Cell pasted = sheet.GetCell(At("C3"));
        Assert.Equal(10d, pasted.Value.AsNumber());
        Assert.Equal(StandardNumberFormats.Percent, pasted.NumberFormat);
        Assert.True(pasted.Style.Bold);
    }

    [Fact]
    public void ComputePasteValues_NaoEscreveNada()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(1));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        clip.ComputePasteValues(workbook, Sheet, At("C3"));

        Assert.True(sheet.GetValue(At("C3")).IsBlank);
    }

    [Fact]
    public void ComputePasteValues_SemMudarNada_NaoDevolveEdicoes()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(5));
        sheet.SetValue(At("B1"), CellValue.Number(5));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        IReadOnlyList<CellEdit> edits = clip.ComputePasteValues(workbook, Sheet, At("B1"));

        Assert.Empty(edits);
    }

    // ------------------------------------------------------ colar formatação

    [Fact]
    public void ComputePasteFormat_LevaEstiloENumberFormatSemMexerNoConteudoDoDestino()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetCell(At("A1"), new Cell
        {
            Value = CellValue.Number(1),
            NumberFormat = StandardNumberFormats.Percent,
            Style = CellStyle.Default with { Bold = true },
        });
        sheet.SetValue(At("C3"), CellValue.Number(999));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);

        foreach (CellEdit edit in clip.ComputePasteFormat(workbook, Sheet, At("C3")))
        {
            sheet.SetCell(edit.Location.Address, edit.After);
        }

        Cell pasted = sheet.GetCell(At("C3"));
        Assert.Equal(999d, pasted.Value.AsNumber());
        Assert.Equal(StandardNumberFormats.Percent, pasted.NumberFormat);
        Assert.True(pasted.Style.Bold);
    }

    [Fact]
    public void ComputePasteFormat_SemMudarNada_NaoDevolveEdicoes()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        var estilo = CellStyle.Default with { Bold = true };
        sheet.SetCell(At("A1"), new Cell { Style = estilo });
        sheet.SetCell(At("B1"), new Cell { Value = CellValue.Number(7), Style = estilo });

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);
        IReadOnlyList<CellEdit> edits = clip.ComputePasteFormat(workbook, Sheet, At("B1"));

        Assert.Empty(edits);
    }

    // ---------------------------------------------------------- recortar (cut)

    [Fact]
    public void Recortar_NaoTraduzAsReferenciasInternasDaFormula()
    {
        // A convenção do Excel: cortar move a célula, então ela continua
        // apontando para o mesmo lugar de antes, não para um lugar deslocado.
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetCell(At("B2"), Cell.FromFormula("A1*2"));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("B2"), isCut: true);
        Paste(clip, workbook, Sheet, At("D5"));

        Assert.Equal("A1*2", sheet.GetCell(At("D5")).Formula);
    }

    [Fact]
    public void Recortar_ValorEEstiloColamNormalmente()
    {
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetCell(At("A1"), new Cell { Value = CellValue.Number(7), Style = CellStyle.Default with { Bold = true } });

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: true);
        Paste(clip, workbook, Sheet, At("C3"));

        Cell pasted = sheet.GetCell(At("C3"));
        Assert.Equal(7d, pasted.Value.AsNumber());
        Assert.True(pasted.Style.Bold);
    }

    [Fact]
    public void IsCut_FicaMarcadoParaQuemFinalizarOMovimento() =>
        Assert.True(ClipboardContent.Capture(CreateWorkbook()[Sheet], CellRange.Parse("A1"), isCut: true).IsCut);

    // -------------------------------------------------- endereços de destino

    [Fact]
    public void DestinationAddresses_CobreOBlocoInteiroAPartirDaAncora()
    {
        var workbook = CreateWorkbook();
        ClipboardContent clip = ClipboardContent.Capture(workbook[Sheet], CellRange.Parse("A1:B2"), isCut: false);

        var destinations = clip.DestinationAddresses(At("D5")).ToList();

        Assert.Equal([At("D5"), At("E5"), At("D6"), At("E6")], destinations);
    }

    [Fact]
    public void DestinationAddresses_IncluiCelulasQueNaoMudariam()
    {
        // Diferente de ComputePaste, que só devolve o que muda, isto aqui
        // precisa listar TODO destino, mudando ou não — é usado para proteger
        // células da limpeza de um recorte, não para saber o que foi alterado.
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(1));
        sheet.SetValue(At("C1"), CellValue.Number(1)); // já tem o mesmo valor que vai ser colado

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1"), isCut: false);

        Assert.Contains(At("C1"), clip.DestinationAddresses(At("C1")));
    }

    [Fact]
    public void DestinationAddresses_ParaNaBordaDaPlanilha()
    {
        var workbook = CreateWorkbook();
        ClipboardContent clip = ClipboardContent.Capture(workbook[Sheet], CellRange.Parse("A1:B1"), isCut: false);

        var lastColumn = new CellAddress(0, CellAddress.MaxColumns - 1);
        var destinations = clip.DestinationAddresses(lastColumn).ToList();

        // A segunda célula do bloco estouraria a planilha; só a primeira sobra.
        Assert.Equal([lastColumn], destinations);
    }

    // ------------------------------------------- recortar e colar sobrepondo a origem

    [Fact]
    public void RecortarEColarSobrepondoAOrigem_NaoPerdeOConteudoQueAcabouDeChegar()
    {
        // Reproduz exatamente a lógica que SpreadsheetView.FinishCut usa: depois
        // de colar, limpar a origem SEM tocar nos endereços que também são
        // destino do próprio colar. Corrige um bug real encontrado por revisão:
        // sem essa proteção, uma célula colada que também pertence ao intervalo
        // de origem seria apagada logo depois de receber o conteúdo.
        var workbook = CreateWorkbook();
        Worksheet sheet = workbook[Sheet];
        sheet.SetValue(At("A1"), CellValue.Number(1));
        sheet.SetValue(At("B1"), CellValue.Number(2));
        sheet.SetValue(At("A2"), CellValue.Number(3));
        sheet.SetValue(At("B2"), CellValue.Number(4));

        ClipboardContent clip = ClipboardContent.Capture(sheet, CellRange.Parse("A1:B2"), isCut: true);

        // Cola deslocado uma coluna à direita: B1 e B2 (parte da origem) também
        // são destino (recebem o conteúdo de A1 e A2).
        var targetAnchor = At("B1");
        IReadOnlyList<CellEdit> pasted = Paste(clip, workbook, Sheet, targetAnchor);

        Assert.NotEmpty(pasted);

        // A mesma proteção que FinishCut aplica.
        var pastedOver = new HashSet<CellAddress>(clip.DestinationAddresses(targetAnchor));

        foreach (CellAddress address in clip.SourceRange.Addresses())
        {
            if (!pastedOver.Contains(address))
            {
                sheet.ClearCell(address);
            }
        }

        // B1 e B2 receberam conteúdo colado (de A1 e A2) e não podem ter sumido.
        Assert.Equal(1d, sheet.GetValue(At("B1")).AsNumber());
        Assert.Equal(3d, sheet.GetValue(At("B2")).AsNumber());

        // A1 e A2 não são destino de nada — continuam limpas, como um recorte espera.
        Assert.True(sheet.GetValue(At("A1")).IsBlank);
        Assert.True(sheet.GetValue(At("A2")).IsBlank);

        // C1 e C2 (destino de B1 e B2 originais) recebem o conteúdo original delas.
        Assert.Equal(2d, sheet.GetValue(At("C1")).AsNumber());
        Assert.Equal(4d, sheet.GetValue(At("C2")).AsNumber());
    }
}
