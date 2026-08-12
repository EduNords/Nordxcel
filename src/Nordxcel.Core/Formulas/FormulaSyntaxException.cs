namespace Nordxcel.Core.Formulas;

/// <summary>
/// Fórmula malformada. Carrega a posição do caractere problemático para que a
/// barra de fórmulas consiga posicionar o cursor ali.
/// </summary>
public sealed class FormulaSyntaxException : Exception
{
    public FormulaSyntaxException(string message, int position)
        : base(message) => Position = position;

    /// <summary>Índice, base zero, do caractere onde o problema foi detectado.</summary>
    public int Position { get; }
}
