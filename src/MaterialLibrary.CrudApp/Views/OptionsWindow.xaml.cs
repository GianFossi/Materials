using System.IO;
using System.Windows;
using MaterialLibraryCrudApp.Services;
using MaterialLibraryCrudApp.ViewModels;
using Microsoft.Win32;

namespace MaterialLibraryCrudApp.Views;

/// <summary>Edits application-level preferences.</summary>
public partial class OptionsWindow : Window
{
    private readonly OptionsViewModel _viewModel;

    /// <summary>Creates the options window for the supplied preferences.</summary>
    public OptionsWindow(ApplicationOptions options)
    {
        InitializeComponent();
        _viewModel = new OptionsViewModel(options);
        DataContext = _viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TrySave())
        {
            DialogResult = true;
        }
    }

    private void BrowseFlexFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the default XML import folder",
            InitialDirectory = Directory.Exists(_viewModel.FlexXmlFolder)
                ? _viewModel.FlexXmlFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.FlexXmlFolder = dialog.FolderName;
        }
    }

    private void BrowseDatabaseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the default SQLite database",
            Filter = FileFilters.Database,
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(_viewModel.DatabaseFilePath))
                ? Path.GetDirectoryName(_viewModel.DatabaseFilePath)!
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = File.Exists(_viewModel.DatabaseFilePath)
                ? Path.GetFileName(_viewModel.DatabaseFilePath)
                : string.Empty
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.DatabaseFilePath = dialog.FileName;
        }
    }
}
