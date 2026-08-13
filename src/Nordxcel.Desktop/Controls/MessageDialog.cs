using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// Diálogo modal simples com uma mensagem e botões de texto — o Avalonia não vem
/// com um <c>MessageBox</c> pronto como o WPF/WinForms. Serve tanto para avisar
/// de um erro (arquivo corrompido, sem permissão) quanto para confirmar antes de
/// perder alterações não salvas.
/// </summary>
public sealed class MessageDialog : Window
{
    private MessageDialog(string title, string message, string[] buttons)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 20, 20, 12),
            FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"),
            FontSize = 13,
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(20, 0, 20, 20),
        };

        foreach (string label in buttons)
        {
            var button = new Button
            {
                Content = label,
                Padding = new Thickness(14, 6, 14, 6),
                FontFamily = new FontFamily("Calibri, Segoe UI, sans-serif"),
                FontSize = 13,
            };

            button.Click += (_, _) => Close(label);

            buttonRow.Children.Add(button);
        }

        var layout = new StackPanel();
        layout.Children.Add(text);
        layout.Children.Add(buttonRow);

        Content = layout;
    }

    /// <summary>
    /// Mostra o diálogo e devolve o rótulo do botão clicado, ou <c>null</c> se o
    /// usuário fechou a janela sem escolher nada (Esc, X do canto).
    /// </summary>
    public static Task<string?> ShowAsync(Window owner, string title, string message, params string[] buttons)
    {
        var dialog = new MessageDialog(title, message, buttons);

        return dialog.ShowDialog<string?>(owner);
    }
}
