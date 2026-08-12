using Nordxcel.Core.Evaluation;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Evaluation;

public class FinancialFunctionsTests
{
    private const string MainSheet = "DCF";

    private static CellValue Eval(string formula, Action<Worksheet>? setup = null)
    {
        var workbook = new Workbook();
        Worksheet sheet = workbook.AddWorksheet(MainSheet);
        setup?.Invoke(sheet);

        var evaluator = new FormulaEvaluator(new WorkbookEvaluationContext(workbook));

        return evaluator.Evaluate(FormulaParser.ParseDefault(formula), new EvaluationScope(MainSheet));
    }

    private static double Number(string formula, Action<Worksheet>? setup = null)
    {
        CellValue value = Eval(formula, setup);

        Assert.True(value.IsNumber, $"Esperava número, veio {value}.");

        return value.AsNumber();
    }

    private static CellErrorType Error(string formula, Action<Worksheet>? setup = null)
    {
        CellValue value = Eval(formula, setup);

        Assert.True(value.IsError, $"Esperava erro, veio {value}.");

        return value.AsError();
    }

    /// <summary>Preenche a linha 1 a partir de A1, que é como um fluxo de caixa fica no modelo.</summary>
    private static Action<Worksheet> Row1(params double[] values) => sheet =>
    {
        for (int column = 0; column < values.Length; column++)
        {
            sheet.SetValue(new CellAddress(0, column), CellValue.Number(values[column]));
        }
    };

    // -------------------------------------------------------------------- VPL

    [Fact]
    public void Vpl_DescontaOPrimeiroFluxoPorUmPeriodo()
    {
        // Convenção do Excel: o primeiro valor já vale um período à frente.
        double expected = (1000d / 1.1d) + (1000d / Math.Pow(1.1d, 2)) + (1000d / Math.Pow(1.1d, 3));

        Assert.Equal(expected, Number("VPL(0,1;1000;1000;1000)"), 9);
    }

    [Fact]
    public void Vpl_DeIntervalo()
    {
        double expected = (500d / 1.08d) + (700d / Math.Pow(1.08d, 2)) + (900d / Math.Pow(1.08d, 3));

        Assert.Equal(expected, Number("VPL(0,08;A1:C1)", Row1(500, 700, 900)), 9);
    }

    [Fact]
    public void Vpl_InvestimentoInicialEntraForaDaFuncao()
    {
        // O padrão de mercado: VPL dos fluxos futuros mais o desembolso em t=0.
        double expected = -1000d + (600d / 1.1d) + (600d / Math.Pow(1.1d, 2));

        Assert.Equal(expected, Number("VPL(0,1;B1:C1)+A1", Row1(-1000, 600, 600)), 9);
    }

    [Fact]
    public void Vpl_IgnoraTextoDentroDoIntervalo()
    {
        double result = Number("VPL(0,1;A1:C1)", sheet =>
        {
            sheet.SetValue(CellAddress.Parse("A1"), CellValue.Number(1000));
            sheet.SetValue(CellAddress.Parse("B1"), CellValue.Text("n/d"));
            sheet.SetValue(CellAddress.Parse("C1"), CellValue.Number(1000));
        });

        // O texto some da série, então o terceiro fluxo passa a ser o segundo período.
        Assert.Equal((1000d / 1.1d) + (1000d / Math.Pow(1.1d, 2)), result, 9);
    }

    [Fact]
    public void Vpl_TaxaDeMenosCem_ViraDivisaoPorZero() =>
        Assert.Equal(CellErrorType.DivideByZero, Error("VPL(-1;1000)"));

    [Fact]
    public void Vpl_PropagaErroDaSerie() =>
        Assert.Equal(
            CellErrorType.NotAvailable,
            Error("VPL(0,1;A1:B1)", sheet =>
            {
                sheet.SetValue(CellAddress.Parse("A1"), CellValue.Number(100));
                sheet.SetValue(CellAddress.Parse("B1"), CellValue.Error(CellErrorType.NotAvailable));
            }));

