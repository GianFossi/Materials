using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MaterialLibraryCrudApp.ViewModels;

namespace MaterialLibraryCrudApp.Views;

/// <summary>Native 2025 property editor; commits grid cells before invoking a save command.</summary>
public partial class Asme2025EditorWindow : Window
{
    private bool _loaded;
    /// <summary>Open one working-copy editor and protect unsaved changes on close.</summary>
    public Asme2025EditorWindow(Asme2025EditorViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel;
        Loaded += (_, _) => _loaded = true;
        viewModel.Points.CollectionChanged += (_, _) => { if (_loaded) viewModel.MarkDirty(); };
        AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((_, args) => { if (_loaded && args.OriginalSource is ComboBox combo && BindingOperations.GetBinding(combo, System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.Path.Path == nameof(viewModel.SizeAxis)) viewModel.MarkDirty(); }));
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((sender, args) => { if (_loaded && args.OriginalSource is TextBox box && !box.IsReadOnly) viewModel.MarkDirty(BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path ?? "curve"); }));
        AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((_, _) => { if (_loaded) viewModel.MarkDirty(); }));
        AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((_, _) => { if (_loaded) viewModel.MarkDirty(); }));
        PreviewMouseDown += (_, args) =>
        {
            if (args.OriginalSource is not DependencyObject target || FindButton(target) is null) return;
            if (!PointGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !PointGrid.CommitEdit(DataGridEditingUnit.Row, true) || HasValidationError(this)) args.Handled = true;
        };
        Closing += (_, e) => e.Cancel = !viewModel.AllowDiscard();
    }
    private static Button? FindButton(DependencyObject node)
    {
        while (node is not null) { if (node is Button button) return button; node = VisualTreeHelper.GetParent(node); }
        return null;
    }
    private static bool HasValidationError(DependencyObject node)
    {
        if (Validation.GetHasError(node)) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (HasValidationError(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }
    private void PointGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (_loaded && DataContext is Asme2025EditorViewModel vm) vm.MarkDirty();
    }
}
