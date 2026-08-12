using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nordxcel.Core.Model;

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// Barra de abas na parte de baixo da janela, como no Excel: clique troca de aba,
/// duplo clique renomeia, botão da direita exclui, e o "+" cria uma nova.
/// </summary>
public sealed class SheetTabStrip : UserControl
{
    private static readonly IBrush ActiveBackground = new SolidColorBrush(Colors.White).ToImmutable();
    private static readonly IBrush InactiveBackground = new SolidColorBrush(Color.FromRgb(230, 230, 230)).ToImmutable();
    private static readonly IBrush StripBackground = new SolidColorBrush(Color.FromRgb(214, 214, 214)).ToImmutable();
    private static readonly IBrush BorderColor = new SolidColorBrush(Color.FromRgb(180, 180, 180)).ToImmutable();
    private static readonly IBrush ActiveIndicator = new SolidColorBrush(Color.FromRgb(33, 115, 70)).ToImmutable();

    private readonly StackPanel _tabsPanel;
    private readonly TextBox _renameBox;

    private Workbook? _workbook;
    private string _activeSheet = string.Empty;
    private string? _renamingSheet;

    public SheetTabStrip()
    {
        _tabsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        _renameBox = new TextBox
        {
            IsVisible = false,
            Width = 120,
            FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"),
            FontSize = 12,
            Padding = new Thickness(6, 2, 6, 2),
        };

        _renameBox.KeyDown += OnRenameBoxKeyDown;
        _renameBox.LostFocus += (_, _) => CommitRename();

        var addButton = new Button
        {
            Content = "+",
            Width = 26,
            Height = 24,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(0),
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        addButton.Click += (_, _) => AddSheet();

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        content.Children.Add(_tabsPanel);
        content.Children.Add(_renameBox);
        content.Children.Add(addButton);

        Content = new Border
        {
            Background = StripBackground,
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = BorderColor,
            Padding = new Thickness(4, 4, 4, 4),
            Child = content,
        };
    }

    /// <summary>Disparado quando o usuário clica numa aba diferente da ativa.</summary>
    public event EventHandler<string>? SheetSelected;

    /// <summary>
    /// Disparado depois de uma aba ser renomeada, com o nome antigo e o novo. Quem
    /// desenha a planilha guarda o nome da aba exibida e precisa ser avisado
    /// diretamente — o <see cref="Worksheet"/> por trás continua sendo o mesmo, só
    /// o rótulo muda, então nem seleção nem rolagem devem ser afetadas.
    /// </summary>
    public event EventHandler<(string OldName, string NewName)>? SheetRenamed;

    /// <summary>Disparado depois de criar, renomear, excluir ou mover uma aba.</summary>
    public event EventHandler? WorkbookChanged;

    public Workbook? Workbook
    {
        get => _workbook;
        set
        {
            _workbook = value;
            Rebuild();
        }
    }

    public string ActiveSheet
    {
        get => _activeSheet;
        set
        {
            if (string.Equals(_activeSheet, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _activeSheet = value;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _tabsPanel.Children.Clear();

        if (_workbook is null)
        {
            return;
        }

        foreach (Worksheet sheet in _workbook.Worksheets)
        {
            _tabsPanel.Children.Add(CreateTab(sheet.Name));
        }
    }

    private Control CreateTab(string name)
    {
        bool isActive = string.Equals(name, _activeSheet, StringComparison.OrdinalIgnoreCase);

        var label = new TextBlock
        {
            Text = name,
            FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"),
            FontSize = 12,
            FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal,
            Foreground = new SolidColorBrush(isActive ? Color.FromRgb(20, 70, 43) : Color.FromRgb(70, 70, 70)).ToImmutable(),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var border = new Border
        {
            Background = isActive ? ActiveBackground : InactiveBackground,
            BorderThickness = new Thickness(1, 1, 1, isActive ? 2 : 1),
            BorderBrush = isActive ? ActiveIndicator : BorderColor,
            Padding = new Thickness(12, 4, 12, 4),
            CornerRadius = new CornerRadius(3, 3, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = label,
        };

        border.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(border).Properties.IsRightButtonPressed)
            {
                ShowContextMenu(border, name);
                e.Handled = true;
                return;
            }

            if (e.ClickCount >= 2)
            {
                BeginRename(name);
                e.Handled = true;
                return;
            }

            if (!string.Equals(name, _activeSheet, StringComparison.OrdinalIgnoreCase))
            {
                SheetSelected?.Invoke(this, name);
            }

            e.Handled = true;
        };

        return border;
    }

    private void ShowContextMenu(Control anchor, string sheetName)
    {
        bool canDelete = _workbook is not null && _workbook.Worksheets.Count > 1;

        var rename = new MenuItem { Header = "Renomear" };
        rename.Click += (_, _) => BeginRename(sheetName);

        var delete = new MenuItem { Header = "Excluir", IsEnabled = canDelete };
        delete.Click += (_, _) => DeleteSheet(sheetName);

        var menu = new ContextMenu { ItemsSource = new object[] { rename, delete } };

        menu.Open(anchor);
    }

    // ------------------------------------------------------------- ações

    private void AddSheet()
    {
        if (_workbook is null)
        {
            return;
        }

        Worksheet created = _workbook.AddWorksheet();

        Rebuild();
        SheetSelected?.Invoke(this, created.Name);
        WorkbookChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteSheet(string name)
    {
        if (_workbook is null || _workbook.Worksheets.Count <= 1)
        {
            return;
        }

        int index = _workbook.Worksheets
            .Select((sheet, i) => (sheet.Name, Index: i))
            .First(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            .Index;

        _workbook.RemoveWorksheet(name);

        if (string.Equals(_activeSheet, name, StringComparison.OrdinalIgnoreCase))
        {
            int fallback = Math.Min(index, _workbook.Worksheets.Count - 1);
            string next = _workbook.Worksheets[fallback].Name;

            _activeSheet = next;
            SheetSelected?.Invoke(this, next);
        }

        Rebuild();
        WorkbookChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BeginRename(string name)
    {
        _renamingSheet = name;
        _renameBox.Text = name;
        _renameBox.IsVisible = true;

        // Pedir foco no mesmo passo em que o controle vira visível não é
        // confiável no Avalonia — já vimos isso na edição de célula (commit 10):
        // a primeira tecla digitada pode se perder antes do foco realmente chegar.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_renamingSheet != name)
                {
                    return;
                }

                _renameBox.Focus();
                _renameBox.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private void OnRenameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitRename();
                e.Handled = true;
                break;

            case Key.Escape:
                CancelRename();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void CommitRename()
    {
        if (_renamingSheet is null || _workbook is null)
        {
            return;
        }

        string oldName = _renamingSheet;
        string newName = (_renameBox.Text ?? string.Empty).Trim();

        _renamingSheet = null;
        _renameBox.IsVisible = false;

        if (newName.Length == 0 || string.Equals(newName, oldName, StringComparison.Ordinal))
        {
            Rebuild();
            return;
        }

        try
        {
            _workbook.RenameWorksheet(oldName, newName);
        }
        catch (ArgumentException)
        {
            // Nome inválido ou já usado por outra aba: mantém o nome original,
            // sem infraestrutura de diálogo ainda para explicar o motivo.
            Rebuild();
            return;
        }

        if (string.Equals(_activeSheet, oldName, StringComparison.OrdinalIgnoreCase))
        {
            _activeSheet = newName;
        }

        Rebuild();
        SheetRenamed?.Invoke(this, (oldName, newName));
        WorkbookChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CancelRename()
    {
        _renamingSheet = null;
        _renameBox.IsVisible = false;
    }
}
