using Avalonia.Controls;
using Nordxcel.Core.Calculation;

namespace Nordxcel.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        CalculationEngine engine = SampleContent.CreateWorkbook();

        Sheet.Engine = engine;
        Sheet.SheetName = engine.Workbook.Worksheets[1].Name;
    }
}
