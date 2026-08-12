using Avalonia.Media;

namespace Nordxcel.Desktop.Rendering;

/// <summary>
/// Cores e medidas da grade, calibradas para lembrar o Excel.
/// <para>
/// A planilha é sempre clara, mesmo se o sistema estiver no tema escuro. Isso é
/// deliberado: o sistema de cores de modelagem — azul para premissa, preto para
/// fórmula, verde para link — pressupõe fundo claro, e é convenção de mercado ler
/// o modelo por essas cores.
/// </para>
/// </summary>
public sealed class GridTheme
{
    public static readonly GridTheme Default = new();

    public IBrush CellBackground { get; } = new SolidColorBrush(Color.FromRgb(255, 255, 255)).ToImmutable();

    public IBrush HeaderBackground { get; } = new SolidColorBrush(Color.FromRgb(245, 245, 245)).ToImmutable();

    public IBrush HeaderForeground { get; } = new SolidColorBrush(Color.FromRgb(68, 68, 68)).ToImmutable();

    public IPen GridLine { get; } =
        new Pen(new SolidColorBrush(Color.FromRgb(214, 214, 214)).ToImmutable(), 1).ToImmutable();

    public IPen HeaderLine { get; } =
        new Pen(new SolidColorBrush(Color.FromRgb(190, 190, 190)).ToImmutable(), 1).ToImmutable();

    /// <summary>Fundo da área além do fim da planilha.</summary>
    public IBrush OutsideBackground { get; } = new SolidColorBrush(Color.FromRgb(250, 250, 250)).ToImmutable();

    /// <summary>Verde do Excel, usado no contorno da seleção.</summary>
    public Color SelectionColor { get; } = Color.FromRgb(33, 115, 70);

    /// <summary>Preenchimento translúcido do intervalo selecionado.</summary>
    public IBrush SelectionFill { get; } = new SolidColorBrush(Color.FromArgb(28, 33, 115, 70)).ToImmutable();

    public IPen SelectionBorder { get; } =
        new Pen(new SolidColorBrush(Color.FromRgb(33, 115, 70)).ToImmutable(), 2).ToImmutable();

    public IBrush HeaderSelectedBackground { get; } = new SolidColorBrush(Color.FromRgb(217, 231, 223)).ToImmutable();

    public IBrush HeaderSelectedForeground { get; } = new SolidColorBrush(Color.FromRgb(20, 70, 43)).ToImmutable();

    public double ColumnHeaderHeight { get; } = 22d;

    public double RowHeaderWidth { get; } = 46d;

    /// <summary>Espaço entre o texto e a borda da célula, dos dois lados.</summary>
    public double CellPadding { get; } = 4d;

    public string FontFamily { get; } = "Calibri, Segoe UI, sans-serif";

    public double HeaderFontSize { get; } = 11d;
}
