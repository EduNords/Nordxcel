using System.Globalization;
using Nordxcel.Core.Model;

namespace Nordxcel.Core.Formulas;

/// <summary>
/// Convenções de escrita de fórmula: separador decimal e separador de argumentos.
/// <para>
/// O padrão é o do Excel em português — decimal com vírgula e argumentos com
/// ponto e vírgula (<c>SOMA(A1:A3;1,5)</c>) — coerente com os nomes de função em
/// português que o Nordxcel usa. Os dois separadores precisam ser diferentes, que é
/// justamente o motivo de o Excel pt-BR trocar a vírgula por ponto e vírgula.
/// </para>
/// </summary>
public sealed class FormulaSyntax
{
    private FormulaSyntax(
        char decimalSeparator,
        char argumentSeparator,
        string trueLiteral,
        string falseLiteral,
        IReadOnlyDictionary<string, string>? functionNames,
        IReadOnlyDictionary<CellErrorType, string>? errorTokens)
    {
        if (decimalSeparator == argumentSeparator)
        {
            throw new ArgumentException(
                "O separador decimal e o de argumentos precisam ser diferentes.",
                nameof(argumentSeparator));
        }

        DecimalSeparator = decimalSeparator;
        ArgumentSeparator = argumentSeparator;
        TrueLiteral = trueLiteral;
        FalseLiteral = falseLiteral;
        FunctionNames = functionNames;
        ErrorTokens = errorTokens;

        NumberFormat = new NumberFormatInfo
        {
            NumberDecimalSeparator = decimalSeparator.ToString(),
        };
    }

    /// <summary>
    /// Nome em português de cada função embutida do Nordxcel para o nome canônico
    /// em inglês que o <c>.xlsx</c> entende. Precisa cobrir toda função registrada
    /// em <see cref="Evaluation.Functions.FunctionRegistry.CreateStandard"/> — é
    /// isso que <see cref="FormulaWriter"/> cobra na exportação, para uma função
    /// nova não vazar sem tradução e virar <c>#NAME?</c> silenciosamente no Excel.
    /// <para>
    /// Declarado antes de <see cref="EnUs"/> de propósito: inicializador de campo
    /// estático roda na ordem do texto, então se <see cref="EnUs"/> viesse
    /// primeiro no arquivo ele capturaria <c>null</c> aqui.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> EnUsFunctionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SOMA"] = "SUM",
        ["MÉDIA"] = "AVERAGE",
        ["MÍNIMO"] = "MIN",
        ["MÁXIMO"] = "MAX",
        ["CONT.VALORES"] = "COUNTA",
        ["SE"] = "IF",
        ["SEERRO"] = "IFERROR",
        ["E"] = "AND",
        ["OU"] = "OR",
        ["ARRED"] = "ROUND",
        ["ABS"] = "ABS",
        ["POTÊNCIA"] = "POWER",
        ["RAIZ"] = "SQRT",
        ["VPL"] = "NPV",
        ["TIR"] = "IRR",
        ["VF"] = "FV",
        ["TAXA"] = "RATE",
    };

    /// <summary>
    /// <see cref="CellErrorType.Circular"/> fica de fora de propósito: não existe
    /// no Excel, e a exportação recusa a pasta inteira antes de chegar a escrever
    /// qualquer fórmula se ela tiver uma referência circular pendente. Mesmo
    /// motivo de ordem de declaração que <see cref="EnUsFunctionNames"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<CellErrorType, string> EnUsErrorTokens = new Dictionary<CellErrorType, string>
    {
        [CellErrorType.DivideByZero] = "#DIV/0!",
        [CellErrorType.Value] = "#VALUE!",
        [CellErrorType.Reference] = "#REF!",
        [CellErrorType.Name] = "#NAME?",
        [CellErrorType.Number] = "#NUM!",
        [CellErrorType.NotAvailable] = "#N/A",
        [CellErrorType.Null] = "#NULL!",
    };

    /// <summary>Convenção brasileira: <c>1,5</c> e <c>SOMA(A1;B1)</c>.</summary>
    public static FormulaSyntax PtBr { get; } = new(',', ';', "VERDADEIRO", "FALSO", null, null);

    /// <summary>
    /// Convenção americana: <c>1.5</c> e <c>SUM(A1,B1)</c>. É a sintaxe que o
    /// <c>.xlsx</c> exige por baixo — o formato OOXML grava fórmula sempre em
    /// inglês/EUA, mesmo quando o Excel que salvou o arquivo está em português;
    /// a tradução para "SOMA" na tela é só de exibição. É por isso que a
    /// exportação usa esta sintaxe para escrever <c>&lt;f&gt;</c>, não a padrão.
    /// </summary>
    public static FormulaSyntax EnUs { get; } = new('.', ',', "TRUE", "FALSE", EnUsFunctionNames, EnUsErrorTokens);

    /// <summary>Convenção usada quando nenhuma é informada.</summary>
    public static FormulaSyntax Default => PtBr;

    public char DecimalSeparator { get; }

    public char ArgumentSeparator { get; }

    /// <summary>Literal lógico verdadeiro (<c>VERDADEIRO</c> ou <c>TRUE</c>).</summary>
    public string TrueLiteral { get; }

    /// <summary>Literal lógico falso (<c>FALSO</c> ou <c>FALSE</c>).</summary>
    public string FalseLiteral { get; }

    /// <summary>
    /// Nome de cada função embutida nesta sintaxe, ou <c>null</c> quando o nome
    /// registrado no <see cref="Evaluation.Functions.FunctionRegistry"/> já é o
    /// que se escreve (caso do português, a língua nativa do Nordxcel).
    /// </summary>
    public IReadOnlyDictionary<string, string>? FunctionNames { get; }

    /// <summary>Token de cada erro nesta sintaxe, ou <c>null</c> quando <see cref="CellErrors.ToDisplayText"/> já basta.</summary>
    public IReadOnlyDictionary<CellErrorType, string>? ErrorTokens { get; }

    internal NumberFormatInfo NumberFormat { get; }
}
