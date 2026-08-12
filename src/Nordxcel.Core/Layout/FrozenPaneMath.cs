namespace Nordxcel.Core.Layout;

/// <summary>
/// Aritmética de painéis congelados: quanto espaço o painel ocupa e qual
/// deslocamento de rolagem usar para posicionar o conteúdo do quadrante rolável.
/// <para>
/// Existe para isolar uma conta fácil de errar — e que já errou uma vez neste
/// projeto. O quadrante rolável desenha a partir de uma origem que já pula o
/// painel congelado (<c>RowHeaderWidth + frozenWidth</c>); a rolagem usada para
/// posicionar conteúdo <b>dentro</b> desse quadrante precisa descontar essa
/// largura, ou ela é somada duas vezes e o conteúdo aparece deslocado para a
/// esquerda do próprio recorte assim que algum painel é congelado. É invisível em
/// qualquer teste sem painel congelado, porque a extensão congelada é zero e a
/// conta degenera na fórmula sem congelamento — por isso vale um teste dedicado.
/// </para>
/// </summary>
public static class FrozenPaneMath
{
    /// <summary>
    /// Extensão ocupada pelo painel congelado num eixo, com uma folga mínima
    /// reservada para o quadrante rolável nunca ser engolido por congelar linhas
    /// ou colunas demais.
    /// </summary>
    public static double FrozenExtent(AxisMetrics axis, int frozenCount, double viewportExtent, double minScrollableExtent)
    {
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentOutOfRangeException.ThrowIfNegative(minScrollableExtent);

        if (frozenCount <= 0)
        {
            return 0d;
        }

        double raw = axis.OffsetOf(Math.Min(frozenCount, axis.Count));

        return Math.Min(raw, Math.Max(0d, viewportExtent - minScrollableExtent));
    }

    /// <summary>
    /// Deslocamento de rolagem para o quadrante rolável: o scroll absoluto — no
    /// mesmo espaço de <see cref="AxisMetrics.OffsetOf"/>, o que <c>ScrollIntoView</c>
    /// e o mínimo de rolagem também usam — menos a extensão congelada.
    /// </summary>
    public static double ScrollableOffset(double scroll, double frozenExtent) => scroll - frozenExtent;
}
