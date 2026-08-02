using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.Interop;
using MaterialLibraryCrudApp.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Turning the visible table into a plotted series.</summary>
public sealed partial class DatabaseViewModel
{

    private void PlotTable()
    {
        if (_currentTable is null) return;

        var yColumns = (string.IsNullOrWhiteSpace(PlotYColumns) ? PlotYColumn : PlotYColumns)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _plotSeries.Clear();
        var values = new List<(double X, double Y)>();
        foreach (DataRow row in _currentTable.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            if (!TryGetDouble(row[PlotXColumn], out var x)) continue;
            foreach (var yColumn in yColumns)
                if (row.Table.Columns.Contains(yColumn) && TryGetDouble(row[yColumn], out var y))
                    values.Add((x, y));
        }

        if (values.Count == 0)
        {
            PlotPoints = [];
            PlotXAxisMinimum = "";
            PlotXAxisMaximum = "";
            PlotYAxisMinimum = "";
            PlotYAxisMaximum = "";
            RaisePropertyChanged(nameof(PlotSeries));
            RaisePropertyChanged(nameof(PlotXAxisMinimum));
            RaisePropertyChanged(nameof(PlotXAxisMaximum));
            RaisePropertyChanged(nameof(PlotYAxisMinimum));
            RaisePropertyChanged(nameof(PlotYAxisMaximum));
            PlotMessage = "No numeric rows found in the loaded page for those columns.";
            return;
        }

        var minX = values.Min(v => v.X);
        var maxX = values.Max(v => v.X);
        var minY = values.Min(v => v.Y);
        var maxY = values.Max(v => v.Y);
        var spanX = Math.Max(maxX - minX, double.Epsilon);
        var spanY = Math.Max(maxY - minY, double.Epsilon);
        PlotXAxisMinimum = minX.ToString("G5");
        PlotXAxisMaximum = maxX.ToString("G5");
        PlotYAxisMinimum = minY.ToString("G5");
        PlotYAxisMaximum = maxY.ToString("G5");
        PlotXAxisQuarter1 = (minX + spanX * 0.25).ToString("G5");
        PlotXAxisQuarter2 = (minX + spanX * 0.50).ToString("G5");
        PlotXAxisQuarter3 = (minX + spanX * 0.75).ToString("G5");
        PlotYAxisQuarter1 = (minY + spanY * 0.25).ToString("G5");
        PlotYAxisQuarter2 = (minY + spanY * 0.50).ToString("G5");
        PlotYAxisQuarter3 = (minY + spanY * 0.75).ToString("G5");
        RaisePropertyChanged(nameof(PlotXAxisMinimum));
        RaisePropertyChanged(nameof(PlotXAxisMaximum));
        RaisePropertyChanged(nameof(PlotYAxisMinimum));
        RaisePropertyChanged(nameof(PlotYAxisMaximum));
        RaisePropertyChanged(nameof(PlotXAxisQuarter1)); RaisePropertyChanged(nameof(PlotXAxisQuarter2)); RaisePropertyChanged(nameof(PlotXAxisQuarter3));
        RaisePropertyChanged(nameof(PlotYAxisQuarter1)); RaisePropertyChanged(nameof(PlotYAxisQuarter2)); RaisePropertyChanged(nameof(PlotYAxisQuarter3));
        const double width = 680.0;
        const double height = 240.0;
        const double pad = 18.0;

        for (var seriesIndex = 0; seriesIndex < yColumns.Count; seriesIndex++)
        {
            var yColumn = yColumns[seriesIndex];
            var points = new PointCollection();
            foreach (DataRow row in _currentTable.Rows)
            {
                if (!TryGetDouble(row[PlotXColumn], out var x) || !row.Table.Columns.Contains(yColumn) || !TryGetDouble(row[yColumn], out var y)) continue;
                points.Add(new Point(pad + ((x - minX) / spanX) * (width - pad * 2.0), height - pad - ((y - minY) / spanY) * (height - pad * 2.0)));
            }
            _plotSeries.Add(new PlotSeriesViewModel(yColumn, points, seriesIndex));
        }

        RaisePropertyChanged(nameof(PlotSeries));

        PlotPoints = _plotSeries.FirstOrDefault()?.Points ?? [];
        PlotMessage = $"{values.Count:N0} point(s), {PlotXAxisLabel}: {minX:G4}..{maxX:G4}, {PlotYAxisLabel}: {minY:G4}..{maxY:G4}.";
    }
}
