using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// Barra de fórmulas: a caixa de nome com a referência da seleção e o campo com o
/// conteúdo da célula ativa. Mostra a fórmula, não o resultado — é para isso que
/// ela existe num modelo.
/// </summary>
public sealed class FormulaBar : UserControl
{
    private readonly TextBlock _nameBox;
    private readonly TextBox _content;

    private bool _updating;

    public FormulaBar()
    {
        _nameBox = new TextBlock
        {
            Text = "A1",
            Width = 96,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"),
            FontSize = 12,
        };

        _content = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = false,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 12,
        };

        _content.KeyDown += OnContentKeyDown;

        var separator = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromRgb(214, 214, 214)).ToImmutable(),
            Margin = new Thickness(0, 4, 0, 4),
        };

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };

        layout.Children.Add(_nameBox);
        Grid.SetColumn(_nameBox, 0);

        layout.Children.Add(separator);
        Grid.SetColumn(separator, 1);

        layout.Children.Add(_content);
        Grid.SetColumn(_content, 2);

        Content = new Border
        {
            Height = 26,
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)).ToImmutable(),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)).ToImmutable(),
            Child = layout,
        };
    }

    /// <summary>Disparado quando o usuário confirma o conteúdo com Enter.</summary>
    public event EventHandler<string>? Committed;

    /// <summary>Disparado quando o usuário desiste com Esc.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Atualiza a barra a partir da seleção, sem disparar confirmação.</summary>
    public void Update(string reference, string content)
    {
        _updating = true;

        try
        {
            _nameBox.Text = reference;
            _content.Text = content;
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnContentKeyDown(object? sender, KeyEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                Committed?.Invoke(this, _content.Text ?? string.Empty);
                e.Handled = true;
                break;

            case Key.Escape:
                Cancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            default:
                break;
        }
    }
}
