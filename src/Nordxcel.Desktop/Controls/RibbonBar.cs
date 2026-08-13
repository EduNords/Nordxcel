using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nordxcel.Core.Editing;

using RgbColor = Nordxcel.Core.Model.Styling.RgbColor;
using BorderLineStyle = Nordxcel.Core.Model.Styling.BorderLineStyle;
using CoreHorizontalAlignment = Nordxcel.Core.Model.Styling.HorizontalAlignment;
using CoreVerticalAlignment = Nordxcel.Core.Model.Styling.VerticalAlignment;
using CellStyle = Nordxcel.Core.Model.Styling.CellStyle;

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// Faixa de opções (ribbon) no estilo do Excel: barra de acesso rápido, tira de
/// abas e o conteúdo agrupado da aba ativa. Substitui o menu clássico e a barra
/// de formatação de linha única.
/// <para>
/// É uma view burra, como <see cref="FormattingToolbar"/> era — só levanta
/// eventos tipados, sem tocar em <c>Worksheet</c>/<c>CalculationEngine</c>. As
/// abas Inserir, Layout da Página, Revisão e Ajuda existem só visualmente por
/// enquanto: o Nordxcel ainda não tem nada para colocar nelas (sem inserir
/// linha/coluna, sem impressão, sem comentário/proteção, sem central de ajuda).
/// Aparecem em branco em vez de ficarem de fora — decisão do próprio usuário, já
/// que o objetivo é parecer com o Excel de verdade desde já.
/// </para>
/// </summary>
public sealed partial class RibbonBar : UserControl
{
    private enum RibbonTab
    {
        Home,
        Insert,
        PageLayout,
        Formulas,
        Data,
        Review,
        View,
        Help,
    }

