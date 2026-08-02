using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.ComponentModel;
using Microsoft.Win32;
using MaterialLibraryCrudApp.ViewModels;

namespace MaterialLibraryCrudApp.Views;

/// <summary>Modal database manager. All behaviour lives in the bound view model.</summary>
public partial class DatabaseWindow : Window
{
    /// <summary>Creates the window over a database view model.</summary>
    /// <param name="viewModel">View model owning the working copy and material list.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is <c>null</c>.</exception>
    public DatabaseWindow(DatabaseViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += (_, _) => RenderPlotSeries();
        Closed += (_, _) => viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Closing += (_, e) => e.Cancel = !viewModel.CanClose();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatabaseViewModel.PlotSeries))
            Dispatcher.BeginInvoke(RenderPlotSeries);
    }

    private void RenderPlotSeries()
    {
        PlotSeriesCanvas.Children.Clear();
        if (DataContext is not DatabaseViewModel viewModel) return;
        foreach (var series in viewModel.PlotSeries)
        {
            PlotSeriesCanvas.Children.Add(new Polyline
            {
                Points = series.Points,
                Stroke = series.Stroke,
                StrokeThickness = 2,
                SnapsToDevicePixels = true
            });
        }
    }

    private void RawTableGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName == "__rowid")
        {
            e.Cancel = true;
        }
    }

    private void ExportPlotPng_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png|All files (*.*)|*.*", FileName = "plot.png" };
        if (dialog.ShowDialog(this) != true) return;
        PlotExportSurface.Measure(new Size(PlotExportSurface.Width, PlotExportSurface.Height));
        PlotExportSurface.Arrange(new Rect(0, 0, PlotExportSurface.Width, PlotExportSurface.Height));
        PlotExportSurface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)PlotExportSurface.Width, (int)PlotExportSurface.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(PlotExportSurface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
    }
}
