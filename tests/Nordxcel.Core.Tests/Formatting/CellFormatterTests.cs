using Nordxcel.Core.Formatting;
using Nordxcel.Core.Model;
using Nordxcel.Core.Model.Styling;

namespace Nordxcel.Core.Tests.Formatting;

public class CellFormatterTests
{
    private static readonly CellFormatter Formatter = new();

    private static string Format(double number, string mask) =>
        Formatter.FormatToText(CellValue.Number(number), mask);

    // ------------------------------------------------- a tabela do roadmap

    [Theory]
    [InlineData(1234567, "1.234.567")]
    [InlineData(-1234567, "(1.234.567)")]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.000")]
    public void Milhar_UsaParentesesNoNegativo(double number, string expected) =>
        Assert.Equal(expected, Format(number, StandardNumberFormats.Thousands));

    [Theory]
    [InlineData(1234567, "R$ 1.234.567")]
    [InlineData(-1234567, "(R$ 1.234.567)")]
    public void Moeda_MantemOSimboloDentroDosParenteses(double number, string expected) =>
        Assert.Equal(expected, Format(number, StandardNumberFormats.CurrencyReal));

    [Theory]
    [InlineData(0.125, "12,5%")]
    [InlineData(-0.125, "(12,5%)")]
    [InlineData(1, "100,0%")]
    public void Porcentagem_MultiplicaPorCem(double number, string expected) =>
        Assert.Equal(expected, Format(number, StandardNumberFormats.Percent));

    [Theory]
    [InlineData(10.2, "10,2x")]
    [InlineData(-10.2, "(10,2x)")]
    [InlineData(8, "8,0x")]
    public void Multiplo_UsaOSufixoX(double number, string expected) =>
        Assert.Equal(expected, Format(number, StandardNumberFormats.Multiple));

    [Fact]
    public void Data_NoPadraoBrasileiro()
    {
        double serial = StandardNumberFormats.ToSerial(new DateTime(2025, 3, 31));

        Assert.Equal("31/03/2025", Format(serial, StandardNumberFormats.ShortDate));
    }

    // ------------------------------------------------------------- separadores

    [Fact]
    public void MascaraEhCanonicaEAExibicaoEhLocalizada()
    {
        // A máscara sempre usa ponto decimal e vírgula de milhar, como no .xlsx.
        const string mask = "#,##0.00";

        Assert.Equal("1.234,50", Formatter.FormatToText(CellValue.Number(1234.5), mask));

        var american = new CellFormatter(NumberFormatCulture.EnUs);

        Assert.Equal("1,234.50", american.FormatToText(CellValue.Number(1234.5), mask));
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(12, "12")]
    [InlineData(123, "123")]
    [InlineData(1234, "1.234")]
    [InlineData(12345, "12.345")]
    [InlineData(123456, "123.456")]
    [InlineData(1234567890, "1.234.567.890")]
    public void SeparadorDeMilhar_AgrupaDeTresEmTres(double number, string expected) =>
        Assert.Equal(expected, Format(number, "#,##0"));

    // ----------------------------------------------------------- casas decimais

    [Theory]
    [InlineData(1234.5678, "0", "1235")]
    [InlineData(1234.5678, "0.0", "1234,6")]
    [InlineData(1234.5678, "0.00", "1234,57")]
    [InlineData(2.5, "0", "3")]
    [InlineData(-2.5, "0", "-3")]
    public void Arredondamento_MeioParaLongeDoZero(double number, string mask, string expected) =>
        Assert.Equal(expected, Format(number, mask));

    [Fact]
    public void Cerquilha_OmiteODigitoAusenteEZeroForcaAExibicao()
    {
        Assert.Equal("1,5", Format(1.5, "0.##"));
        Assert.Equal("1,50", Format(1.5, "0.00"));
        Assert.Equal("1", Format(1, "0.##"));
        Assert.Equal("1,00", Format(1, "0.00"));
    }

    [Fact]
    public void ParteInteiraSemZero_EscondeOZeroSozinho()
    {
        Assert.Equal(",5", Format(0.5, "#.0"));
        Assert.Equal("0,5", Format(0.5, "0.0"));
    }

    [Fact]
    public void ZerosAEsquerda_SaoForcadosPelaMascara() =>
        Assert.Equal("007", Format(7, "000"));

