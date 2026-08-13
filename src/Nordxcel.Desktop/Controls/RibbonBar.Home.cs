using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nordxcel.Core.Editing;
using Nordxcel.Core.Formatting;

using RgbColor = Nordxcel.Core.Model.Styling.RgbColor;
using BorderLineStyle = Nordxcel.Core.Model.Styling.BorderLineStyle;
using CoreHorizontalAlignment = Nordxcel.Core.Model.Styling.HorizontalAlignment;
using CoreVerticalAlignment = Nordxcel.Core.Model.Styling.VerticalAlignment;
using CellStyle = Nordxcel.Core.Model.Styling.CellStyle;

namespace Nordxcel.Desktop.Controls;

/// <summary>Aba Página Inicial: área de transferência, fonte, alinhamento e número — adaptado do antigo <c>FormattingToolbar</c>.</summary>
public sealed partial class RibbonBar
{
    private Button _boldButton = null!;
    private Button _italicButton = null!;
    private Button _underlineButton = null!;
    private Button _alignLeftButton = null!;
    private Button _alignCenterButton = null!;
    private Button _alignRightButton = null!;
    private Button _alignTopButton = null!;
    private Button _alignMiddleButton = null!;
    private Button _alignBottomButton = null!;
    private ComboBox _fontFamilyBox = null!;
    private ComboBox _fontSizeBox = null!;

    private Control BuildHomePage()
    {
        Control clipboardGroup = RibbonGroup("Área de Transferência", BuildClipboardGroupContent());
        Control fontGroup = RibbonGroup("Fonte", BuildFontGroupContent());
        Control alignmentGroup = RibbonGroup("Alinhamento", BuildAlignmentGroupContent());
        Control numberGroup = RibbonGroup("Número", BuildNumberGroupContent());

        return PageContent(clipboardGroup, fontGroup, alignmentGroup, numberGroup);
    }

    private void UpdateHomeFromStyle(CellStyle style, string? numberFormat)
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

    // ------------------------------------------------- área de transferência

    private Control BuildClipboardGroupContent()
    {
        Button paste = CreatePasteSplitButton();

        Button cut = ClipboardRowButton("Recortar", "Recortar (Ctrl+X)");
        cut.Click += (_, _) => CutRequested?.Invoke(this, EventArgs.Empty);

        Button copy = ClipboardRowButton("Copiar", "Copiar (Ctrl+C)");
        copy.Click += (_, _) => CopyRequested?.Invoke(this, EventArgs.Empty);

        var rightColumn = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        rightColumn.Children.Add(cut);
        rightColumn.Children.Add(copy);
        rightColumn.Children.Add(_formatPainterButton);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(paste);
        row.Children.Add(rightColumn);

        return row;
    }

