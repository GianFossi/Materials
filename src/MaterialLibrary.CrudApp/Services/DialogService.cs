using System.Windows;
using MaterialLibrary.Domain;
using MaterialLibraryCrudApp.ViewModels;
using MaterialLibraryCrudApp.Views;
using Microsoft.Win32;

namespace MaterialLibraryCrudApp.Services;

/// <summary>WPF implementation of <see cref="IDialogService"/>, owning all modal UI side effects.</summary>
public sealed class DialogService : IDialogService
{
    private readonly Window _owner;

    /// <summary>Creates the service.</summary>
    /// <param name="owner">Window used as the modal owner for every dialog raised here.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner"/> is <c>null</c>.</exception>
    public DialogService(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public string? AskOpenPath(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };

        // ShowDialog returns bool?; only an explicit true means the user confirmed.
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? AskSavePath(string title, string filter, string? suggestedPath)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter };

        if (!string.IsNullOrWhiteSpace(suggestedPath))
        {
            dialog.FileName = suggestedPath;
        }

        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public void ShowError(string message) =>
        MessageBox.Show(_owner, message, "Material Library", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <inheritdoc />
    public void ShowInformation(string message) =>
        MessageBox.Show(_owner, message, "Material Library", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <inheritdoc />
    public bool ConfirmDelete(string materialId) =>
        MessageBox.Show(
            _owner,
            $"Delete material '{materialId}'?",
            "Confirm delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool ConfirmDestructiveSql(string sql) =>
        MessageBox.Show(_owner, $"Execute this potentially destructive SQL?\n\n{sql}",
            "Confirm SQL", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public bool ConfirmDiscardChanges(string context) =>
        MessageBox.Show(_owner, $"Discard unsaved changes before {context}?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <inheritdoc />
    public Material? EditMaterial(Material? existing)
    {
        var dialog = new MaterialEditWindow(new MaterialEditViewModel(existing)) { Owner = _owner };

        return dialog.ShowDialog() == true ? dialog.ConfirmedMaterial : null;
    }

    /// <inheritdoc />
    public Material? EditMaterialTables(Material material)
    {
        var dialog = new MaterialTablesWindow(new MaterialTablesViewModel(material)) { Owner = _owner };

        return dialog.ShowDialog() == true ? dialog.ConfirmedMaterial : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<Material>? ManageDatabase(IReadOnlyList<Material> currentMaterials)
    {
        var viewModel = new DatabaseViewModel(this, currentMaterials);
        var dialog = new DatabaseWindow(viewModel) { Owner = _owner };
        dialog.ShowDialog();

        // The manager is a workspace rather than an OK/Cancel dialog: whatever the user imported
        // while it was open is handed back, regardless of how the window was closed.
        return viewModel.ImportedMaterials.Count > 0 ? viewModel.ImportedMaterials : null;
    }
}