    // -------------------------------------------------------------------- TIR

    [Fact]
    public void Tir_ZeraOValorPresenteDaSerie()
    {
        double rate = Number("TIR(A1:D1)", Row1(-1000, 500, 500, 500));

        // Conferência independente: o VPL na taxa encontrada tem que dar zero.
        double presentValue = -1000d +
                              (500d / (1d + rate)) +
                              (500d / Math.Pow(1d + rate, 2)) +
                              (500d / Math.Pow(1d + rate, 3));

        Assert.Equal(0d, presentValue, 9);
        Assert.Equal(0.23375d, rate, 5);
    }

    [Fact]
    public void Tir_PrimeiroFluxoFicaEmZero()
    {
        // Duplicar o investimento e receber o dobro em um período é 100% de retorno.
        Assert.Equal(1d, Number("TIR(A1:B1)", Row1(-100, 200)), 9);
    }

    [Fact]
    public void Tir_TaxaNegativaQuandoOProjetoDestroiValor()
    {
        double rate = Number("TIR(A1:C1)", Row1(-1000, 300, 300));

        Assert.True(rate < 0d, $"Esperava taxa negativa, veio {rate}.");

        double presentValue = -1000d + (300d / (1d + rate)) + (300d / Math.Pow(1d + rate, 2));

        Assert.Equal(0d, presentValue, 9);
    }

    [Fact]
    public void Tir_RespeitaOPalpiteInformado() =>
        Assert.Equal(
            Number("TIR(A1:D1)", Row1(-1000, 500, 500, 500)),
            Number("TIR(A1:D1;0,5)", Row1(-1000, 500, 500, 500)),
            9);

    [Fact]
    public void Tir_SemTrocaDeSinal_ViraErroNumerico()
    {
        Assert.Equal(CellErrorType.Number, Error("TIR(A1:C1)", Row1(100, 200, 300)));
        Assert.Equal(CellErrorType.Number, Error("TIR(A1:C1)", Row1(-100, -200, -300)));
    }

    [Fact]
    public void Tir_ComUmUnicoFluxo_ViraErroNumerico() =>
        Assert.Equal(CellErrorType.Number, Error("TIR(A1:A1)", Row1(-100)));

    [Fact]
    public void Tir_ConvergeEmSerieLongaDePrivateEquity()
    {
        // Entrada de 500, cinco anos sem distribuição e saída de 1.400 na venda.
        double rate = Number("TIR(A1:G1)", Row1(-500, 0, 0, 0, 0, 0, 1400));

        Assert.Equal(Math.Pow(1400d / 500d, 1d / 6d) - 1d, rate, 8);
    }

    // --------------------------------------------------------------------- VF

    [Fact]
    public void Vf_CapitalizaOValorPresente()
    {
        // 1.000 aplicados a 5% por 10 anos.
        Assert.Equal(1000d * Math.Pow(1.05d, 10), Number("VF(0,05;10;0;-1000)"), 8);
    }

    [Fact]
    public void Vf_AcumulaOsAportesPeriodicos()
    {
        double expected = 100d * (Math.Pow(1.05d, 10) - 1d) / 0.05d;

        Assert.Equal(expected, Number("VF(0,05;10;-100)"), 8);
    }

    [Fact]
    public void Vf_PagamentoNoComecoDoPeriodoRendeUmPeriodoAMais()
    {
        double atEnd = Number("VF(0,05;10;-100;0;0)");
        double atStart = Number("VF(0,05;10;-100;0;1)");

        Assert.Equal(atEnd * 1.05d, atStart, 8);
    }

    [Fact]
    public void Vf_ComTaxaZero_SoSomaOsAportes() =>
        Assert.Equal(1000d, Number("VF(0;10;-100)"), 9);

