using Nordxcel.Core.Evaluation.Functions.Builtins;

namespace Nordxcel.Core.Evaluation.Functions;

/// <summary>Catálogo de funções, indexado pelo nome sem diferenciar maiúsculas.</summary>
public sealed class FunctionRegistry : IFunctionRegistry
{
    private readonly Dictionary<string, IFormulaFunction> _functions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Catálogo padrão do Nordxcel, usado quando nenhum outro é informado.
    /// É exposto pela interface justamente para não poder ser alterado por engano;
    /// quem quiser acrescentar funções usa <see cref="CreateStandard"/>.
    /// </summary>
    public static IFunctionRegistry Standard { get; } = CreateStandard();

    /// <summary>Nomes das funções registradas.</summary>
    public IReadOnlyCollection<string> Names => _functions.Keys;

    /// <summary>Cria um catálogo novo com todas as funções embutidas.</summary>
    public static FunctionRegistry CreateStandard()
    {
        var registry = new FunctionRegistry();

        // Agregação
        registry.Register(new SumFunction());
        registry.Register(new AverageFunction());
        registry.Register(new MinFunction());
        registry.Register(new MaxFunction());
        registry.Register(new CountAFunction());

        // Lógicas
        registry.Register(new IfFunction());
        registry.Register(new IfErrorFunction());
        registry.Register(new AndFunction());
        registry.Register(new OrFunction());

        // Matemáticas
        registry.Register(new RoundFunction());
        registry.Register(new AbsFunction());
        registry.Register(new PowerFunction());
        registry.Register(new SqrtFunction());

        // Financeiras
        registry.Register(new NpvFunction());
        registry.Register(new IrrFunction());
        registry.Register(new FvFunction());
        registry.Register(new RateFunction());

        return registry;
    }

    public void Register(IFormulaFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);

        if (!_functions.TryAdd(function.Name, function))
        {
            throw new ArgumentException($"Já existe uma função chamada '{function.Name}'.", nameof(function));
        }
    }

    public bool TryGetFunction(string name, out IFormulaFunction? function)
    {
        if (string.IsNullOrEmpty(name))
        {
            function = null;
            return false;
        }

        return _functions.TryGetValue(name, out function);
    }
}
