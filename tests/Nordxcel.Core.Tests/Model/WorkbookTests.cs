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

    [Fact]
    public void CreateUniqueWorksheetName_ComecaEmUm()
    {
        var workbook = new Workbook();

        Assert.Equal("Planilha1", workbook.CreateUniqueWorksheetName());
    }

    [Fact]
    public void CreateUniqueWorksheetName_PulaOsNomesJaUsados()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Planilha1");
        workbook.AddWorksheet("Planilha2");

        Assert.Equal("Planilha3", workbook.CreateUniqueWorksheetName());
    }

    [Fact]
    public void CreateUniqueWorksheetName_FuncionaMesmoComBuracoNaSequencia()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Planilha1");
        workbook.AddWorksheet("Planilha3");

        // Planilha2 está livre, mesmo com Planilha3 já existindo.
        Assert.Equal("Planilha2", workbook.CreateUniqueWorksheetName());
    }

    [Fact]
    public void CreateUniqueWorksheetName_IgnoraNomesQueNaoSeguemOPadrao()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("DCF");
        workbook.AddWorksheet("Premissas");

        Assert.Equal("Planilha1", workbook.CreateUniqueWorksheetName());
    }

    [Fact]
    public void CreateUniqueWorksheetName_AceitaPrefixoCustomizado()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Cenário1");

        Assert.Equal("Cenário2", workbook.CreateUniqueWorksheetName("Cenário"));
    }

    [Fact]
    public void AddWorksheet_SemNomeGeraUmAutomatico()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Planilha1");

        Worksheet added = workbook.AddWorksheet();

        Assert.Equal("Planilha2", added.Name);
        Assert.Equal(2, workbook.Worksheets.Count);
    }

    [Fact]
    public void Clone_CopiaTodasAsAbasNaMesmaOrdem()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Premissas");
        workbook["Premissas"].SetValue(CellAddress.Parse("B2"), CellValue.Number(8));
        workbook.AddWorksheet("DCF");
        workbook["DCF"].SetCell(CellAddress.Parse("A1"), Cell.FromFormula("Premissas!B2*2"));

        Workbook clone = workbook.Clone();

        Assert.Equal(["Premissas", "DCF"], clone.Worksheets.Select(s => s.Name));
        Assert.Equal(8d, clone["Premissas"].GetValue(CellAddress.Parse("B2")).AsNumber());
        Assert.Equal("Premissas!B2*2", clone["DCF"].GetCell(CellAddress.Parse("A1")).Formula);
    }

    [Fact]
    public void Clone_EIndependenteDoOriginal()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet("Planilha1");
        workbook["Planilha1"].SetValue(CellAddress.Parse("A1"), CellValue.Number(1));

        Workbook clone = workbook.Clone();
        clone["Planilha1"].SetValue(CellAddress.Parse("A1"), CellValue.Number(999));
        clone.AddWorksheet("SóNaCópia");

        Assert.Equal(1d, workbook["Planilha1"].GetValue(CellAddress.Parse("A1")).AsNumber());
        Assert.False(workbook.ContainsWorksheet("SóNaCópia"));
    }
}
