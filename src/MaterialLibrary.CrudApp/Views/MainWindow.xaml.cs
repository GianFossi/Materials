using System.Windows;

namespace MaterialLibraryCrudApp.Views;

/// <summary>Main window shell. All behaviour lives in the bound view model; this class stays empty by design.</summary>
public partial class MainWindow : Window
{
    /// <summary>Initialises the window from its XAML definition.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