    // ---------------------------------------------------------------- escala

    [Fact]
    public void VirgulaNoFim_DividePorMil()
    {
        Assert.Equal("1.235", Format(1234567, StandardNumberFormats.InThousands));
        Assert.Equal("1,2", Format(1234567, StandardNumberFormats.InMillions));
    }

    // ----------------------------------------------------------- seções da máscara

    [Fact]
    public void UmaSecao_AcrescentaOSinalDeMenos() =>
        Assert.Equal("-1.234", Format(-1234, "#,##0"));

    [Fact]
    public void DuasSecoes_ZeroUsaASecaoPositiva() =>
        Assert.Equal("0", Format(0, "#,##0;(#,##0)"));

    [Fact]
    public void TresSecoes_ZeroTemASuaPropria()
    {
        const string mask = "#,##0;(#,##0);\"-\"";

        Assert.Equal("1.234", Format(1234, mask));
        Assert.Equal("(1.234)", Format(-1234, mask));
        Assert.Equal("-", Format(0, mask));
    }

    [Fact]
    public void SecaoVazia_EscondeOValor() =>
        Assert.Equal(string.Empty, Format(0, "#,##0;(#,##0);"));

    [Fact]
    public void QuatroSecoes_AUltimaFormataOTexto() =>
        Assert.Equal(
            "[Receita]",
            Formatter.FormatToText(CellValue.Text("Receita"), "#,##0;(#,##0);\"-\";\"[\"@\"]\""));

    [Fact]
    public void TextoSemSecaoDeTexto_ApareceComoEsta() =>
        Assert.Equal("Receita", Formatter.FormatToText(CellValue.Text("Receita"), "#,##0"));

    // ------------------------------------------------------------------ cores

    [Fact]
    public void CorEntreColchetes_VoltaJuntoComOTexto()
    {
        FormattedValue positive = Formatter.Format(
            CellValue.Number(1234), StandardNumberFormats.ThousandsRedNegative);

        FormattedValue negative = Formatter.Format(
            CellValue.Number(-1234), StandardNumberFormats.ThousandsRedNegative);

        Assert.Null(positive.Color);
        Assert.Equal(new RgbColor(255, 0, 0), negative.Color);
        Assert.Equal("(1.234)", negative.Text);
    }

    // ------------------------------------------------------- outros tipos

    [Fact]
    public void CelulaVazia_NaoMostraNada() =>
        Assert.Equal(string.Empty, Formatter.FormatToText(CellValue.Blank, StandardNumberFormats.Thousands));

    [Fact]
    public void Erro_NenhumaMascaraODisfarca() =>
        Assert.Equal(
            "#DIV/0!",
            Formatter.FormatToText(CellValue.Error(CellErrorType.DivideByZero), StandardNumberFormats.Thousands));

    [Fact]
    public void Logico_ApareceEmPortugues() =>
        Assert.Equal("VERDADEIRO", Formatter.FormatToText(CellValue.True, StandardNumberFormats.Thousands));

    [Fact]
    public void SemMascara_UsaOFormatoGeral()
    {
        Assert.Equal("1234,5", Formatter.FormatToText(CellValue.Number(1234.5), null));
        Assert.Equal("1234,5", Formatter.FormatToText(CellValue.Number(1234.5), "General"));
        Assert.Equal("0,1", Formatter.FormatToText(CellValue.Number(0.1), null));
    }

    [Fact]
    public void FormatoGeral_NaoExpoeRuidoDePontoFlutuante() =>
        Assert.Equal("0,3", Formatter.FormatToText(CellValue.Number(0.1 + 0.2), null));

    [Fact]
    public void Format_ACelulaUsaAMascaraDela()
    {
        var cell = new Cell
        {
            Value = CellValue.Number(-1234567),
            NumberFormat = StandardNumberFormats.Thousands,
        };

        Assert.Equal("(1.234.567)", Formatter.Format(cell).Text);
    }

    // ------------------------------------------------------------------ datas

    [Fact]
    public void DataSerial_ConverteNosDoisSentidos()
    {
        var date = new DateTime(2025, 8, 12);
        double serial = ExcelDate.ToSerial(date);

        Assert.True(ExcelDate.TryFromSerial(serial, out DateTime roundTripped));
        Assert.Equal(date, roundTripped);
    }

