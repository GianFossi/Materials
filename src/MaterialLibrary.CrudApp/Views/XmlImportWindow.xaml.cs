using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MaterialLibraryCrudApp.ViewModels;

namespace MaterialLibraryCrudApp.Views;

/// <summary>Association helper with source/database curve overlays; XML instructions are displayed as data only.</summary>
public partial class XmlImportWindow : Window
{
    private readonly XmlImportViewModel _viewModel;
    /// <summary>Create the graphical XML import workflow over an explicit working copy.</summary>
    public XmlImportWindow(XmlImportViewModel viewModel)
    {
        InitializeComponent(); DataContext = _viewModel = viewModel;
        viewModel.PropertyChanged += Changed;
        Closed += (_, _) => viewModel.PropertyChanged -= Changed;
    }
    /// <summary>Refresh the plot or target view after a preview update.</summary>
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(XmlImportViewModel.PreviewRecord) or nameof(XmlImportViewModel.ExistingCurves)) Draw();
        if (e.PropertyName == nameof(XmlImportViewModel.Targets)) System.Windows.Data.CollectionViewSource.GetDefaultView(_viewModel.Targets).Refresh();
    }
    /// <summary>Redraw at the current display dimensions.</summary>
    private void Plot_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();
    /// <summary>Plot SI physical values or log10(A)/B MPa with independent temperature series.</summary>
    private void Draw()
    {
        if (Plot is null || _viewModel is null) return;
        Plot.Children.Clear();
        var record = _viewModel.PreviewRecord;
        var series = new List<(string Name, Brush Color, (double X, double Y)[] Points)>();
        if (record is not null)
        {
            foreach (var group in record.Points.GroupBy(p => record.PropertyCode == "EP" ? p.Temperature : 0))
                series.Add(($"XML {record.PropertyCode} {(record.PropertyCode == "EP" ? group.Key + " °C" : "")}", Brushes.RoyalBlue, group.Select(p => (record.PropertyCode == "EP" ? Math.Log10(p.X) : p.Temperature, p.Y)).Where(p => double.IsFinite(p.Item1) && double.IsFinite(p.Y)).OrderBy(p => p.Item1).ToArray()));
        }
        foreach (var curve in _viewModel.ExistingCurves) series.Add(($"DB #{curve.Id}", Brushes.Gray, curve.Points.Select(p => (p.Temperature, p.Value)).ToArray()));
        foreach (var curve in _viewModel.ExistingPressureCurves) series.Add((curve.Name, Brushes.Gray, curve.Points.Select(p => (Math.Log10(p.A), p.B)).ToArray()));
        var points = series.SelectMany(s => s.Points).ToArray();
        PlotLegend.Text = string.Join(" · ", series.Select(s => s.Name));
        if (points.Length == 0) { PlotLegend.Text = "Nessun punto: selezionare o ricostruire un record numerico verificato."; return; }
        double x0 = points.Min(p => p.X), x1 = points.Max(p => p.X), y0 = points.Min(p => p.Y), y1 = points.Max(p => p.Y);
        var width = Math.Max(1, Plot.ActualWidth - 72); var height = Math.Max(1, Plot.ActualHeight - 40);
        foreach (var s in series)
        {
            var line = new Polyline { Stroke = s.Color, StrokeThickness = 2 };
            foreach (var p in s.Points) line.Points.Add(new(56 + (p.X-x0)/Math.Max(1e-12,x1-x0)*width, 10+height-(p.Y-y0)/Math.Max(1e-12,y1-y0)*height));
            Plot.Children.Add(line);
            foreach (var point in line.Points) { var dot = new Ellipse { Width = 5, Height = 5, Fill = s.Color }; Canvas.SetLeft(dot, point.X - 2.5); Canvas.SetTop(dot, point.Y - 2.5); Plot.Children.Add(dot); }
        }
        var label = new TextBlock { Text = $"X: {x0:G4} … {x1:G4}   Y: {y0:G4} … {y1:G4}", FontSize = 10 };
        Canvas.SetLeft(label, 8); Canvas.SetBottom(label, 4); Plot.Children.Add(label);
    }
}