    /// <summary>Botão estreito de uma linha só, do tamanho certo para empilhar três (Recortar/Copiar/Pincel) ao lado do Colar.</summary>
    private static Button ClipboardRowButton(string text, string tooltip)
    {
        var button = new Button
        {
            Content = text,
            Width = 80,
            Height = 15,
            Padding = new Thickness(4, 0, 2, 0),
            Background = IdleBackground,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily(RibbonFont),
            FontSize = 10,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        ToolTip.SetTip(button, tooltip);

        button.PointerEntered += (_, _) => { if (!IsPressed(button)) { button.Background = HoverBackground; } };
        button.PointerExited += (_, _) => { if (!IsPressed(button)) { button.Background = IdleBackground; } };

        return button;
    }

    private Button CreateFormatPainterButton()
    {
        Button button = ClipboardRowButton("Pincel", "Pincel de Formatação — copia a formatação da célula ativa: clique aqui, depois clique (ou arraste) no destino");
        button.Click += (_, _) => FormatPainterRequested?.Invoke(this, EventArgs.Empty);
        return button;
    }

    private Button CreatePasteSplitButton()
    {
        var mainArea = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
        mainArea.Children.Add(new TextBlock { Text = "Colar", FontFamily = new FontFamily(RibbonFont), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
        mainArea.Children.Add(new TextBlock { Text = "▾", FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center });

        var button = new Button
        {
            Content = mainArea,
            Width = 52,
            Height = 44,
            Background = IdleBackground,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        ToolTip.SetTip(button, "Colar (Ctrl+V) — clique para mais opções");

        button.PointerEntered += (_, _) => button.Background = HoverBackground;
        button.PointerExited += (_, _) => button.Background = IdleBackground;

        var flyout = new Flyout();
        var panel = new StackPanel { Margin = new Thickness(4), Spacing = 1, Width = 170 };

        Button pasteRow = ToolbarMenuRow("Colar");
        pasteRow.Click += (_, _) => { PasteRequested?.Invoke(this, EventArgs.Empty); flyout.Hide(); };

        Button valuesRow = ToolbarMenuRow("Colar Valores");
        valuesRow.Click += (_, _) => { PasteValuesRequested?.Invoke(this, EventArgs.Empty); flyout.Hide(); };

        Button formatRow = ToolbarMenuRow("Colar Formatação");
        formatRow.Click += (_, _) => { PasteFormatRequested?.Invoke(this, EventArgs.Empty); flyout.Hide(); };

        panel.Children.Add(pasteRow);
        panel.Children.Add(valuesRow);
        panel.Children.Add(formatRow);

        flyout.Content = panel;
        button.Flyout = flyout;

        // Clicar no corpo do botão (fora da abertura do flyout) cola normal — o
        // Flyout já cobre a função de "mais opções" ao segurar/clicar de novo,
        // então não precisa de uma área separada para a seta.
        button.Click += (_, _) => PasteRequested?.Invoke(this, EventArgs.Empty);

        return button;
    }

    // ------------------------------------------------------------- fonte

    private Control BuildFontGroupContent()
    {
        _fontFamilyBox = CreateFontFamilyBox();
        _fontSizeBox = CreateFontSizeBox();

        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        topRow.Children.Add(_fontFamilyBox);
        topRow.Children.Add(_fontSizeBox);

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

        var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        bottomRow.Children.Add(_boldButton);
        bottomRow.Children.Add(_italicButton);
        bottomRow.Children.Add(_underlineButton);
        bottomRow.Children.Add(fontColorButton);
        bottomRow.Children.Add(fillColorButton);
        bottomRow.Children.Add(borderButton);

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(topRow);
        stack.Children.Add(bottomRow);

        return stack;
    }

    private ComboBox CreateFontFamilyBox()
    {
        var box = new ComboBox
        {
            Width = 120,
            FontFamily = new FontFamily(RibbonFont),
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
            FontFamily = new FontFamily(RibbonFont),
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

    // -------------------------------------------------------- alinhamento

    private Control BuildAlignmentGroupContent()
    {
        _alignLeftButton = CreateAlignButton("Alinhar à esquerda", CoreHorizontalAlignment.Left);
        _alignCenterButton = CreateAlignButton("Centralizar", CoreHorizontalAlignment.Center);
        _alignRightButton = CreateAlignButton("Alinhar à direita", CoreHorizontalAlignment.Right);

        _alignTopButton = CreateVerticalAlignButton("Alinhar em cima", CoreVerticalAlignment.Top);
        _alignMiddleButton = CreateVerticalAlignButton("Centralizar verticalmente", CoreVerticalAlignment.Center);
        _alignBottomButton = CreateVerticalAlignButton("Alinhar embaixo", CoreVerticalAlignment.Bottom);

        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        topRow.Children.Add(_alignTopButton);
        topRow.Children.Add(_alignMiddleButton);
        topRow.Children.Add(_alignBottomButton);

        var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        bottomRow.Children.Add(_alignLeftButton);
        bottomRow.Children.Add(_alignCenterButton);
        bottomRow.Children.Add(_alignRightButton);

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(topRow);
        stack.Children.Add(bottomRow);

        return stack;
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

    // ------------------------------------------------------------- número

    private Control BuildNumberGroupContent()
    {
        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        topRow.Children.Add(CreateNumberFormatButton("Geral", "Formato geral, sem máscara", null));
        topRow.Children.Add(CreateNumberFormatButton("#.##0", "Milhar, com parênteses no negativo", StandardNumberFormats.Thousands));
        topRow.Children.Add(CreateCurrencyButton());
        topRow.Children.Add(CreateNumberFormatButton("%", "Porcentagem", StandardNumberFormats.Percent));

        var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        bottomRow.Children.Add(CreateNumberFormatButton("0,0x", "Múltiplo (EV/EBITDA, P/L...)", StandardNumberFormats.Multiple));
        bottomRow.Children.Add(CreateNumberFormatButton("Data", "Data no padrão dd/mm/aaaa", StandardNumberFormats.ShortDate));
        bottomRow.Children.Add(CreateDecimalStepButton(",0→,00", "Aumentar casas decimais", increase: true));
        bottomRow.Children.Add(CreateDecimalStepButton(",00→,0", "Diminuir casas decimais", increase: false));

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(topRow);
        stack.Children.Add(bottomRow);

        return stack;
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
}
