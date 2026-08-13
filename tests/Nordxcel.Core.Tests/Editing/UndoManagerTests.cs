using Nordxcel.Core.Editing;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Editing;

public class UndoManagerTests
{
    private static CellEdit Edit(string address, double before, double after) => new(
        new CellLocation("DCF", CellAddress.Parse(address)),
        Cell.FromNumber(before),
        Cell.FromNumber(after));

    private static UndoStep Step(string description, params CellEdit[] edits) => new(description, edits);

    [Fact]
    public void Vazio_NaoTemNadaParaDesfazerOuRefazer()
    {
        var undo = new UndoManager();

        Assert.False(undo.CanUndo);
        Assert.False(undo.CanRedo);
        Assert.Null(undo.NextUndoDescription);
        Assert.Null(undo.Undo());
        Assert.Null(undo.Redo());
    }

    [Fact]
    public void Push_HabilitaDesfazer()
    {
        var undo = new UndoManager();

        undo.Push(Step("Digitar", Edit("A1", 0, 10)));

        Assert.True(undo.CanUndo);
        Assert.False(undo.CanRedo);
        Assert.Equal("Digitar", undo.NextUndoDescription);
    }

    [Fact]
    public void Push_ComListaVazia_NaoEmpilhaNada()
    {
        // Uma "mudança" que não mudou nada de fato não deveria virar um passo de
        // desfazer que não faz nada quando acionado.
        var undo = new UndoManager();

        undo.Push(Step("Nada", []));

        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Undo_MoveOPassoParaORefazer()
    {
        var undo = new UndoManager();
        UndoStep original = Step("Digitar", Edit("A1", 0, 10));
        undo.Push(original);

        UndoStep? undone = undo.Undo();

        Assert.Same(original, undone);
        Assert.False(undo.CanUndo);
        Assert.True(undo.CanRedo);
        Assert.Equal("Digitar", undo.NextRedoDescription);
    }

    [Fact]
    public void Redo_DevolveOPassoParaODesfazer()
    {
        var undo = new UndoManager();
        undo.Push(Step("Digitar", Edit("A1", 0, 10)));
        undo.Undo();

        UndoStep? redone = undo.Redo();

        Assert.NotNull(redone);
        Assert.True(undo.CanUndo);
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void UndoERedo_EmSequencia_VoltamAoEstadoOriginal()
    {
        var undo = new UndoManager();
        undo.Push(Step("A", Edit("A1", 0, 1)));
        undo.Push(Step("B", Edit("A1", 1, 2)));
        undo.Push(Step("C", Edit("A1", 2, 3)));

        Assert.Equal("C", undo.Undo()!.Description);
        Assert.Equal("B", undo.Undo()!.Description);
        Assert.Equal("B", undo.Redo()!.Description);
        Assert.Equal("C", undo.Redo()!.Description);
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void NovaAcao_ApagaOQueDavaParaRefazer()
    {
        // Igual ao Excel: editar algo depois de desfazer invalida o redo.
        var undo = new UndoManager();
        undo.Push(Step("A", Edit("A1", 0, 1)));
        undo.Undo();

        Assert.True(undo.CanRedo);

        undo.Push(Step("B", Edit("A1", 0, 5)));

        Assert.False(undo.CanRedo);
        Assert.Null(undo.NextRedoDescription);
    }

    [Fact]
    public void RespeitaOLimiteDePassos()
    {
        var undo = new UndoManager(maxSteps: 3);

        for (int i = 0; i < 5; i++)
        {
            undo.Push(Step($"Passo{i}", Edit("A1", i, i + 1)));
        }

        Assert.Equal(3, undo.UndoCount);

        // Os dois primeiros passos (0 e 1) foram descartados; o mais antigo que
        // sobra é o Passo2.
        undo.Undo();
        undo.Undo();
        UndoStep last = undo.Undo()!;

        Assert.Equal("Passo2", last.Description);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Clear_EsvaziaOsDoisLados()
    {
        var undo = new UndoManager();
        undo.Push(Step("A", Edit("A1", 0, 1)));
        undo.Undo();

        undo.Clear();

        Assert.False(undo.CanUndo);
        Assert.False(undo.CanRedo);
    }
}
