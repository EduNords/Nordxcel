using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Model;

public class WorkbookTests
{
    [Fact]
    public void CreateDefault_ComecaComUmaAbaVazia()
    {
        Workbook workbook = Workbook.CreateDefault();

        Worksheet sheet = Assert.Single(workbook.Worksheets);
        Assert.Equal("Planilha1", sheet.Name);
        Assert.Equal(0, sheet.CellCount);
    }

    [Fact]
    public void AddWorksheet_MantemAOrdemDeInsercao()
    {
        var workbook = new Workbook();

        workbook.AddWorksheet("Premissas");
        workbook.AddWorksheet("DCF");
        workbook.AddWorksheet("Sensibilidade");

        Assert.Equal(["Premissas", "DCF", "Sensibilidade"], workbook.Worksheets.Select(s => s.Name));
    }

    [Fact]
    public void BuscaPorNome_IgnoraCaixaDasLetras()
    {
        var workbook = new Workbook();
        Worksheet sheet = workbook.AddWorksheet("Premissas");

        // As fórmulas resolvem Premissas!B5 e premissas!B5 para a mesma aba.
        Assert.Same(sheet, workbook["premissas"]);
        Assert.True(workbook.ContainsWorksheet("PREMISSAS"));
    }

    [Fact]
    public void AddWorksheet_RecusaNomeRepetido()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Premissas");

        Assert.Throws<ArgumentException>(() => workbook.AddWorksheet("premissas"));
    }

    [Fact]
    public void Indexador_LancaQuandoAAbaNaoExiste() =>
        Assert.Throws<KeyNotFoundException>(() => new Workbook()["Inexistente"]);

    [Fact]
    public void RemoveWorksheet_TiraDaListaEDoIndice()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Premissas");
        workbook.AddWorksheet("DCF");

        Assert.True(workbook.RemoveWorksheet("premissas"));
        Assert.False(workbook.ContainsWorksheet("Premissas"));
        Assert.Single(workbook.Worksheets);
        Assert.False(workbook.RemoveWorksheet("Premissas"));
    }

    [Fact]
    public void RenameWorksheet_AtualizaOIndice()
    {
        var workbook = new Workbook();
        Worksheet sheet = workbook.AddWorksheet("Plan1");

        workbook.RenameWorksheet("Plan1", "Premissas");

        Assert.Equal("Premissas", sheet.Name);
        Assert.Same(sheet, workbook["Premissas"]);
        Assert.False(workbook.ContainsWorksheet("Plan1"));
    }

    [Fact]
    public void RenameWorksheet_PermiteTrocarApenasACaixaDasLetras()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("premissas");

        workbook.RenameWorksheet("premissas", "Premissas");

        Assert.Equal("Premissas", workbook["PREMISSAS"].Name);
    }

    [Fact]
    public void RenameWorksheet_RecusaColidirComOutraAba()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Premissas");
        workbook.AddWorksheet("DCF");

        Assert.Throws<ArgumentException>(() => workbook.RenameWorksheet("DCF", "premissas"));
        Assert.Throws<KeyNotFoundException>(() => workbook.RenameWorksheet("Inexistente", "Nova"));
    }

    [Fact]
    public void MoveWorksheet_ReordenaAsAbas()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Premissas");
        workbook.AddWorksheet("DCF");
        workbook.AddWorksheet("Sensibilidade");

        workbook.MoveWorksheet("Sensibilidade", 0);

        Assert.Equal(["Sensibilidade", "Premissas", "DCF"], workbook.Worksheets.Select(s => s.Name));
        Assert.Throws<ArgumentOutOfRangeException>(() => workbook.MoveWorksheet("DCF", 3));
    }
}
