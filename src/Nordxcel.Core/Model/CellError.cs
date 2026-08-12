namespace Nordxcel.Core.Model;

/// <summary>
/// Erros que uma célula pode carregar. Os textos exibidos seguem o Excel em
/// português, já que as funções do Nordxcel também são em português.
/// </summary>
public enum CellErrorType
{
    /// <summary>Valor não inicializado — não deve aparecer em célula calculada.</summary>
    None = 0,

    /// <summary>Divisão por zero (<c>#DIV/0!</c>).</summary>
    DivideByZero,

    /// <summary>Tipo de argumento incompatível (<c>#VALOR!</c>).</summary>
    Value,

    /// <summary>Referência inválida, normalmente por exclusão de linha/coluna (<c>#REF!</c>).</summary>
    Reference,

    /// <summary>Nome de função ou referência desconhecido (<c>#NOME?</c>).</summary>
    Name,

    /// <summary>Argumento numérico fora do domínio, ou iteração que não convergiu (<c>#NÚM!</c>).</summary>
    Number,

    /// <summary>Valor não disponível (<c>#N/D</c>).</summary>
    NotAvailable,

    /// <summary>Interseção de intervalos vazia (<c>#NULO!</c>).</summary>
    Null,

    /// <summary>
    /// Referência circular (<c>#CIRC!</c>). Não existe no Excel, que mostra zero e um aviso;
    /// no Nordxcel o ciclo é recusado e marcado na célula, conforme a decisão do roadmap
    /// de tratar o modelo como um DAG. Cálculo iterativo fica para o LBO.
    /// </summary>
    Circular,
}

/// <summary>Conversão entre <see cref="CellErrorType"/> e o texto exibido na célula.</summary>
public static class CellErrors
{
    private static readonly (CellErrorType Type, string Text)[] Map =
    [
        (CellErrorType.DivideByZero, "#DIV/0!"),
        (CellErrorType.Value, "#VALOR!"),
        (CellErrorType.Reference, "#REF!"),
        (CellErrorType.Name, "#NOME?"),
        (CellErrorType.Number, "#NÚM!"),
        (CellErrorType.NotAvailable, "#N/D"),
        (CellErrorType.Null, "#NULO!"),
        (CellErrorType.Circular, "#CIRC!"),
    ];

    /// <summary>Texto exibido na célula para o erro informado.</summary>
    public static string ToDisplayText(this CellErrorType error)
    {
        foreach ((CellErrorType type, string text) in Map)
        {
            if (type == error)
            {
                return text;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(error), error, "Erro de célula desconhecido.");
    }

    /// <summary>Interpreta um texto como <c>#DIV/0!</c> digitado diretamente na célula.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out CellErrorType error)
    {
        foreach ((CellErrorType type, string candidate) in Map)
        {
            if (text.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                error = type;
                return true;
            }
        }

        error = CellErrorType.None;
        return false;
    }

    /// <summary>
    /// Reconhece um literal de erro no início do texto, devolvendo quantos caracteres
    /// ele ocupa. É o que permite ao tokenizer aceitar <c>SEERRO(A1;#N/D)</c>.
    /// Casa sempre o literal mais longo, para que <c>#NÚM!</c> não seja confundido com <c>#N/D</c>.
    /// </summary>
    public static bool TryMatchPrefix(ReadOnlySpan<char> text, out CellErrorType error, out int length)
    {
        error = CellErrorType.None;
        length = 0;

        foreach ((CellErrorType type, string candidate) in Map)
        {
            if (candidate.Length > length && text.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                error = type;
                length = candidate.Length;
            }
        }

        return length > 0;
    }
}
