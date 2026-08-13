using Nordxcel.Core.Evaluation;
using Nordxcel.Core.Evaluation.Functions;
using Nordxcel.Core.Formatting;
using Nordxcel.Core.Formulas;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Tests.Evaluation;

public class BuiltinFunctionsTests
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

    /// <summary>Preenche A1:A5 com os valores informados.</summary>
    private static Action<Worksheet> ColumnA(params CellValue[] values) => sheet =>
    {
        for (int row = 0; row < values.Length; row++)
        {
            sheet.SetValue(new CellAddress(row, 0), values[row]);
        }
    };

    // ------------------------------------------------------------- agregação

    [Fact]
    public void Soma_DeIntervalo() =>
        Assert.Equal(60d, Number("SOMA(A1:A3)", ColumnA(
            CellValue.Number(10),
            CellValue.Number(20),
            CellValue.Number(30))));

    [Fact]
    public void Soma_DeArgumentosSoltosEIntervalosMisturados() =>
        Assert.Equal(115d, Number("SOMA(A1:A3;5;10)", ColumnA(
            CellValue.Number(10),
            CellValue.Number(20),
            CellValue.Number(70))));

    [Fact]
    public void Soma_IgnoraTextoELogicoDentroDeIntervalo() =>
        Assert.Equal(30d, Number("SOMA(A1:A4)", ColumnA(
            CellValue.Number(10),
            CellValue.Text("n/d"),
            CellValue.True,
            CellValue.Number(20))));

    [Fact]
    public void Soma_ConverteLogicoEscritoNaFormula()
    {
        // A distinção do Excel: literal é convertido, célula não.
        Assert.Equal(1d, Number("SOMA(VERDADEIRO)"));
        Assert.Equal(0d, Number("SOMA(A1)", ColumnA(CellValue.True)));
    }

    [Fact]
    public void Soma_ConverteTextoNumericoEscritoNaFormula() =>
        Assert.Equal(15d, Number("SOMA(\"10\";5)"));

    [Fact]
    public void Soma_TextoNaoNumericoEscritoNaFormula_ViraErroDeValor() =>
        Assert.Equal(CellErrorType.Value, Error("SOMA(\"abc\")"));

    [Fact]
    public void Soma_PropagaErroDeDentroDoIntervalo() =>
        Assert.Equal(CellErrorType.DivideByZero, Error("SOMA(A1:A3)", ColumnA(
            CellValue.Number(10),
            CellValue.Error(CellErrorType.DivideByZero),
            CellValue.Number(30))));

    [Fact]
    public void Soma_DeIntervaloVazio_DaZero() =>
        Assert.Equal(0d, Number("SOMA(A1:A10)"));

    [Fact]
    public void Media_DivideApenasPelosNumerosEncontrados() =>
        Assert.Equal(20d, Number("MÉDIA(A1:A4)", ColumnA(
            CellValue.Number(10),
            CellValue.Text("n/d"),
            CellValue.Number(30),
            CellValue.Blank)));

    [Fact]
    public void Media_SemNenhumNumero_ViraDivisaoPorZero() =>
        Assert.Equal(CellErrorType.DivideByZero, Error("MÉDIA(A1:A5)"));

    [Fact]
    public void MinimoEMaximo()
    {
        Action<Worksheet> setup = ColumnA(
            CellValue.Number(-5),
            CellValue.Number(12),
            CellValue.Number(3));

        Assert.Equal(-5d, Number("MÍNIMO(A1:A3)", setup));
        Assert.Equal(12d, Number("MÁXIMO(A1:A3)", setup));
    }

    [Fact]
    public void MinimoEMaximo_SemNenhumNumero_DaoZero()
    {
        Assert.Equal(0d, Number("MÍNIMO(A1:A5)"));
        Assert.Equal(0d, Number("MÁXIMO(A1:A5)"));
    }

    [Fact]
    public void ContValores_ContaTudoQueNaoEstaVazio() =>
        Assert.Equal(3d, Number("CONT.VALORES(A1:A5)", ColumnA(
            CellValue.Number(10),
            CellValue.Text("rótulo"),
            CellValue.Blank,
            CellValue.Error(CellErrorType.NotAvailable))));

    [Fact]
    public void ContValores_NaoPropagaErro() =>
        Assert.Equal(1d, Number("CONT.VALORES(A1:A3)", ColumnA(CellValue.Error(CellErrorType.DivideByZero))));

    [Fact]
    public void Mult_MultiplicaOsArgumentos() =>
        Assert.Equal(24d, Number("MULT(A1:A3)", ColumnA(
            CellValue.Number(2),
            CellValue.Number(3),
            CellValue.Number(4))));

    [Fact]
    public void Mult_SemNenhumNumero_DaZero() =>
        Assert.Equal(0d, Number("MULT(A1:A5)"));

    [Fact]
    public void Med_ComQuantidadeImpar_DevolveOValorDoMeio() =>
        Assert.Equal(5d, Number("MED(A1:A3)", ColumnA(CellValue.Number(9), CellValue.Number(1), CellValue.Number(5))));

    [Fact]
    public void Med_ComQuantidadePar_DevolveAMediaDosDoisDoMeio() =>
        Assert.Equal(7d, Number("MED(A1:A4)", ColumnA(
            CellValue.Number(9), CellValue.Number(1), CellValue.Number(5), CellValue.Number(20))));

    [Fact]
    public void Med_SemNenhumNumero_ViraErroDeNumero() =>
        Assert.Equal(CellErrorType.Number, Error("MED(A1:A5)"));

    [Fact]
    public void Percentil_MedianaBateComMed() =>
        Assert.Equal(5d, Number("PERCENTIL(A1:A3;0,5)", ColumnA(CellValue.Number(9), CellValue.Number(1), CellValue.Number(5))));

    [Fact]
    public void Percentil_NosExtremosDevolveMinimoEMaximo()
    {
        var setup = ColumnA(CellValue.Number(10), CellValue.Number(20), CellValue.Number(30));

        Assert.Equal(10d, Number("PERCENTIL(A1:A3;0)", setup));
        Assert.Equal(30d, Number("PERCENTIL(A1:A3;1)", setup));
    }

    [Fact]
    public void Percentil_NaPosicaoExata_NaoInterpola() =>
        // 5 valores (posições 0 a 4), k=0,25 cai exatamente na posição 1 (valor 10).
        Assert.Equal(10d, Number("PERCENTIL(A1:A5;0,25)", ColumnA(
            CellValue.Number(0), CellValue.Number(10), CellValue.Number(20), CellValue.Number(30), CellValue.Number(40))));

    [Fact]
    public void Percentil_EntreDuasPosicoes_Interpola() =>
        // 4 valores (0, 10, 20, 30), k=0,25 cai a 3/4 do caminho entre a posição 0 e a 1.
        Assert.Equal(7.5d, Number("PERCENTIL(A1:A4;0,25)", ColumnA(
            CellValue.Number(0), CellValue.Number(10), CellValue.Number(20), CellValue.Number(30))));

    [Fact]
    public void Percentil_ForaDeZeroEUm_ViraErroDeNumero() =>
        Assert.Equal(CellErrorType.Number, Error("PERCENTIL(A1:A3;1,5)", ColumnA(CellValue.Number(1))));

    // --------------------------------------------------------------- lógicas

    [Fact]
    public void Se_EscolheORamoCerto()
    {
        Assert.Equal(CellValue.Text("sim"), Eval("SE(1=1;\"sim\";\"não\")"));
        Assert.Equal(CellValue.Text("não"), Eval("SE(1=2;\"sim\";\"não\")"));
    }

    [Fact]
    public void Se_NaoAvaliaORamoDescartado()
    {
        // O ponto todo de SE(A1=0;0;1/A1): sem avaliação preguiçosa isso daria #DIV/0!.
        Assert.Equal(0d, Number("SE(A1=0;0;1/A1)", ColumnA(CellValue.Number(0))));
        Assert.Equal(0.25d, Number("SE(A1=0;0;1/A1)", ColumnA(CellValue.Number(4))));
    }

    [Fact]
    public void Se_SemOTerceiroArgumento_DevolveFalso() =>
        Assert.Equal(CellValue.False, Eval("SE(1=2;\"sim\")"));

    [Fact]
    public void Se_ComArgumentoOmitido_DevolveZero() =>
        Assert.Equal(0d, Number("SE(1=1;;5)"));

    [Fact]
    public void Se_CondicaoQueNaoEhLogica_ViraErroDeValor() =>
        Assert.Equal(CellErrorType.Value, Error("SE(\"abc\";1;2)"));

    [Fact]
    public void Se_CondicaoNumericaSegueARegraDoExcel()
    {
        Assert.Equal(1d, Number("SE(3;1;2)"));
        Assert.Equal(2d, Number("SE(0;1;2)"));
    }

    [Fact]
    public void SeErro_SubstituiApenasQuandoDaErro()
    {
        Assert.Equal(0d, Number("SEERRO(1/0;0)"));
        Assert.Equal(2d, Number("SEERRO(4/2;0)"));
    }

    [Fact]
    public void SeErro_ProtegeAMargemQuandoAReceitaEhZero() =>
        Assert.Equal(0d, Number("SEERRO(A1/A2;0)", ColumnA(
            CellValue.Number(150),
            CellValue.Number(0))));

    [Fact]
    public void SeErro_NaoEsconderErroInexistente() =>
        Assert.Equal(CellValue.Text("ok"), Eval("SEERRO(\"ok\";\"escondido\")"));

    [Theory]
    [InlineData("E(VERDADEIRO;VERDADEIRO)", true)]
    [InlineData("E(VERDADEIRO;FALSO)", false)]
    [InlineData("OU(FALSO;FALSO)", false)]
    [InlineData("OU(FALSO;VERDADEIRO)", true)]
    public void EOu_CombinamOsLogicos(string formula, bool expected) =>
        Assert.Equal(CellValue.Logical(expected), Eval(formula));

    [Fact]
    public void EOu_AceitamNumeroComoLogico()
    {
        Assert.Equal(CellValue.True, Eval("E(1;3)"));
        Assert.Equal(CellValue.False, Eval("E(1;0)"));
    }

    [Fact]
    public void EOu_IgnoramTextoDentroDeIntervalo() =>
        Assert.Equal(CellValue.True, Eval("E(A1:A3)", ColumnA(
            CellValue.True,
            CellValue.Text("rótulo"),
            CellValue.Number(1))));

    [Fact]
    public void EOu_SemNenhumLogico_ViramErroDeValor() =>
        Assert.Equal(CellErrorType.Value, Error("E(A1:A5)"));

    [Fact]
    public void EOu_PropagamErroMesmoComOResultadoJaDecidido()
    {
        // Diferente do && de uma linguagem: não há curto-circuito.
        Assert.Equal(CellErrorType.DivideByZero, Error("E(FALSO;1/0)"));
        Assert.Equal(CellErrorType.DivideByZero, Error("OU(VERDADEIRO;1/0)"));
    }

    // ------------------------------------------------------------ referência

    [Fact]
    public void Escolher_DevolveOArgumentoNaPosicaoPedida() =>
        Assert.Equal(CellValue.Text("B"), Eval("ESCOLHER(2;\"A\";\"B\";\"C\")"));

    [Fact]
    public void Escolher_TruncaIndiceComCasasDecimais() =>
        // 2,9 aponta pra segunda opção, não a terceira — igual a ARRED não faria aqui.
        Assert.Equal(CellValue.Text("B"), Eval("ESCOLHER(2,9;\"A\";\"B\";\"C\")"));

    [Theory]
    [InlineData("ESCOLHER(0;\"A\";\"B\")")]
    [InlineData("ESCOLHER(3;\"A\";\"B\")")]
    public void Escolher_IndiceForaDaFaixa_ViraErroDeValor(string formula) =>
        Assert.Equal(CellErrorType.Value, Error(formula));

    // ----------------------------------------------------------- matemáticas

    [Theory]
    [InlineData("ARRED(1234,5678;2)", 1234.57)]
    [InlineData("ARRED(1234,5678;0)", 1235)]
    [InlineData("ARRED(1234,5678;-2)", 1200)]
    [InlineData("ARRED(1250;-2)", 1300)]
    [InlineData("ARRED(2,5;0)", 3)]
    [InlineData("ARRED(-2,5;0)", -3)]
    public void Arred_ArredondaMeioParaLongeDoZero(string formula, double expected) =>
        Assert.Equal(expected, Number(formula), 9);

    [Fact]
    public void Abs()
    {
        Assert.Equal(5d, Number("ABS(-5)"));
        Assert.Equal(5d, Number("ABS(5)"));
    }

    [Fact]
    public void Potencia_SeComportaIgualAoOperador()
    {
        Assert.Equal(1024d, Number("POTÊNCIA(2;10)"));
        Assert.Equal(Number("2^10"), Number("POTÊNCIA(2;10)"));
        Assert.Equal(CellErrorType.Number, Error("POTÊNCIA(0;0)"));
        Assert.Equal(CellErrorType.DivideByZero, Error("POTÊNCIA(0;-1)"));
    }

    [Fact]
    public void Raiz()
    {
        Assert.Equal(3d, Number("RAIZ(9)"));
        Assert.Equal(CellErrorType.Number, Error("RAIZ(-1)"));
    }

    // ------------------------------------------------------------- aninhadas

    [Fact]
    public void FuncoesAninhadas() =>
        Assert.Equal(20d, Number("ARRED(MÉDIA(A1:A3);0)", ColumnA(
            CellValue.Number(10),
            CellValue.Number(20),
            CellValue.Number(31))));

    [Fact]
    public void MargemEbitdaArredondadaComProtecao() =>
        Assert.Equal(0.35d, Number("ARRED(SEERRO(A2/A1;0);4)", ColumnA(
            CellValue.Number(1000),
            CellValue.Number(350))));

    [Fact]
    public void SomaDeFluxosDescontados()
    {
        // Três FCFs de 1.000 descontados a 10%, somados um a um.
        double result = Number("SOMA(A1/(1+0,1)^1;A2/(1+0,1)^2;A3/(1+0,1)^3)", ColumnA(
            CellValue.Number(1000),
            CellValue.Number(1000),
            CellValue.Number(1000)));

        double expected = (1000d / 1.1) + (1000d / Math.Pow(1.1, 2)) + (1000d / Math.Pow(1.1, 3));

        Assert.Equal(expected, result, 9);
    }

    // ------------------------------------------------------------------ data

    [Fact]
    public void Hoje_DevolveOSerialDoDiaDeHoje() =>
        Assert.Equal(ExcelDate.ToSerial(DateTime.Today), Number("HOJE()"));

    [Fact]
    public void FracaoAno_UmAnoCheio_DaUm() =>
        Assert.Equal(1d, Number("FRAÇÃOANO(A1;A2)", ColumnA(
            CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 1, 1))),
            CellValue.Number(ExcelDate.ToSerial(new DateTime(2025, 1, 1))))), 6);

    [Fact]
    public void FracaoAno_MeioAno30_360_DaMeio() =>
        Assert.Equal(0.5d, Number("FRAÇÃOANO(A1;A2;0)", ColumnA(
            CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 1, 1))),
            CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 7, 1))))), 6);

    [Fact]
    public void FracaoAno_NaoDependeDaOrdemDasDatas() =>
        Assert.Equal(
            Number("FRAÇÃOANO(A1;A2)", ColumnA(
                CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 1, 1))),
                CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 7, 1))))),
            Number("FRAÇÃOANO(A1;A2)", ColumnA(
                CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 7, 1))),
                CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 1, 1))))));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FracaoAno_BasesSuportadas_NaoDaoErro(int basis)
    {
        CellValue value = Eval($"FRAÇÃOANO(A1;A2;{basis})", ColumnA(
            CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 1, 1))),
            CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 7, 1)))));

        Assert.True(value.IsNumber, $"Base {basis} devia dar número, deu {value}.");
    }

    [Fact]
    public void FracaoAno_Base1NaoSuportada_ViraErroDeNumero() =>
        Assert.Equal(CellErrorType.Number, Error("FRAÇÃOANO(A1;A2;1)", ColumnA(
            CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 1, 1))),
            CellValue.Number(ExcelDate.ToSerial(new DateTime(2024, 7, 1))))));

    // -------------------------------------------------------------- catálogo

    [Fact]
    public void Catalogo_TemAsFuncoesDoRoadmap()
    {
        IFunctionRegistry registry = FunctionRegistry.Standard;

        foreach (string name in new[]
                 {
                     "SOMA", "MÉDIA", "MÍNIMO", "MÁXIMO", "CONT.VALORES", "MULT", "MED", "PERCENTIL",
                     "SE", "E", "OU", "SEERRO",
                     "ARRED", "ABS", "POTÊNCIA", "RAIZ",
                     "ESCOLHER", "HOJE", "FRAÇÃOANO",
                 })
        {
            Assert.True(registry.TryGetFunction(name, out _), $"Faltou registrar {name}.");
        }
    }

    [Fact]
    public void Catalogo_BuscaSemDiferenciarMaiusculas() =>
        Assert.True(FunctionRegistry.Standard.TryGetFunction("soma", out _));

    [Fact]
    public void Catalogo_RecusaRegistroDuplicado()
    {
        FunctionRegistry registry = FunctionRegistry.CreateStandard();

        Assert.Throws<ArgumentException>(() =>
            registry.Register(new Nordxcel.Core.Evaluation.Functions.Builtins.SumFunction()));
    }

    [Fact]
    public void QuantidadeErradaDeArgumentos_ViraErroDeValor()
    {
        Assert.Equal(CellErrorType.Value, Error("ABS(1;2)"));
        Assert.Equal(CellErrorType.Value, Error("SE(1)"));
        Assert.Equal(CellErrorType.Value, Error("SE(1;2;3;4)"));
    }

    [Fact]
    public void CatalogoVazio_TornaTodaFuncaoDesconhecida()
    {
        var workbook = new Workbook();
        workbook.AddWorksheet(MainSheet);

        var evaluator = new FormulaEvaluator(
            new WorkbookEvaluationContext(workbook),
            EmptyFunctionRegistry.Instance);

        CellValue value = evaluator.Evaluate(
            FormulaParser.ParseDefault("SOMA(1;2)"),
            new EvaluationScope(MainSheet));

        Assert.Equal(CellErrorType.Name, value.AsError());
    }
}