    [Fact]
    public void DataSerial_ReproduzOsValoresConhecidosDoExcel()
    {
        // Referências conhecidas: 01/01/1900 é 1 e 01/01/2000 é 36526.
        Assert.Equal(1d, ExcelDate.ToSerial(new DateTime(1900, 1, 1)));
        Assert.Equal(36_526d, ExcelDate.ToSerial(new DateTime(2000, 1, 1)));
    }

    [Fact]
    public void DataSerial_ForaDoIntervalo_ViraErro()
    {
        Assert.False(ExcelDate.TryFromSerial(-1, out _));
        Assert.Equal("#NÚM!", Format(-1, StandardNumberFormats.ShortDate));
    }

    [Fact]
    public void MascaraDeData_AceitaTokensCurtosELongos()
    {
        double serial = StandardNumberFormats.ToSerial(new DateTime(2025, 3, 5));

        Assert.Equal("5/3/25", Format(serial, "d/m/yy"));
        Assert.Equal("05/03/2025", Format(serial, "dd/mm/yyyy"));
    }

    [Fact]
    public void MascaraDeData_DistingueMesDeMinutoPeloContexto()
    {
        double serial = StandardNumberFormats.ToSerial(new DateTime(2025, 3, 5, 14, 7, 0));

        // O primeiro mm é mês; o segundo, depois de hh, é minuto.
        Assert.Equal("03 14:07", Format(serial, "mm hh:mm"));
    }

    [Fact]
    public void TryParseDate_LeODigitadoNoPadraoBrasileiro()
    {
        Assert.True(StandardNumberFormats.TryParseDate("31/12/2025", out double serial));
        Assert.Equal(new DateTime(2025, 12, 31), ExcelDate.TryFromSerial(serial, out DateTime date) ? date : default);

        Assert.False(StandardNumberFormats.TryParseDate("não é data", out _));
    }

    // -------------------------------------------------------- casas decimais

    [Fact]
    public void GetDecimals_LeAQuantidadeAtual()
    {
        Assert.Equal(0, StandardNumberFormats.GetDecimals(StandardNumberFormats.Thousands));
        Assert.Equal(1, StandardNumberFormats.GetDecimals(StandardNumberFormats.Percent));
        Assert.Equal(2, StandardNumberFormats.GetDecimals("#,##0.00;(#,##0.00)"));
    }

    [Fact]
    public void Aumentar_E_DiminuirCasas_PreservamPrefixoESufixo()
    {
        string comUma = StandardNumberFormats.IncreaseDecimals(StandardNumberFormats.Thousands);

        Assert.Equal("#,##0.0;(#,##0.0)", comUma);
        Assert.Equal("1.234,6", Format(1234.56, comUma));

        string semNenhuma = StandardNumberFormats.DecreaseDecimals(comUma);

        Assert.Equal(StandardNumberFormats.Thousands, semNenhuma);
    }

    [Fact]
    public void Aumentar_Casas_NaMoedaMantemOSimbolo()
    {
        string mask = StandardNumberFormats.IncreaseDecimals(StandardNumberFormats.CurrencyReal);

        Assert.Equal("R$ 1.234,6", Format(1234.56, mask));
        Assert.Equal("(R$ 1.234,6)", Format(-1234.56, mask));
    }

    [Fact]
    public void Aumentar_Casas_NoMultiploMantemOX() =>
        Assert.Equal("10,25x", Format(10.25, StandardNumberFormats.IncreaseDecimals(StandardNumberFormats.Multiple)));

    [Fact]
    public void DiminuirCasas_NaoPassaDeZero() =>
        Assert.Equal(0, StandardNumberFormats.GetDecimals(
            StandardNumberFormats.DecreaseDecimals(StandardNumberFormats.Thousands)));

    [Fact]
    public void SemMascara_AumentarCasasComecaDoFormatoDeMilhar() =>
        Assert.Equal("#,##0.0;(#,##0.0)", StandardNumberFormats.IncreaseDecimals(null));

    [Fact]
    public void Currency_MontaMascaraComSimboloCustomizado()
    {
        string mask = StandardNumberFormats.Currency("€");

        Assert.Equal("€ 1.234", Format(1234, mask));
        Assert.Equal("(€ 1.234)", Format(-1234, mask));
    }
}
