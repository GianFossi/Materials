using System.Windows;
using MaterialLibraryCrudApp.ViewModels;
using MaterialLibrary.Domain;

namespace MaterialLibraryCrudApp.Views;

/// <summary>Modal dialog for creating a material or editing an existing one's identity and basic properties.</summary>
public partial class MaterialEditWindow : Window
{
    private readonly MaterialEditViewModel _viewModel;

    /// <summary>Creates the dialog over an editing buffer.</summary>
    /// <param name="viewModel">Mutable buffer seeded from the material being edited, or from nothing when creating.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is <c>null</c>.</exception>
    public MaterialEditWindow(MaterialEditViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>The material confirmed by the user, or <c>null</c> while the dialog is unconfirmed or cancelled.</summary>
    public Material? ConfirmedMaterial { get; private set; }

    /// <summary>
    /// Validates the buffer and, when valid, converts it to a domain record and closes the dialog.
    /// </summary>
    /// <param name="sender">Event source; unused.</param>
    /// <param name="e">Event data; unused.</param>
    /// <remarks>
    /// Invalid input leaves the dialog open so the user can correct it in place, which is why this
    /// is not simply a command with a <c>CanExecute</c> guard: the user needs to be told
    /// <i>why</i> the values were rejected.
    /// </remarks>
    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryBuildMaterial(out var material, out var validationMessage))
        {
            MessageBox.Show(
                this,
                validationMessage,
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ConfirmedMaterial = material;
        DialogResult = true;
    }
}