    [Fact]
    public void Vf_TipoInvalido_ViraErroNumerico() =>
        Assert.Equal(CellErrorType.Number, Error("VF(0,05;10;-100;0;2)"));

    [Fact]
    public void Vf_SegueAConvencaoDeSinalDoExcel()
    {
        // Dinheiro que sai é negativo, então o valor futuro volta positivo.
        Assert.True(Number("VF(0,05;10;0;-1000)") > 0d);
        Assert.True(Number("VF(0,05;10;0;1000)") < 0d);
    }

    // ------------------------------------------------------------------- TAXA

    [Fact]
    public void Taxa_EncontraATaxaQueLevaDoPresenteAoFuturo() =>
        Assert.Equal(
            0.05d,
            Number("TAXA(10;0;-1000;A1)", Row1(1000d * Math.Pow(1.05d, 10))),
            8);

    [Fact]
    public void Taxa_DeUmaSerieDePagamentos()
    {
        // Financiamento de 1.000 em 12 prestações de 100.
        double rate = Number("TAXA(12;-100;1000)");

        // Conferência independente: o valor presente das prestações tem que fechar em 1.000.
        double presentValue = 0d;

        for (int period = 1; period <= 12; period++)
        {
            presentValue += 100d / Math.Pow(1d + rate, period);
        }

        Assert.Equal(1000d, presentValue, 6);
    }

    [Fact]
    public void Taxa_EhOInversoDeVf()
    {
        double futureValue = Number("VF(0,07;15;-250;-1000)");

        Assert.Equal(0.07d, Number("TAXA(15;-250;-1000;A1)", Row1(futureValue)), 7);
    }

    [Fact]
    public void Taxa_ComPeriodoInvalido_ViraErroNumerico()
    {
        Assert.Equal(CellErrorType.Number, Error("TAXA(0;-100;1000)"));
        Assert.Equal(CellErrorType.Number, Error("TAXA(-5;-100;1000)"));
    }

    [Fact]
    public void Taxa_SemSolucao_ViraErroNumerico()
    {
        // Pagar e receber com o mesmo sinal nunca fecha em nenhuma taxa.
        Assert.Equal(CellErrorType.Number, Error("TAXA(10;100;1000)"));
    }

    // ------------------------------------------------------- valuation completo

    [Fact]
    public void ValuationCompleto_FluxosDescontadosMaisPerpetuidade()
    {
        // FCFs de 5 anos, WACC 11%, crescimento na perpetuidade 3%.
        double result = Number(
            "VPL($A$3;B1:F1)+(F1*(1+$B$3))/($A$3-$B$3)/(1+$A$3)^5",
            sheet =>
            {
                double[] flows = [0, 1000, 1100, 1210, 1331, 1464.1];

                for (int column = 0; column < flows.Length; column++)
                {
                    sheet.SetValue(new CellAddress(0, column), CellValue.Number(flows[column]));
                }

                sheet.SetValue(CellAddress.Parse("A3"), CellValue.Number(0.11));
                sheet.SetValue(CellAddress.Parse("B3"), CellValue.Number(0.03));
            });

        double explicitPeriod = 0d;
        double[] projected = [1000, 1100, 1210, 1331, 1464.1];

        for (int year = 1; year <= projected.Length; year++)
        {
            explicitPeriod += projected[year - 1] / Math.Pow(1.11d, year);
        }

        double terminalValue = 1464.1d * 1.03d / (0.11d - 0.03d) / Math.Pow(1.11d, 5);

        Assert.Equal(explicitPeriod + terminalValue, result, 6);
    }

    [Fact]
    public void Catalogo_TemAsFuncoesFinanceirasDoRoadmap()
    {
        foreach (string name in new[] { "VPL", "TIR", "VF", "TAXA" })
        {
            Assert.True(
                Nordxcel.Core.Evaluation.Functions.FunctionRegistry.Standard.TryGetFunction(name, out _),
                $"Faltou registrar {name}.");
        }
    }
}
