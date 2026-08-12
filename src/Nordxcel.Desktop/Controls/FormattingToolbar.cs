using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nordxcel.Core.Editing;
using Nordxcel.Core.Formatting;

// O Avalonia tem HorizontalAlignment/VerticalAlignment próprios (layout de UI),
// diferentes dos de estilo de célula do Core.
using CoreHorizontalAlignment = Nordxcel.Core.Model.Styling.HorizontalAlignment;
using CoreVerticalAlignment = Nordxcel.Core.Model.Styling.VerticalAlignment;
using RgbColor = Nordxcel.Core.Model.Styling.RgbColor;
using CellStyle = Nordxcel.Core.Model.Styling.CellStyle;
using BorderLineStyle = Nordxcel.Core.Model.Styling.BorderLineStyle;

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// Barra de formatação: fonte, cor, borda, alinhamento e formato de número.
/// <para>
/// É uma view burra — só levanta eventos tipados por botão, sem tocar em
/// <c>Worksheet</c> nem <c>CalculationEngine</c> diretamente. Quem aplica a
/// mudança é o <see cref="SpreadsheetView"/>, através dos métodos que já sabem
/// operar sobre a seleção atual (o mesmo desenho que <see cref="FormulaBar"/> e
/// <see cref="SheetTabStrip"/> usam).
/// </para>
/// </summary>
public sealed class FormattingToolbar : UserControl
{
    private static readonly string ToolbarFont = "Calibri, Segoe UI, sans-serif";
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)).ToImmutable();
    private static readonly IBrush ActiveBackground = new SolidColorBrush(Color.FromRgb(217, 231, 223)).ToImmutable();
    private static readonly IBrush IdleBackground = Brushes.Transparent;
    private static readonly IBrush HoverBackground = new SolidColorBrush(Color.FromRgb(235, 235, 235)).ToImmutable();

    /// <summary>Paleta enxuta: preto/branco, as três cores do sistema automático e um punhado de cores comuns.</summary>
    private static readonly (string Label, RgbColor Color)[] Palette =
    [
        ("Preto", new RgbColor(0, 0, 0)),
        ("Branco", new RgbColor(255, 255, 255)),
        ("Azul (entrada)", CellColorClassifier.InputColor),
        ("Verde (link)", CellColorClassifier.LinkColor),
        ("Vermelho", new RgbColor(192, 0, 0)),
        ("Laranja", new RgbColor(237, 125, 49)),
        ("Amarelo", new RgbColor(255, 192, 0)),
        ("Verde-claro", new RgbColor(112, 173, 71)),
        ("Azul-claro", new RgbColor(68, 114, 196)),
        ("Roxo", new RgbColor(112, 48, 160)),
        ("Cinza-escuro", new RgbColor(64, 64, 64)),
        ("Cinza-claro", new RgbColor(217, 217, 217)),
    ];

    private readonly Button _boldButton;
    private readonly Button _italicButton;
    private readonly Button _underlineButton;
    private readonly Button _alignLeftButton;
    private readonly Button _alignCenterButton;
    private readonly Button _alignRightButton;
    private readonly Button _alignTopButton;
    private readonly Button _alignMiddleButton;
    private readonly Button _alignBottomButton;
    private readonly ComboBox _fontFamilyBox;
    private readonly ComboBox _fontSizeBox;

    private bool _updating;

    public FormattingToolbar()
    {
        _fontFamilyBox = CreateFontFamilyBox();
        _fontSizeBox = CreateFontSizeBox();

        _boldButton = CreateToggleLikeButton("N", "Negrito", bold: true);
        _italicButton = CreateToggleLikeButton("I", "Itálico", italic: true);
        _underlineButton = CreateToggleLikeButton("S", "Sublinhado", underline: true);

        _boldButton.Click += (_, _) => BoldToggleRequested?.Invoke(this, EventArgs.Empty);
        _italicButton.Click += (_, _) => ItalicToggleRequested?.Invoke(this, EventArgs.Empty);
        _underlineButton.Click += (_, _) => UnderlineToggleRequested?.Invoke(this, EventArgs.Empty);

        Button fontColorButton = CreateColorPickerButton(
            "A",
            "Cor da fonte",
            underlineColor: Color.FromRgb(192, 0, 0),
            includeAutomatic: true,
            color => FontColorSelected?.Invoke(this, color));

        Button fillColorButton = CreateColorPickerButton(
            "Fundo",
            "Cor de preenchimento",
            underlineColor: Color.FromRgb(255, 192, 0),
            includeAutomatic: true,
            color => FillColorSelected?.Invoke(this, color),
            automaticLabel: "Sem preenchimento");

        Button borderButton = CreateBorderButton();

        _alignLeftButton = CreateAlignButton("Alinhar à esquerda", CoreHorizontalAlignment.Left);
        _alignCenterButton = CreateAlignButton("Centralizar", CoreHorizontalAlignment.Center);
        _alignRightButton = CreateAlignButton("Alinhar à direita", CoreHorizontalAlignment.Right);

        _alignTopButton = CreateVerticalAlignButton("Alinhar em cima", CoreVerticalAlignment.Top);
        _alignMiddleButton = CreateVerticalAlignButton("Centralizar verticalmente", CoreVerticalAlignment.Center);
        _alignBottomButton = CreateVerticalAlignButton("Alinhar embaixo", CoreVerticalAlignment.Bottom);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 3, 6, 3) };

        content.Children.Add(_fontFamilyBox);
        content.Children.Add(_fontSizeBox);
        content.Children.Add(Divider());

        content.Children.Add(_boldButton);
        content.Children.Add(_italicButton);
        content.Children.Add(_underlineButton);
        content.Children.Add(Divider());

        content.Children.Add(fontColorButton);
        content.Children.Add(fillColorButton);
        content.Children.Add(borderButton);
        content.Children.Add(Divider());

        content.Children.Add(_alignLeftButton);
        content.Children.Add(_alignCenterButton);
        content.Children.Add(_alignRightButton);
        content.Children.Add(_alignTopButton);
        content.Children.Add(_alignMiddleButton);
        content.Children.Add(_alignBottomButton);
        content.Children.Add(Divider());

        content.Children.Add(CreateNumberFormatButton("Geral", "Formato geral, sem máscara", null));
        content.Children.Add(CreateNumberFormatButton("#.##0", "Milhar, com parênteses no negativo", StandardNumberFormats.Thousands));
        content.Children.Add(CreateCurrencyButton());
        content.Children.Add(CreateNumberFormatButton("%", "Porcentagem", StandardNumberFormats.Percent));
        content.Children.Add(CreateNumberFormatButton("0,0x", "Múltiplo (EV/EBITDA, P/L...)", StandardNumberFormats.Multiple));
        content.Children.Add(CreateNumberFormatButton("Data", "Data no padrão dd/mm/aaaa", StandardNumberFormats.ShortDate));
        content.Children.Add(CreateDecimalStepButton(",0→,00", "Aumentar casas decimais", increase: true));
        content.Children.Add(CreateDecimalStepButton(",00→,0", "Diminuir casas decimais", increase: false));

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)).ToImmutable(),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = DividerBrush,
            Child = scroller,
        };
    }

    public event EventHandler? BoldToggleRequested;

    public event EventHandler? ItalicToggleRequested;

    public event EventHandler? UnderlineToggleRequested;

    public event EventHandler<RgbColor?>? FontColorSelected;

    public event EventHandler<RgbColor?>? FillColorSelected;

    public event EventHandler<string>? FontFamilySelected;

    public event EventHandler<double>? FontSizeSelected;

    public event EventHandler<CoreHorizontalAlignment>? HorizontalAlignmentSelected;

    public event EventHandler<CoreVerticalAlignment>? VerticalAlignmentSelected;

    public event EventHandler<(BorderPreset Preset, BorderLineStyle Style, RgbColor Color)>? BorderPresetSelected;

    public event EventHandler<string?>? NumberFormatSelected;

    public event EventHandler<bool>? DecimalStepRequested;

    /// <summary>Atualiza os botões para refletir o estilo da célula ativa, sem disparar eventos.</summary>
    public void UpdateFromStyle(CellStyle style, string? numberFormat)
    {
        ArgumentNullException.ThrowIfNull(style);

        _updating = true;

        try
        {
            SetPressed(_boldButton, style.Bold);
            SetPressed(_italicButton, style.Italic);
            SetPressed(_underlineButton, style.Underline);

            SetPressed(_alignLeftButton, style.HorizontalAlignment is CoreHorizontalAlignment.Left);
            SetPressed(_alignCenterButton, style.HorizontalAlignment is CoreHorizontalAlignment.Center);
            SetPressed(_alignRightButton, style.HorizontalAlignment is CoreHorizontalAlignment.Right);

            SetPressed(_alignTopButton, style.VerticalAlignment is CoreVerticalAlignment.Top);
            SetPressed(_alignMiddleButton, style.VerticalAlignment is CoreVerticalAlignment.Center);
            SetPressed(_alignBottomButton, style.VerticalAlignment is CoreVerticalAlignment.Bottom);

            if (FindFontFamilyItem(style.FontFamily) is { } item)
            {
                _fontFamilyBox.SelectedItem = item;
            }

            if (FindFontSizeItem(style.FontSize) is { } sizeItem)
            {
                _fontSizeBox.SelectedItem = sizeItem;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    // ------------------------------------------------------------- fábricas

    private static Border Divider() => new()
    {
        Width = 1,
        Background = DividerBrush,
        Margin = new Thickness(3, 3, 3, 3),
    };

    private ComboBox CreateFontFamilyBox()
    {
        // O roadmap pede o mínimo de 2-3 fontes padrão; Calibri é o default do
        // Core (CellStyle.Default.FontFamily), então precisa estar na lista.
        var box = new ComboBox
        {
            Width = 120,
            FontFamily = new FontFamily(ToolbarFont),
            FontSize = 12,
            ItemsSource = new[] { "Calibri", "Arial", "Times New Roman", "Consolas" },
        };

        box.SelectionChanged += (_, _) =>
        {
            if (_updating || box.SelectedItem is not string family)
            {
                return;
            }

            FontFamilySelected?.Invoke(this, family);
        };

        return box;
    }

    private ComboBox CreateFontSizeBox()
    {
        var box = new ComboBox
        {
            Width = 56,
            FontFamily = new FontFamily(ToolbarFont),
            FontSize = 12,
            ItemsSource = new double[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36 },
        };

        box.SelectionChanged += (_, _) =>
        {
            if (_updating || box.SelectedItem is not double size)
            {
                return;
            }

            FontSizeSelected?.Invoke(this, size);
        };

        return box;
    }

    private object? FindFontFamilyItem(string family)
    {
        if (_fontFamilyBox.ItemsSource is not IEnumerable<string> items)
        {
            return null;
        }

        foreach (string item in items)
        {
            if (string.Equals(item, family, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private object? FindFontSizeItem(double size)
    {
        if (_fontSizeBox.ItemsSource is not IEnumerable<double> items)
        {
            return null;
        }

        foreach (double item in items)
        {
            if (Math.Abs(item - size) < 0.01)
            {
                return item;
            }
        }

        return null;
    }

    private Button CreateToggleLikeButton(string text, string tooltip, bool bold = false, bool italic = false, bool underline = false)
    {
        Button button = ToolbarButton(null, tooltip, width: 26);
        button.Content = new TextBlock
        {
            Text = text,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            TextDecorations = underline ? TextDecorations.Underline : null,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        return button;
    }

    private Button CreateAlignButton(string tooltip, CoreHorizontalAlignment alignment)
    {
        Button button = ToolbarButton(AlignmentAbbreviation(alignment), tooltip, width: 34);
        button.Click += (_, _) => HorizontalAlignmentSelected?.Invoke(this, alignment);
        return button;
    }

    private Button CreateVerticalAlignButton(string tooltip, CoreVerticalAlignment alignment)
    {
        Button button = ToolbarButton(VerticalAbbreviation(alignment), tooltip, width: 34);
        button.Click += (_, _) => VerticalAlignmentSelected?.Invoke(this, alignment);
        return button;
    }

    private static string AlignmentAbbreviation(CoreHorizontalAlignment alignment) => alignment switch
    {
        CoreHorizontalAlignment.Left => "Esq",
        CoreHorizontalAlignment.Center => "Ctr",
        CoreHorizontalAlignment.Right => "Dir",
        _ => "—",
    };

    private static string VerticalAbbreviation(CoreVerticalAlignment alignment) => alignment switch
    {
        CoreVerticalAlignment.Top => "Top",
        CoreVerticalAlignment.Center => "Mid",
        CoreVerticalAlignment.Bottom => "Bot",
        _ => "—",
    };

    private Button CreateColorPickerButton(
        string label,
        string tooltip,
        Color underlineColor,
        bool includeAutomatic,
        Action<RgbColor?> onSelected,
        string automaticLabel = "Automático")
    {
        var labelBlock = new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
        var stripe = new Border { Height = 3, Background = new SolidColorBrush(underlineColor).ToImmutable(), Margin = new Thickness(2, 0, 2, 0) };

        var stack = new StackPanel { Spacing = 1 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(stripe);

        Button button = ToolbarButton(null, tooltip, width: label.Length > 1 ? 46 : 28);
        button.Content = stack;

        var flyout = new Flyout();
        var panel = new StackPanel { Margin = new Thickness(8), Spacing = 6, Width = 190 };

        if (includeAutomatic)
        {
            Button autoButton = ToolbarMenuRow(automaticLabel);
            autoButton.Click += (_, _) =>
            {
                onSelected(null);
                flyout.Hide();
            };
            panel.Children.Add(autoButton);
        }

        var grid = new WrapPanel { Orientation = Orientation.Horizontal };

        foreach ((string swatchLabel, RgbColor color) in Palette)
        {
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)).ToImmutable(),
                BorderBrush = DividerBrush,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };

            ToolTip.SetTip(swatch, swatchLabel);

            swatch.PointerPressed += (_, _) =>
            {
                onSelected(color);
                flyout.Hide();
            };

            grid.Children.Add(swatch);
        }

        panel.Children.Add(grid);

        var hexBox = new TextBox { PlaceholderText = "#RRGGBB", FontSize = 11, Width = 100 };
        var applyHex = ToolbarButton("Aplicar", "Usar essa cor", width: 60);

        applyHex.Click += (_, _) =>
        {
            if (RgbColor.TryParse(hexBox.Text ?? string.Empty, out RgbColor custom))
            {
                onSelected(custom);
                flyout.Hide();
            }
        };

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        hexRow.Children.Add(hexBox);
        hexRow.Children.Add(applyHex);
        panel.Children.Add(hexRow);

        flyout.Content = panel;
        button.Flyout = flyout;

        return button;
    }

    private Button CreateBorderButton()
    {
        Button button = ToolbarButton("Bordas", "Bordas", width: 48);

        var flyout = new Flyout();
        var panel = new StackPanel { Margin = new Thickness(4), Spacing = 1, Width = 200 };

        (string Label, BorderPreset Preset, BorderLineStyle Style)[] presets =
        [
            ("Todas as Bordas", BorderPreset.All, BorderLineStyle.Thin),
            ("Contorno Externo", BorderPreset.Outline, BorderLineStyle.Thin),
            ("Borda Superior", BorderPreset.Top, BorderLineStyle.Thin),
            ("Borda Inferior", BorderPreset.Bottom, BorderLineStyle.Thin),
            ("Borda Esquerda", BorderPreset.Left, BorderLineStyle.Thin),
            ("Borda Direita", BorderPreset.Right, BorderLineStyle.Thin),
            ("Borda Inferior Grossa", BorderPreset.Bottom, BorderLineStyle.Thick),
            ("Linha Dupla — Total Geral", BorderPreset.Bottom, BorderLineStyle.Double),
            ("Sem Borda", BorderPreset.None, BorderLineStyle.None),
        ];

        foreach ((string label, BorderPreset preset, BorderLineStyle style) in presets)
        {
            Button row = ToolbarMenuRow(label);

            row.Click += (_, _) =>
            {
                BorderPresetSelected?.Invoke(this, (preset, style, RgbColor.Black));
                flyout.Hide();
            };

            panel.Children.Add(row);
        }

        flyout.Content = panel;
        button.Flyout = flyout;

        return button;
    }

    private Button CreateNumberFormatButton(string text, string tooltip, string? mask)
    {
        Button button = ToolbarButton(text, tooltip, width: 46);
        button.Click += (_, _) => NumberFormatSelected?.Invoke(this, mask);
        return button;
    }

    private Button CreateCurrencyButton()
    {
        Button button = ToolbarButton("R$", "Moeda (clique para escolher o símbolo)", width: 46);

        var flyout = new Flyout();
        var panel = new StackPanel { Margin = new Thickness(4), Spacing = 1, Width = 160 };

        (string Label, string Mask)[] options =
        [
            ("Real (R$)", StandardNumberFormats.CurrencyReal),
            ("Dólar (US$)", StandardNumberFormats.CurrencyDollar),
            ("Euro (€)", StandardNumberFormats.Currency("€")),
        ];

        foreach ((string label, string mask) in options)
        {
            Button row = ToolbarMenuRow(label);
            row.Click += (_, _) =>
            {
                NumberFormatSelected?.Invoke(this, mask);
                flyout.Hide();
            };
            panel.Children.Add(row);
        }

        var customPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(4, 4, 4, 0) };
        var symbolBox = new TextBox { PlaceholderText = "Símbolo", FontSize = 11, Width = 70 };
        var applyCustom = ToolbarButton("OK", "Usar símbolo customizado", width: 36);

        applyCustom.Click += (_, _) =>
        {
            string symbol = (symbolBox.Text ?? string.Empty).Trim();

            if (symbol.Length == 0)
            {
                return;
            }

            NumberFormatSelected?.Invoke(this, StandardNumberFormats.Currency(symbol));
            flyout.Hide();
        };

        customPanel.Children.Add(symbolBox);
        customPanel.Children.Add(applyCustom);
        panel.Children.Add(customPanel);

        flyout.Content = panel;
        button.Flyout = flyout;

        return button;
    }

    private Button CreateDecimalStepButton(string text, string tooltip, bool increase)
    {
        Button button = ToolbarButton(text, tooltip, width: 52);
        button.FontSize = 10;
        button.Click += (_, _) => DecimalStepRequested?.Invoke(this, increase);
        return button;
    }

    private static Button ToolbarButton(string? text, string tooltip, double width)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 24,
            Padding = new Thickness(2, 0, 2, 0),
            Background = IdleBackground,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily(ToolbarFont),
            FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        ToolTip.SetTip(button, tooltip);

        button.PointerEntered += (_, _) =>
        {
            if (!IsPressed(button))
            {
                button.Background = HoverBackground;
            }
        };

        button.PointerExited += (_, _) =>
        {
            if (!IsPressed(button))
            {
                button.Background = IdleBackground;
            }
        };

        return button;
    }

    private static Button ToolbarMenuRow(string text)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = IdleBackground,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily(ToolbarFont),
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        button.PointerEntered += (_, _) => button.Background = HoverBackground;
        button.PointerExited += (_, _) => button.Background = IdleBackground;

        return button;
    }

    /// <summary>Marcador simples de "pressionado", guardado na propriedade Tag para não precisar de campo por botão.</summary>
    private static bool IsPressed(Button button) => button.Tag is true;

    private static void SetPressed(Button button, bool pressed)
    {
        button.Tag = pressed;
        button.Background = pressed ? ActiveBackground : IdleBackground;
    }
}
