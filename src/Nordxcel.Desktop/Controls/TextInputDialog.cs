using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// Diálogo modal com um ou mais campos de texto — usado pela Tabela de Dados para
/// pedir referências de célula (entrada, saída) sem precisar de um formulário
/// dedicado para cada combinação de campos.
/// </summary>
public sealed class TextInputDialog : Window
{
    private readonly TextBox[] _inputs;

    private TextInputDialog(string title, string message, string[] labels)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _inputs = new TextBox[labels.Length];

        var layout = new StackPanel { Margin = new Thickness(20) };

        layout.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"),
            FontSize = 13,
        });

        for (int i = 0; i < labels.Length; i++)
        {
            layout.Children.Add(new TextBlock
            {
                Text = labels[i],
                Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 4),
                FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"),
                FontSize = 12,
            });

            var input = new TextBox { FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"), FontSize = 13 };
            input.KeyDown += OnFieldKeyDown;

            _inputs[i] = input;
            layout.Children.Add(input);
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(14, 6, 14, 6) };
        cancel.Click += (_, _) => Close(null);

        var ok = new Button { Content = "OK", Padding = new Thickness(14, 6, 14, 6) };
        ok.Click += (_, _) => Confirm();

        buttonRow.Children.Add(cancel);
        buttonRow.Children.Add(ok);
        layout.Children.Add(buttonRow);

        Content = layout;
    }

    /// <summary>
    /// Mostra o diálogo e devolve o texto de cada campo, na ordem dos rótulos, ou
    /// <c>null</c> se o usuário cancelou.
    /// </summary>
    public static async Task<string[]?> ShowAsync(Window owner, string title, string message, params string[] labels)
    {
        var dialog = new TextInputDialog(title, message, labels);

        if (labels.Length > 0)
        {
            dialog.Opened += (_, _) => dialog._inputs[0].Focus();
        }

        return await dialog.ShowDialog<string[]?>(owner);
    }

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Confirm();
                e.Handled = true;
                break;

            case Key.Escape:
                Close(null);
                e.Handled = true;
                break;
        }
    }

    private void Confirm() =>
        Close(Array.ConvertAll(_inputs, input => input.Text ?? string.Empty));
}
