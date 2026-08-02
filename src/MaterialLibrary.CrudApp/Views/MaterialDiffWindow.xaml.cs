using System.Windows;
using MaterialLibraryCrudApp.ViewModels;

namespace MaterialLibraryCrudApp.Views;

/// <summary>Side-by-side material comparison window. All behaviour lives in the bound view model.</summary>
public partial class MaterialDiffWindow : Window
{
    /// <summary>Creates the window over a comparison view model.</summary>
    /// <param name="viewModel">View model supplying the material lists and the computed differences.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is <c>null</c>.</exception>
    public MaterialDiffWindow(MaterialDiffViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }
}