    private static readonly string RibbonFont = "Calibri, Segoe UI, sans-serif";
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)).ToImmutable();
    private static readonly IBrush RibbonBackground = new SolidColorBrush(Color.FromRgb(250, 250, 250)).ToImmutable();
    private static readonly IBrush TabStripBackground = new SolidColorBrush(Colors.White).ToImmutable();
    private static readonly IBrush QatBackground = new SolidColorBrush(Color.FromRgb(243, 243, 243)).ToImmutable();
    private static readonly IBrush ActiveIndicator = new SolidColorBrush(Color.FromRgb(33, 115, 70)).ToImmutable();
    private static readonly IBrush ActiveBackground = new SolidColorBrush(Color.FromRgb(217, 231, 223)).ToImmutable();
    private static readonly IBrush IdleBackground = Brushes.Transparent;
    private static readonly IBrush HoverBackground = new SolidColorBrush(Color.FromRgb(235, 235, 235)).ToImmutable();
    private static readonly IBrush GroupCaptionBrush = new SolidColorBrush(Color.FromRgb(110, 110, 110)).ToImmutable();
    private static readonly IBrush FileTabBackground = new SolidColorBrush(Color.FromRgb(33, 115, 70)).ToImmutable();

    /// <summary>Paleta enxuta: preto/branco, as três cores do sistema automático e um punhado de cores comuns.</summary>
    private static readonly (string Label, RgbColor Color)[] Palette =
    [
        ("Preto", new RgbColor(0, 0, 0)),
        ("Branco", new RgbColor(255, 255, 255)),
        ("Azul (entrada)", Nordxcel.Core.Formatting.CellColorClassifier.InputColor),
        ("Verde (link)", Nordxcel.Core.Formatting.CellColorClassifier.LinkColor),
        ("Vermelho", new RgbColor(192, 0, 0)),
        ("Laranja", new RgbColor(237, 125, 49)),
        ("Amarelo", new RgbColor(255, 192, 0)),
        ("Verde-claro", new RgbColor(112, 173, 71)),
        ("Azul-claro", new RgbColor(68, 114, 196)),
        ("Roxo", new RgbColor(112, 48, 160)),
        ("Cinza-escuro", new RgbColor(64, 64, 64)),
        ("Cinza-claro", new RgbColor(217, 217, 217)),
    ];

    private readonly Dictionary<RibbonTab, Button> _tabButtons = new();
    private readonly Dictionary<RibbonTab, Control> _tabPages = new();
    private readonly ContentControl _pageHost = new();
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly Button _formatPainterButton;

    private RibbonTab _activeTab = RibbonTab.Home;
    private bool _updating;

    public RibbonBar()
    {
        var qat = BuildQuickAccessToolbar(out _undoButton, out _redoButton);
        var tabStrip = BuildTabStrip();

        // Precisa existir antes de construir a página Início, que coloca este
        // botão dentro do grupo Área de Transferência.
        _formatPainterButton = CreateFormatPainterButton();

        foreach (RibbonTab tab in Enum.GetValues<RibbonTab>())
        {
            _tabPages[tab] = BuildPage(tab);
        }

        var layout = new StackPanel();
        layout.Children.Add(qat);
        layout.Children.Add(tabStrip);
        layout.Children.Add(new Border
        {
            Background = RibbonBackground,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = DividerBrush,
            Child = _pageHost,
        });

        Content = layout;

        // Sincroniza o conteúdo E o destaque visual da aba inicial — atribuir só
        // o conteúdo (sem passar por SelectTab) deixava a página certa visível
        // mas nenhuma aba parecendo selecionada na tira.
        SelectTab(_activeTab);
    }

    // ---------------------------------------------------------------- eventos

    // Formatação (mesma convenção que FormattingToolbar já usava)
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

    // Área de transferência
    public event EventHandler? CutRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? PasteValuesRequested;
    public event EventHandler? PasteFormatRequested;
    public event EventHandler? FormatPainterRequested;

    // Barra de acesso rápido
    public event EventHandler? SaveRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;

    // Backstage "Arquivo"
    public event EventHandler? NewRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? SaveAsRequested;
    public event EventHandler? ExportXlsxRequested;

    // Aba Exibir
    public event EventHandler? FreezePanesRequested;
    public event EventHandler? FreezeTopRowRequested;
    public event EventHandler? FreezeFirstColumnRequested;
    public event EventHandler? UnfreezePanesRequested;

    // Aba Dados
    public event EventHandler? DataTableOneVariableRequested;
    public event EventHandler? DataTableTwoVariableRequested;

    // Aba Fórmulas
    public event EventHandler<string>? FunctionInsertRequested;

    // --------------------------------------------------------------- estado

    /// <summary>Atualiza os botões para refletir o estilo da célula ativa, sem disparar eventos.</summary>
    public void UpdateFromStyle(CellStyle style, string? numberFormat)
    {
        ArgumentNullException.ThrowIfNull(style);

        _updating = true;

        try
        {
            UpdateHomeFromStyle(style, numberFormat);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Habilita/desabilita e atualiza a dica dos botões de desfazer/refazer da barra de acesso rápido.</summary>
    public void UpdateUndoRedo(bool canUndo, string? undoDescription, bool canRedo, string? redoDescription)
    {
        _undoButton.IsEnabled = canUndo;
        _redoButton.IsEnabled = canRedo;

        ToolTip.SetTip(_undoButton, undoDescription is { } undo ? $"Desfazer {undo}" : "Desfazer");
        ToolTip.SetTip(_redoButton, redoDescription is { } redo ? $"Refazer {redo}" : "Refazer");
    }

    /// <summary>Reflete se o pincel de formatação está armado, esperando o próximo clique na grade.</summary>
    public void UpdateFormatPainterState(bool pending) => SetPressed(_formatPainterButton, pending);

    // ------------------------------------------------------- barra de acesso rápido

    private Control BuildQuickAccessToolbar(out Button undo, out Button redo)
    {
        Button save = IconButton("💾", "Salvar (Ctrl+S)");
        save.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);

        undo = IconButton("↶", "Desfazer (Ctrl+Z)");
        undo.IsEnabled = false;
        undo.Click += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);

        redo = IconButton("↷", "Refazer (Ctrl+Y)");
        redo.IsEnabled = false;
        redo.Click += (_, _) => RedoRequested?.Invoke(this, EventArgs.Empty);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 3, 6, 3) };
        content.Children.Add(save);
        content.Children.Add(undo);
        content.Children.Add(redo);

        return new Border { Background = QatBackground, Child = content };
    }

    private static Button IconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 26,
            Height = 22,
            Padding = new Thickness(0),
            Background = IdleBackground,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        ToolTip.SetTip(button, tooltip);

        button.PointerEntered += (_, _) => { if (button.IsEnabled) { button.Background = HoverBackground; } };
        button.PointerExited += (_, _) => button.Background = IdleBackground;

        return button;
    }

    // ---------------------------------------------------------------- abas

    private Control BuildTabStrip()
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };

        strip.Children.Add(CreateFileTabButton());

        strip.Children.Add(CreateTabButton(RibbonTab.Home, "Página Inicial"));
        strip.Children.Add(CreateTabButton(RibbonTab.Insert, "Inserir"));
        strip.Children.Add(CreateTabButton(RibbonTab.PageLayout, "Layout da Página"));
        strip.Children.Add(CreateTabButton(RibbonTab.Formulas, "Fórmulas"));
        strip.Children.Add(CreateTabButton(RibbonTab.Data, "Dados"));
        strip.Children.Add(CreateTabButton(RibbonTab.Review, "Revisão"));
        strip.Children.Add(CreateTabButton(RibbonTab.View, "Exibir"));
        strip.Children.Add(CreateTabButton(RibbonTab.Help, "Ajuda"));

        return new Border
        {
            Background = TabStripBackground,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = DividerBrush,
            Padding = new Thickness(4, 0, 0, 0),
            Child = strip,
        };
    }

    private Button CreateTabButton(RibbonTab tab, string label)
    {
        var indicator = new Border { Height = 3, Background = Brushes.Transparent, Margin = new Thickness(2, 0, 2, 0) };

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily(RibbonFont),
            FontSize = 12,
            Margin = new Thickness(10, 6, 10, 4),
        });
        stack.Children.Add(indicator);

        var button = new Button
        {
            Content = stack,
            Background = IdleBackground,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = indicator,
        };

        button.Click += (_, _) => SelectTab(tab);
        button.PointerEntered += (_, _) => { if (_activeTab != tab) { button.Background = HoverBackground; } };
        button.PointerExited += (_, _) => { if (_activeTab != tab) { button.Background = IdleBackground; } };

        _tabButtons[tab] = button;
        return button;
    }

    private void SelectTab(RibbonTab tab)
    {
        _activeTab = tab;
        _pageHost.Content = _tabPages[tab];

        foreach ((RibbonTab candidate, Button button) in _tabButtons)
        {
            bool active = candidate == tab;
            button.Background = active ? ActiveBackground : IdleBackground;

            if (button.Tag is Border indicator)
            {
                indicator.Background = active ? ActiveIndicator : Brushes.Transparent;
            }
        }
    }

    private Control BuildPage(RibbonTab tab) => tab switch
    {
        RibbonTab.Home => BuildHomePage(),
        RibbonTab.Formulas => BuildFormulasPage(),
        RibbonTab.Data => BuildDataPage(),
        RibbonTab.View => BuildViewPage(),
        _ => new Border { Height = 74 },
    };

    private Control BuildDataPage()
    {
        Button oneVariable = RibbonButton("Tabela de Dados\n(1 Variável)", "Análise de sensibilidade com uma variável de entrada", width: 100, height: 44);
        oneVariable.Click += (_, _) => DataTableOneVariableRequested?.Invoke(this, EventArgs.Empty);

        Button twoVariable = RibbonButton("Tabela de Dados\n(2 Variáveis)", "Análise de sensibilidade com duas variáveis de entrada", width: 100, height: 44);
        twoVariable.Click += (_, _) => DataTableTwoVariableRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(oneVariable);
        row.Children.Add(twoVariable);

        return PageContent(RibbonGroup("Ferramentas de Dados", row));
    }

    private Control BuildViewPage()
    {
        Button freeze = RibbonButton("Congelar\nPainéis", "Congela as linhas acima e as colunas à esquerda da seleção", width: 90, height: 44);
        freeze.Click += (_, _) => FreezePanesRequested?.Invoke(this, EventArgs.Empty);

        Button freezeTop = RibbonButton("Congelar Linha\nSuperior", "Congela só a primeira linha", width: 90, height: 44);
        freezeTop.Click += (_, _) => FreezeTopRowRequested?.Invoke(this, EventArgs.Empty);

        Button freezeFirst = RibbonButton("Congelar 1ª\nColuna", "Congela só a primeira coluna", width: 90, height: 44);
        freezeFirst.Click += (_, _) => FreezeFirstColumnRequested?.Invoke(this, EventArgs.Empty);

        Button unfreeze = RibbonButton("Descongelar\nPainéis", "Remove os painéis congelados", width: 90, height: 44);
        unfreeze.Click += (_, _) => UnfreezePanesRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(freeze);
        row.Children.Add(freezeTop);
        row.Children.Add(freezeFirst);
        row.Children.Add(unfreeze);

        return PageContent(RibbonGroup("Janela", row));
    }

    private static Control PageContent(params Control[] groups)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(6, 2, 6, 2) };

        for (int i = 0; i < groups.Length; i++)
        {
            if (i > 0)
            {
                row.Children.Add(GroupDivider());
            }

            row.Children.Add(groups[i]);
        }

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = row,
        };

        return scroller;
    }

    // ------------------------------------------------------------ backstage

    private Button CreateFileTabButton()
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = "Arquivo",
                Foreground = Brushes.White,
                FontFamily = new FontFamily(RibbonFont),
                FontSize = 12,
                Margin = new Thickness(12, 6, 12, 7),
            },
            Background = FileTabBackground,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var panel = new StackPanel { Margin = new Thickness(4), Spacing = 1, Width = 200 };

        (string Label, EventHandler? Invoke)[] items =
        [
            ("Novo", (_, _) => NewRequested?.Invoke(this, EventArgs.Empty)),
            ("Abrir...", (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty)),
            ("Salvar", (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty)),
            ("Salvar Como...", (_, _) => SaveAsRequested?.Invoke(this, EventArgs.Empty)),
        ];

        foreach ((string label, EventHandler? handler) in items)
        {
            Button row = ToolbarMenuRow(label);
            row.Click += (s, e) => { handler?.Invoke(s, e); flyout.Hide(); };
            panel.Children.Add(row);
        }

        panel.Children.Add(new Border { Height = 1, Background = DividerBrush, Margin = new Thickness(4, 4, 4, 4) });

        Button export = ToolbarMenuRow("Exportar para .xlsx...");
        export.Click += (_, _) => { ExportXlsxRequested?.Invoke(this, EventArgs.Empty); flyout.Hide(); };
        panel.Children.Add(export);

        flyout.Content = panel;
        button.Flyout = flyout;

        return button;
    }

    // -------------------------------------------------------------- fábricas

    private static Border GroupDivider() => new()
    {
        Width = 1,
        Background = DividerBrush,
        Margin = new Thickness(3, 6, 3, 6),
    };

    /// <summary>Um grupo do ribbon: conteúdo em cima, legenda centralizada embaixo — igual ao Excel.</summary>
    private static Border RibbonGroup(string caption, Control content)
    {
        var stack = new DockPanel();

        var captionBlock = new TextBlock
        {
            Text = caption,
            FontFamily = new FontFamily(RibbonFont),
            FontSize = 10,
            Foreground = GroupCaptionBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        };

        DockPanel.SetDock(captionBlock, Dock.Bottom);
        stack.Children.Add(captionBlock);
        stack.Children.Add(content);

        return new Border { Padding = new Thickness(6, 4, 6, 2), Child = stack };
    }

    private static Button RibbonButton(string text, string tooltip, double width, double height)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = text,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontFamily = new FontFamily(RibbonFont),
                FontSize = 11,
            },
            Width = width,
            Height = height,
            Padding = new Thickness(4, 2, 4, 2),
            Background = IdleBackground,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        ToolTip.SetTip(button, tooltip);

        button.PointerEntered += (_, _) => { if (!IsPressed(button)) { button.Background = HoverBackground; } };
        button.PointerExited += (_, _) => { if (!IsPressed(button)) { button.Background = IdleBackground; } };

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
            FontFamily = new FontFamily(RibbonFont),
            FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        ToolTip.SetTip(button, tooltip);

        button.PointerEntered += (_, _) => { if (!IsPressed(button)) { button.Background = HoverBackground; } };
        button.PointerExited += (_, _) => { if (!IsPressed(button)) { button.Background = IdleBackground; } };

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
            FontFamily = new FontFamily(RibbonFont),
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        button.PointerEntered += (_, _) => button.Background = HoverBackground;
        button.PointerExited += (_, _) => button.Background = IdleBackground;

        return button;
    }

    private static Border Divider() => new()
    {
        Width = 1,
        Background = DividerBrush,
        Margin = new Thickness(3, 3, 3, 3),
    };

    /// <summary>Marcador simples de "pressionado", guardado na propriedade Tag para não precisar de campo por botão.</summary>
    private static bool IsPressed(Button button) => button.Tag is true;

    private static void SetPressed(Button button, bool pressed)
    {
        button.Tag = pressed;
        button.Background = pressed ? ActiveBackground : IdleBackground;
    }
}
