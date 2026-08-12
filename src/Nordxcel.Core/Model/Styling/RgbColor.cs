using System.Globalization;

namespace Nordxcel.Core.Model.Styling;

/// <summary>
/// Cor RGB de 8 bits por canal. O Core não referencia nenhuma biblioteca de UI,
/// então a conversão para o tipo de cor do Avalonia acontece na camada de apresentação.
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Black => new(0, 0, 0);

    public static RgbColor White => new(255, 255, 255);

    /// <summary>Interpreta <c>#RRGGBB</c> ou <c>RRGGBB</c>.</summary>
    public static RgbColor Parse(string hex) =>
        TryParse(hex, out RgbColor color)
            ? color
            : throw new FormatException($"'{hex}' não é uma cor hexadecimal válida.");

    /// <inheritdoc cref="Parse"/>
    public static bool TryParse(ReadOnlySpan<char> hex, out RgbColor color)
    {
        color = default;

        if (hex.Length > 0 && hex[0] == '#')
        {
            hex = hex[1..];
        }

        if (hex.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
            !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
            !byte.TryParse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        color = new RgbColor(r, g, b);
        return true;
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
