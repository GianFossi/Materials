using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
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
        viewModel.CurveNotes.CollectionChanged += (_, _) => { if (_loaded) viewModel.MarkDirty(); };
        AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((_, args) =>
        {
            if (!_loaded || args.OriginalSource is not ComboBox combo) return;
            var path = BindingOperations.GetBinding(combo, System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.Path.Path;
            if (path is nameof(viewModel.SizeAxis) or nameof(viewModel.S1StressLevel)) viewModel.MarkDirty();
        }));
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((sender, args) => { if (_loaded && args.OriginalSource is TextBox box && !box.IsReadOnly) viewModel.MarkDirty(BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path ?? "curve"); }));
        AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((_, _) => { if (_loaded) viewModel.MarkDirty(); }));
        AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((_, _) => { if (_loaded) viewModel.MarkDirty(); }));
        PreviewMouseDown += (_, args) =>
        {
            if (args.OriginalSource is not DependencyObject target || FindButton(target) is null) return;
            var pointsCommitted = PointGrid.CommitEdit(DataGridEditingUnit.Cell, true) && PointGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var notesCommitted = CurveNotesGrid.CommitEdit(DataGridEditingUnit.Cell, true) && CurveNotesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (!pointsCommitted || !notesCommitted || HasValidationError(this)) args.Handled = true;
        };
        Closing += (_, e) => e.Cancel = !viewModel.AllowDiscard();
    }
    private static Button? FindButton(DependencyObject node)
    {
        DependencyObject? current = node;
        while (current is not null) { if (current is Button button) return button; current = GetParent(current); }
        return null;
    }
    private static DependencyObject? GetParent(DependencyObject node)
    {
        if (node is Visual or Visual3D) return VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        if (node is FrameworkContentElement frameworkContentElement) return frameworkContentElement.Parent ?? ContentOperations.GetParent(frameworkContentElement);
        if (node is ContentElement contentElement) return ContentOperations.GetParent(contentElement);
        return LogicalTreeHelper.GetParent(node);
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
    private void CurveNotesGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (_loaded && DataContext is Asme2025EditorViewModel vm) vm.MarkDirty();
    }
}
