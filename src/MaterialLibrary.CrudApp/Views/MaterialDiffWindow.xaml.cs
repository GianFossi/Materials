using System.Windows;
using MaterialLibraryCrudApp.ViewModels;

namespace MaterialLibraryCrudApp.Views;

public partial class MaterialDiffWindow : Window
{
    public MaterialDiffWindow(MaterialDiffViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
