using Nordxcel.Core.Model;

namespace Nordxcel.Core.Evaluation.Functions;

/// <summary>
/// Uma função de planilha. Recebe a chamada com os argumentos <b>ainda não
/// avaliados</b>, o que permite a funções como <c>SE</c> e <c>SEERRO</c> avaliarem
/// só o ramo que interessa — sem isso, <c>SE(A1=0;0;1/A1)</c> devolveria
/// <c>#DIV/0!</c> justamente no caso que a fórmula tenta proteger.
/// </summary>
public interface IFormulaFunction
{
    /// <summary>Nome em maiúsculas, como o parser normaliza.</summary>
    string Name { get; }

    /// <summary>Menor quantidade de argumentos aceita.</summary>
    int MinArguments { get; }

    /// <summary>Maior quantidade de argumentos aceita, ou <see cref="int.MaxValue"/> se for livre.</summary>
    int MaxArguments { get; }

    CellValue Invoke(FunctionCall call);
}

/// <summary>Catálogo de funções conhecidas pelo avaliador.</summary>
public interface IFunctionRegistry
{
    /// <summary>Busca pelo nome, sem diferenciar maiúsculas de minúsculas.</summary>
    bool TryGetFunction(string name, out IFormulaFunction? function);
}

/// <summary>
/// Catálogo vazio: toda função é desconhecida e vira <c>#NOME?</c>.
/// É o padrão do avaliador até a biblioteca de funções entrar.
/// </summary>
public sealed class EmptyFunctionRegistry : IFunctionRegistry
{
    public static readonly EmptyFunctionRegistry Instance = new();

    private EmptyFunctionRegistry()
    {
    }

    public bool TryGetFunction(string name, out IFormulaFunction? function)
    {
        function = null;
        return false;
    }
}
