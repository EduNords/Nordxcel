using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media;

namespace Nordxcel.Desktop.Rendering;

/// <summary>
/// Reaproveita os objetos de texto já medidos. Montar um <see cref="FormattedText"/>
/// custa layout de fonte, e a grade redesenha centenas de células a cada rolagem —
/// sem cache, isso vira o gargalo do quadro.
/// </summary>
public sealed class TextCache
{
    /// <summary>Passando disso o cache é esvaziado, para não crescer sem limite.</summary>
    private const int Capacity = 4096;

    private readonly Dictionary<Key, FormattedText> _entries = [];
    private readonly Dictionary<FontKey, Typeface> _typefaces = [];

    public FormattedText Get(string text, string fontFamily, double fontSize, bool bold, bool italic, Color color)
    {
        var key = new Key(text, fontFamily, fontSize, bold, italic, color);

        if (_entries.TryGetValue(key, out FormattedText? cached))
        {
            return cached;
        }

        if (_entries.Count >= Capacity)
        {
            _entries.Clear();
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            GetTypeface(fontFamily, bold, italic),
            fontSize,
            new SolidColorBrush(color).ToImmutable());

        _entries[key] = formatted;

        return formatted;
    }

    private Typeface GetTypeface(string fontFamily, bool bold, bool italic)
    {
        var key = new FontKey(fontFamily, bold, italic);

        if (_typefaces.TryGetValue(key, out Typeface cached))
        {
            return cached;
        }

        var typeface = new Typeface(
            new FontFamily(fontFamily),
            italic ? FontStyle.Italic : FontStyle.Normal,
            bold ? FontWeight.Bold : FontWeight.Normal);

        _typefaces[key] = typeface;

        return typeface;
    }

    public void Clear()
    {
        _entries.Clear();
        _typefaces.Clear();
    }

    private readonly record struct Key(
        string Text,
        string FontFamily,
        double FontSize,
        bool Bold,
        bool Italic,
        Color Color);

    private readonly record struct FontKey(string FontFamily, bool Bold, bool Italic);
}
