using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Nordxcel.Core.Calculation;

namespace Nordxcel.Desktop.Controls;

/// <summary>
/// A planilha com as barras de rolagem. A rolagem é em pixels, e não em células
/// inteiras, para o movimento ficar contínuo como o do Excel moderno.
/// </summary>
public sealed class SpreadsheetView : UserControl
{
    private readonly SpreadsheetCanvas _canvas = new();
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical };
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal };

    private bool _syncing;

    public SpreadsheetView()
    {
        _vertical.Scroll += OnVerticalScroll;
        _horizontal.Scroll += OnHorizontalScroll;
        _canvas.ScrollChanged += (_, _) => SyncScrollBars();

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("*,Auto"),
        };

        layout.Children.Add(_canvas);
        Grid.SetRow(_canvas, 0);
        Grid.SetColumn(_canvas, 0);

        layout.Children.Add(_vertical);
        Grid.SetRow(_vertical, 0);
        Grid.SetColumn(_vertical, 1);

        layout.Children.Add(_horizontal);
        Grid.SetRow(_horizontal, 1);
        Grid.SetColumn(_horizontal, 0);

        Content = layout;
    }

    public CalculationEngine? Engine
    {
        get => _canvas.Engine;
        set
        {
            _canvas.Engine = value;
            SyncScrollBars();
        }
    }

    public string SheetName
    {
        get => _canvas.SheetName;
        set
        {
            _canvas.SheetName = value;
            _canvas.ScrollX = 0d;
            _canvas.ScrollY = 0d;
            SyncScrollBars();
        }
    }

    public SpreadsheetCanvas Canvas => _canvas;

    /// <summary>Redesenha e reajusta as barras depois de o conteúdo mudar.</summary>
    public void Refresh()
    {
        _canvas.Refresh();
        SyncScrollBars();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size result = base.ArrangeOverride(finalSize);
        SyncScrollBars();

        return result;
    }

    private void OnVerticalScroll(object? sender, ScrollEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _canvas.ScrollY = _vertical.Value;
    }

    private void OnHorizontalScroll(object? sender, ScrollEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _canvas.ScrollX = _horizontal.Value;
    }

    private void SyncScrollBars()
    {
        (double extentWidth, double extentHeight) = _canvas.GetScrollExtent();

        _syncing = true;

        try
        {
            Configure(_vertical, extentHeight, _canvas.ViewportHeight, _canvas.ScrollY);
            Configure(_horizontal, extentWidth, _canvas.ViewportWidth, _canvas.ScrollX);
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void Configure(ScrollBar bar, double extent, double viewport, double value)
    {
        double maximum = Math.Max(0d, extent - viewport);

        bar.Minimum = 0d;
        bar.Maximum = maximum;
        bar.ViewportSize = viewport;
        bar.LargeChange = Math.Max(viewport, 1d);
        bar.SmallChange = 20d;
        bar.Value = Math.Clamp(value, 0d, maximum);
        bar.IsEnabled = maximum > 0d;
    }
}
