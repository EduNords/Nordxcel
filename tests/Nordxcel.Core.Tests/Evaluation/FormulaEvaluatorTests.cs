using Nordxcel.Core.Evaluation;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Evaluation;

public class FormulaEvaluatorTests
{
    private const string MainSheet = "DCF";

    /// <summary>Monta uma pasta com a aba principal e avalia a fórmula a partir dela.</summary>
    private static CellValue Eval(string formula, Action<Workbook>? setup = null)
    {
        var workbook = new Workbook();
        workbook.AddWorksheet(MainSheet);
        setup?.Invoke(workbook);

        var evaluator = new FormulaEvaluator(new WorkbookEvaluationContext(workbook));

        return evaluator.Evaluate(FormulaParser.ParseDefault(formula), new EvaluationScope(MainSheet));
    }

    private static double Number(string formula, Action<Workbook>? setup = null)
    {
        CellValue value = Eval(formula, setup);

        Assert.True(value.IsNumber, $"Esperava número, veio {value}.");

        return value.AsNumber();
    }

    private static CellErrorType Error(string formula, Action<Workbook>? setup = null)
    {
        CellValue value = Eval(formula, setup);

        Assert.True(value.IsError, $"Esperava erro, veio {value}.");

        return value.AsError();
    }

    private static Action<Workbook> Cells(params (string Address, CellValue Value)[] cells) => workbook =>
    {
        Worksheet sheet = workbook[MainSheet];

        foreach ((string address, CellValue value) in cells)
        {
            sheet.SetValue(CellAddress.Parse(address), value);
        }
    };

    // -------------------------------------------------------------- literais

    [Fact]
    public void Avalia_Literais()
    {
        Assert.Equal(CellValue.Number(1.5), Eval("1,5"));
        Assert.Equal(CellValue.Text("Receita"), Eval("\"Receita\""));
        Assert.Equal(CellValue.True, Eval("VERDADEIRO"));
        Assert.Equal(CellValue.Error(CellErrorType.NotAvailable), Eval("#N/D"));
    }

    [Fact]
    public void NomeDesconhecido_ViraErroDeNome() =>
        Assert.Equal(CellErrorType.Name, Error("Taxa_Imposto"));

    [Fact]
    public void FuncaoDesconhecida_ViraErroDeNome() =>
        Assert.Equal(CellErrorType.Name, Error("SOMA(1;2)"));

    // ------------------------------------------------------------ aritmética

