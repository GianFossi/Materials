using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.ViewModels;

namespace MaterialLibraryCrudApp.Views;

/// <summary>Modal editor for the numeric tables stored inside a material.</summary>
public partial class MaterialTablesWindow : Window
{
    private readonly MaterialTablesViewModel _viewModel;

    /// <summary>Creates the editor over a material's tables.</summary>
    /// <param name="viewModel">View model owning the working material.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is <c>null</c>.</exception>
    public MaterialTablesWindow(MaterialTablesViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = viewModel;

        // Column layout depends on the selected table, so it is rebuilt whenever that changes.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BuildColumns();
    }

    /// <summary>The material carrying the confirmed table edits, or <c>null</c> while unconfirmed.</summary>
    public Material? ConfirmedMaterial { get; private set; }

    /// <summary>Rebuilds the grid columns when the selected table changes.</summary>
    /// <param name="sender">Event source; unused.</param>
    /// <param name="e">Identifies the changed property.</param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaterialTablesViewModel.SelectedTable))
        {
            BuildColumns();
        }
    }

    /// <summary>Generates one grid column per column of the selected table specification.</summary>
    /// <remarks>
    /// Each column binds to the row view model's integer indexer (<c>[0]</c>, <c>[1]</c>, ...),
    /// which is the only way to address a cell whose position is known solely at runtime.
    /// Optional columns are marked in the header so blank cells read as deliberate.
    /// </remarks>
    private void BuildColumns()
    {
        RowsGrid.Columns.Clear();

        var columns = _viewModel.SelectedTable.Columns;

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];

            RowsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.IsOptional ? column.DisplayHeader + " *" : column.DisplayHeader,
                Width = new DataGridLength(1.0, DataGridLengthUnitType.Star),
                Binding = new Binding($"[{i}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                },
            });
        }
    }

    /// <summary>Commits the grid and closes the dialog when the tables are valid.</summary>
    /// <param name="sender">Event source; unused.</param>
    /// <param name="e">Event data; unused.</param>
    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        // Push any in-progress cell edit into the row view model before validating.
        RowsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        StressStrainPointsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        if (!_viewModel.TryBuildMaterial(out var material, out var error))
        {
            MessageBox.Show(this, error, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ConfirmedMaterial = material;
        DialogResult = true;
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }
}
