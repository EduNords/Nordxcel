using System;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// Aba Fórmulas: biblioteca de funções por grupo, igual ao Excel. Cada botão só
/// começa a edição da célula ativa com <c>=NOME(</c> já digitado — quem aplica
/// isso de verdade é <see cref="SpreadsheetView.StartFunctionEntry"/>.
/// </summary>
public sealed partial class RibbonBar
{
    private Control BuildFormulasPage()
    {
        Control financial = RibbonGroup("Financeiras", FunctionRow("VPL", "TIR", "VF", "TAXA"));
        Control logical = RibbonGroup("Lógicas", FunctionRow("SE", "SEERRO", "E", "OU"));
        Control aggregation = RibbonGroup("Agregação", FunctionRow("SOMA", "MÉDIA", "MÍNIMO", "MÁXIMO", "CONT.VALORES"));
        Control math = RibbonGroup("Matemática", FunctionRow("ARRED", "ABS", "POTÊNCIA", "RAIZ"));

        return PageContent(financial, logical, aggregation, math);
    }

    private StackPanel FunctionRow(params string[] functionNames)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        foreach (string name in functionNames)
        {
            Button button = RibbonButton(name, $"Insere {name}(...) na célula ativa", width: 64, height: 44);
            button.Click += (_, _) => FunctionInsertRequested?.Invoke(this, name);
            row.Children.Add(button);
        }

        return row;
    }
}