    [Theory]
    [InlineData("2+3", 5)]
    [InlineData("10-3-2", 5)]
    [InlineData("2*3", 6)]
    [InlineData("10/4", 2.5)]
    [InlineData("2^10", 1024)]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("-5", -5)]
    [InlineData("--5", 5)]
    [InlineData("50%", 0.5)]
    [InlineData("1+50%", 1.5)]
    public void Avalia_Aritmetica(string formula, double expected) =>
        Assert.Equal(expected, Number(formula));

    [Fact]
    public void Potencia_SegueAPrecedenciaDoExcel()
    {
        Assert.Equal(4d, Number("-2^2"));    // (-2)^2, e não -(2^2)
        Assert.Equal(64d, Number("2^3^2"));  // (2^3)^2, e não 2^(3^2)
    }

    [Fact]
    public void DivisaoPorZero_ViraErro() =>
        Assert.Equal(CellErrorType.DivideByZero, Error("1/0"));

    [Fact]
    public void Potencia_CasosSemResultadoReal()
    {
        Assert.Equal(CellErrorType.Number, Error("0^0"));
        Assert.Equal(CellErrorType.DivideByZero, Error("0^-1"));
        Assert.Equal(CellErrorType.Number, Error("-8^0,5"));
        Assert.Equal(-8d, Number("-8^1"));
    }

    [Fact]
    public void Estouro_ViraErroEmVezDeInfinito() =>
        Assert.Equal(CellErrorType.Number, Error("1E308*10"));

    // --------------------------------------------------------------- coerção

    [Fact]
    public void TextoQueParecaNumero_EhCoagidoNaAritmetica() =>
        Assert.Equal(15d, Number("A1+5", Cells(("A1", CellValue.Text("10")))));

    [Fact]
    public void TextoComVirgulaDecimal_EhCoagido() =>
        Assert.Equal(3d, Number("A1*2", Cells(("A1", CellValue.Text("1,5")))));

    [Fact]
    public void TextoQueNaoEhNumero_ViraErroDeValor() =>
        Assert.Equal(CellErrorType.Value, Error("A1+1", Cells(("A1", CellValue.Text("abc")))));

    [Fact]
    public void TextoVazio_NaoEhNumero() =>
        Assert.Equal(CellErrorType.Value, Error("A1+1", Cells(("A1", CellValue.Text("")))));

    [Fact]
    public void LogicoValeUmOuZeroNaAritmetica()
    {
        Assert.Equal(2d, Number("VERDADEIRO+1"));
        Assert.Equal(1d, Number("FALSO+1"));
    }

    [Fact]
    public void CelulaVazia_ValeZero() =>
        Assert.Equal(5d, Number("Z99+5"));

    // --------------------------------------------------------- concatenação

    [Fact]
    public void Concatenacao_JuntaTextos() =>
        Assert.Equal(CellValue.Text("Receita 2025"), Eval("\"Receita \"&\"2025\""));

    [Fact]
    public void Concatenacao_FormataNumeroComVirgulaDecimal() =>
        Assert.Equal(CellValue.Text("x1,5"), Eval("\"x\"&1,5"));

    [Fact]
    public void Concatenacao_UsaOsNomesEmPortuguesDosLogicos() =>
        Assert.Equal(CellValue.Text("VERDADEIRO"), Eval("\"\"&VERDADEIRO"));

    [Fact]
    public void Concatenacao_CelulaVaziaNaoAcrescentaNada() =>
        Assert.Equal(CellValue.Text("ab"), Eval("\"a\"&Z99&\"b\""));

    // ---------------------------------------------------------- comparações

    [Theory]
    [InlineData("1=1", true)]
    [InlineData("1=2", false)]
    [InlineData("1<>2", true)]
    [InlineData("1<2", true)]
    [InlineData("2<=2", true)]
    [InlineData("3>2", true)]
    [InlineData("2>=3", false)]
    public void Avalia_Comparacoes(string formula, bool expected) =>
        Assert.Equal(CellValue.Logical(expected), Eval(formula));

    [Fact]
    public void ComparacaoDeTexto_IgnoraCaixaDasLetras() =>
        Assert.Equal(CellValue.True, Eval("\"DCF\"=\"dcf\""));

    [Fact]
    public void ComparacaoEntreTipos_NumeroVemAntesDeTextoQueVemAntesDeLogico()
    {
        Assert.Equal(CellValue.True, Eval("1<\"a\""));
        Assert.Equal(CellValue.True, Eval("\"z\"<VERDADEIRO"));
        Assert.Equal(CellValue.True, Eval("1<VERDADEIRO"));
    }

    [Fact]
    public void CelulaVazia_AssumeOTipoDoOutroLadoNaComparacao()
    {
        Assert.Equal(CellValue.True, Eval("Z99=0"));
        Assert.Equal(CellValue.True, Eval("Z99=\"\""));
        Assert.Equal(CellValue.True, Eval("Z99=FALSO"));
    }

    // ----------------------------------------------------------- referências

    [Fact]
    public void Referencia_LeOValorDaCelula() =>
        Assert.Equal(42d, Number("A1", Cells(("A1", CellValue.Number(42)))));

    [Fact]
    public void ReferenciaAbsoluta_LeAMesmaCelula() =>
        Assert.Equal(42d, Number("$A$1", Cells(("A1", CellValue.Number(42)))));

    [Fact]
    public void ReferenciaEntreAbas_LeDaAbaCerta()
    {
        double result = Number("Premissas!B3*100", workbook =>
        {
            Worksheet premissas = workbook.AddWorksheet("Premissas");
            premissas.SetValue(CellAddress.Parse("B3"), CellValue.Number(0.11));
        });

        Assert.Equal(11d, result, 10);
    }

    [Fact]
    public void ReferenciaEntreAbas_IgnoraACaixaDoNomeDaAba()
    {
        double result = Number("premissas!B3", workbook =>
        {
            Worksheet premissas = workbook.AddWorksheet("Premissas");
            premissas.SetValue(CellAddress.Parse("B3"), CellValue.Number(7));
        });

        Assert.Equal(7d, result);
    }

    [Fact]
    public void ReferenciaParaAbaInexistente_ViraErroDeReferencia() =>
        Assert.Equal(CellErrorType.Reference, Error("Inexistente!A1"));

    [Fact]
    public void ErroNaCelula_SePropagaPelaExpressao() =>
        Assert.Equal(
            CellErrorType.DivideByZero,
            Error("A1*2+1", Cells(("A1", CellValue.Error(CellErrorType.DivideByZero)))));

    [Fact]
    public void ErroMaisAEsquerda_EhOQuePropaga() =>
        Assert.Equal(
            CellErrorType.NotAvailable,
            Error(
                "A1+A2",
                Cells(
                    ("A1", CellValue.Error(CellErrorType.NotAvailable)),
                    ("A2", CellValue.Error(CellErrorType.DivideByZero)))));

    // ------------------------------------------------------------ intervalos

    [Fact]
    public void IntervaloUsadoComoEscalar_ViraErroDeValor()
    {
        // Sem interseção implícita: um intervalo só faz sentido dentro de uma função.
        Assert.Equal(CellErrorType.Value, Error("B2:B10"));
        Assert.Equal(CellErrorType.Value, Error("B2:B10*2"));
    }

    [Fact]
    public void IntervaloSobreviveComoIntervaloNaAvaliacaoDeOperando()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet(MainSheet);

        var evaluator = new FormulaEvaluator(new WorkbookEvaluationContext(workbook));
        FormulaValue value = evaluator.EvaluateOperand(
            FormulaParser.ParseDefault("B2:B10"),
            new EvaluationScope(MainSheet));

        Assert.True(value.IsRange);
        Assert.Equal(CellRange.Parse("B2:B10"), value.Range);
        Assert.Equal(MainSheet, value.SheetName);
    }

    [Fact]
    public void Contexto_DevolveOsValoresDoIntervaloEmVarreduraPorLinha()
    {
        var workbook = new Workbook();
        Worksheet sheet = workbook.AddWorksheet(MainSheet);

        sheet.SetValue(CellAddress.Parse("A1"), CellValue.Number(1));
        sheet.SetValue(CellAddress.Parse("B1"), CellValue.Number(2));
        sheet.SetValue(CellAddress.Parse("A2"), CellValue.Number(3));
        sheet.SetValue(CellAddress.Parse("B2"), CellValue.Number(4));

        var context = new WorkbookEvaluationContext(workbook);

        // A ordem importa: VPL e TIR leem a sequência como períodos consecutivos.
        Assert.Equal(
            [1d, 2d, 3d, 4d],
            context.GetValues(MainSheet, CellRange.Parse("A1:B2")).Select(v => v.AsNumber()));
    }

    // ------------------------------------------------------- fórmula de DCF

    [Fact]
    public void FluxoDescontadoDeUmPeriodo()
    {
        // FCF de 1.000 no ano 3, com WACC de 11%.
        double result = Number("D12/(1+Premissas!$B$3)^D$4", workbook =>
        {
            Worksheet dcf = workbook[MainSheet];
            dcf.SetValue(CellAddress.Parse("D12"), CellValue.Number(1000));
            dcf.SetValue(CellAddress.Parse("D4"), CellValue.Number(3));

            Worksheet premissas = workbook.AddWorksheet("Premissas");
            premissas.SetValue(CellAddress.Parse("B3"), CellValue.Number(0.11));
        });

        Assert.Equal(1000d / Math.Pow(1.11, 3), result, 10);
    }

    [Fact]
    public void ValorTerminalPorGordonGrowth()
    {
        // FCF de 1.000, WACC de 11%, crescimento na perpetuidade de 3%.
        double result = Number("D12*(1+$B$4)/($B$3-$B$4)", Cells(
            ("D12", CellValue.Number(1000)),
            ("B3", CellValue.Number(0.11)),
            ("B4", CellValue.Number(0.03))));

        Assert.Equal(1000d * 1.03 / 0.08, result, 8);
    }

    [Fact]
    public void MargemComWaccIgualAoCrescimento_ViraDivisaoPorZero()
    {
        // Erro clássico de modelagem: g igual ao WACC explode a perpetuidade.
        Assert.Equal(
            CellErrorType.DivideByZero,
            Error("D12*(1+$B$4)/($B$3-$B$4)", Cells(
                ("D12", CellValue.Number(1000)),
                ("B3", CellValue.Number(0.08)),
                ("B4", CellValue.Number(0.08)))));
    }

    // ------------------------------------------------- proteção de aninhamento

    [Fact]
    public void Parser_RecusaAninhamentoAlemDoLimite()
    {
        string profunda = new string('(', 200) + "1" + new string(')', 200);

        FormulaSyntaxException exception =
            Assert.Throws<FormulaSyntaxException>(() => FormulaParser.ParseDefault(profunda));

        Assert.Contains("aninhamento", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_RecusaSinalUnarioRepetidoAlemDoLimite() =>
        Assert.Throws<FormulaSyntaxException>(() => FormulaParser.ParseDefault(new string('-', 200) + "1"));

    [Fact]
    public void Parser_AceitaAninhamentoDentroDoLimite() =>
        Assert.Equal(1d, Number(new string('(', 60) + "1" + new string(')', 60)));
}
