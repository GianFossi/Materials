using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MaterialLibrary;
using MaterialLibraryCrudApp.Interop;
using MaterialLibraryCrudApp.Services;
using MaterialLibraryCrudApp.Views;
using MaterialLibrary.Crud;
using MaterialLibrary.Domain;
using Microsoft.FSharp.Core;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>
/// Drives the main window: owns the CRUD repository and exposes the materials grid,
/// status text, and the create/read/update/delete and file commands.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    /// <summary>Library format version written into saved JSON files.</summary>
    private const string DefaultLibraryVersion = "1.0.2";

    private readonly IDialogService _dialogService;

    private MaterialCrudRepository _repository = new();
    private string? _currentFilePath;
    private string _statusMessage = "Ready.";
    private MaterialRowViewModel? _selectedMaterial;

    /// <summary>Creates the main view model with an empty in-memory library.</summary>
    /// <param name="dialogService">Provider of file pickers, message boxes, and the material editor.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dialogService"/> is <c>null</c>.</exception>
    public MainViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        NewLibraryCommand = new RelayCommand(NewLibrary);
        OpenLibraryCommand = new RelayCommand(OpenLibrary);
        SaveLibraryCommand = new RelayCommand(SaveLibrary);
        SaveLibraryAsCommand = new RelayCommand(SaveLibraryAs);
        NewMaterialCommand = new RelayCommand(NewMaterial);
        EditMaterialCommand = new RelayCommand(EditMaterial, HasSelection);
        EditTablesCommand = new RelayCommand(EditTables, HasSelection);
        ImportXmlDataCommand = new RelayCommand(ImportXmlData, HasSelection);
        OpenLibraryXmlCommand = new RelayCommand(OpenLibraryXml);
        SaveLibraryXmlCommand = new RelayCommand(SaveLibraryXml);
        ImportMaterialXmlCommand = new RelayCommand(ImportMaterialXml);
        ExportMaterialXmlCommand = new RelayCommand(ExportMaterialXml, HasSelection);
        ManageDatabaseCommand = new RelayCommand(ManageDatabase);
        DeleteMaterialCommand = new RelayCommand(DeleteMaterial, HasSelection);
        CompareMaterialsCommand = new RelayCommand(CompareMaterials, () => Materials.Count > 1);

        RefreshMaterials();
    }

    /// <summary>Materials currently in the repository, sorted by ID.</summary>
    public ObservableCollection<MaterialRowViewModel> Materials { get; } = [];

    /// <summary>Row selected in the grid, or <c>null</c> when nothing is selected.</summary>
    public MaterialRowViewModel? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (SetProperty(ref _selectedMaterial, value))
            {
                // Edit and Delete are only meaningful with a selection.
                EditMaterialCommand.RaiseCanExecuteChanged();
                EditTablesCommand.RaiseCanExecuteChanged();
                ImportXmlDataCommand.RaiseCanExecuteChanged();
                ExportMaterialXmlCommand.RaiseCanExecuteChanged();
                DeleteMaterialCommand.RaiseCanExecuteChanged();
                CompareMaterialsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Message shown in the status bar.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Window title, including the current file or an unsaved marker.</summary>
    public string WindowTitle => $"Material Library CRUD - {_currentFilePath ?? "(unsaved)"}";

    /// <summary>Discards the current library and starts an empty one.</summary>
    public RelayCommand NewLibraryCommand { get; }

    /// <summary>Loads a library from a JSON file chosen by the user.</summary>
    public RelayCommand OpenLibraryCommand { get; }

    /// <summary>Saves to the current file, prompting for a path when there is none.</summary>
    public RelayCommand SaveLibraryCommand { get; }

    /// <summary>Saves to a path chosen by the user.</summary>
    public RelayCommand SaveLibraryAsCommand { get; }

    /// <summary>Opens the editor to create a material.</summary>
    public RelayCommand NewMaterialCommand { get; }

    /// <summary>Opens the editor for the selected material.</summary>
    public RelayCommand EditMaterialCommand { get; }

    /// <summary>Opens the table editor for the selected material.</summary>
    public RelayCommand EditTablesCommand { get; }

    /// <summary>Imports a staged XML data file into the selected material.</summary>
    public RelayCommand ImportXmlDataCommand { get; }

    /// <summary>Loads a material library from an XML file.</summary>
    public RelayCommand OpenLibraryXmlCommand { get; }

    /// <summary>Saves the library to an XML file.</summary>
    public RelayCommand SaveLibraryXmlCommand { get; }

    /// <summary>Loads a single material from an XML file into the library.</summary>
    public RelayCommand ImportMaterialXmlCommand { get; }

    /// <summary>Saves the selected material to an XML file.</summary>
    public RelayCommand ExportMaterialXmlCommand { get; }

    /// <summary>Opens the database manager.</summary>
    public RelayCommand ManageDatabaseCommand { get; }

    /// <summary>Deletes the selected material after confirmation.</summary>
    public RelayCommand DeleteMaterialCommand { get; }
    /// <summary>Opens the side-by-side comparison window for the loaded materials.</summary>
    public RelayCommand CompareMaterialsCommand { get; }

    /// <summary>Whether a grid row is currently selected.</summary>
    /// <returns><c>true</c> when <see cref="SelectedMaterial"/> is set.</returns>
    private bool HasSelection() => SelectedMaterial is not null;

    private void CompareMaterials()
    {
        var window = new MaterialDiffWindow(new MaterialDiffViewModel(Materials.ToList(), SelectedMaterial));
        window.Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(item => item.DataContext == this);
        window.ShowDialog();
    }

    /// <summary>Replaces the repository with an empty one.</summary>
    private void NewLibrary()
    {
        _repository = new MaterialCrudRepository();
        _currentFilePath = null;
        RefreshMaterials();
        StatusMessage = "Created a new, empty library.";
    }

    /// <summary>Prompts for a JSON file and loads it into a fresh repository.</summary>
    private void OpenLibrary()
    {
        var path = _dialogService.AskOpenPath("Open material library", FileFilters.LibraryJson);
        if (path is null)
        {
            return;
        }

        if (!MaterialCrudRepository.LoadFromFile(path).TryUnwrap(out var loaded, out var error))
        {
            ShowError(error);
            return;
        }

        _repository = loaded;
        _currentFilePath = path;
        RefreshMaterials();
        StatusMessage = $"Loaded {Materials.Count} material(s) from {path}.";
    }

    /// <summary>Saves to the known path, falling back to Save As when the library has never been saved.</summary>
    private void SaveLibrary()
    {
        if (_currentFilePath is not null)
        {
            SaveToPath(_currentFilePath);
            return;
        }

        SaveLibraryAs();
    }

    /// <summary>Prompts for a destination path and saves there.</summary>
    private void SaveLibraryAs()
    {
        var path = _dialogService.AskSavePath("Save material library", FileFilters.LibraryJson, _currentFilePath);
        if (path is not null)
        {
            SaveToPath(path);
        }
    }

    /// <summary>Writes the repository contents to a JSON file and records it as the current file.</summary>
    /// <param name="path">Destination path.</param>
    private void SaveToPath(string path)
    {
        // SaveToFile takes an optional description; None is passed explicitly because C# cannot
        // omit an F# option-typed parameter.
        var result = _repository.SaveToFile(path, DefaultLibraryVersion, FSharpOption<string>.None);

        // The success payload is unit, so only the error branch carries information.
        if (!result.TryUnwrap(out _, out var error))
        {
            ShowError(error);
            return;
        }

        _currentFilePath = path;
        RaisePropertyChanged(nameof(WindowTitle));
        StatusMessage = $"Saved {Materials.Count} material(s) to {path}.";
    }

    /// <summary>Opens the editor in create mode and adds the confirmed material.</summary>
    private void NewMaterial()
    {
        var created = _dialogService.EditMaterial(null);
        if (created is null)
        {
            return;
        }

        if (!_repository.Create(created).TryUnwrap(out var change, out var error))
        {
            ShowError(error);
            return;
        }

        RefreshMaterials(created.Id);
        StatusMessage = change.Message;
    }

    /// <summary>Opens the editor for the selected material and stores the confirmed result.</summary>
    private void EditMaterial()
    {
        var selected = SelectedMaterial;
        if (selected is null)
        {
            StatusMessage = "Select a material to edit.";
            return;
        }

        // Re-read from the repository rather than trusting the row snapshot, so a stale grid
        // cannot silently overwrite newer data.
        if (!_repository.Read(selected.Id).TryUnwrap(out var current, out var readError))
        {
            ShowError(readError);
            return;
        }

        var edited = _dialogService.EditMaterial(current);
        if (edited is null)
        {
            return;
        }

        if (!_repository.Upsert(edited).TryUnwrap(out var change, out var upsertError))
        {
            ShowError(upsertError);
            return;
        }

        RefreshMaterials(edited.Id);
        StatusMessage = change.Message;
    }

    /// <summary>Opens the tables editor for the selected material and stores the confirmed result.</summary>
    private void EditTables()
    {
        var selected = SelectedMaterial;
        if (selected is null)
        {
            StatusMessage = "Select a material to edit its tables.";
            return;
        }

        // Re-read so the editor always starts from the stored record, not a stale grid snapshot.
        if (!_repository.Read(selected.Id).TryUnwrap(out var current, out var readError))
        {
            ShowError(readError);
            return;
        }

        var edited = _dialogService.EditMaterialTables(current);
        if (edited is null)
        {
            return;
        }

        if (!_repository.Upsert(edited).TryUnwrap(out var change, out var upsertError))
        {
            ShowError(upsertError);
            return;
        }

        RefreshMaterials(edited.Id);
        StatusMessage = change.Message;
    }


    /// <summary>Loads a material library from an XML file, replacing the current one.</summary>
    private void OpenLibraryXml()
    {
        var path = _dialogService.AskOpenPath("Open material library (XML)", FileFilters.LibraryXml);
        if (path is null)
        {
            return;
        }

        if (!MaterialLibraryXmlSerialization.loadFromFile(path).TryUnwrap(out var materials, out var error))
        {
            ShowError(error);
            return;
        }

        _repository = new MaterialCrudRepository(materials);
        // XML and JSON are separate formats: adopting the XML path as the "current file" would make
        // a later plain Save silently rewrite it as JSON.
        _currentFilePath = null;
        RefreshMaterials();
        StatusMessage = $"Loaded {Materials.Count} material(s) from {path}.";
    }

    /// <summary>Saves the whole library to an XML file.</summary>
    private void SaveLibraryXml()
    {
        var path = _dialogService.AskSavePath("Save material library (XML)", FileFilters.LibraryXml, null);
        if (path is null)
        {
            return;
        }

        var result = MaterialLibraryXmlSerialization.saveToFile(
            path,
            DefaultLibraryVersion,
            FSharpOption<string>.None,
            _repository.List());

        if (!result.TryUnwrap(out _, out var error))
        {
            ShowError(error);
            return;
        }

        StatusMessage = $"Saved {Materials.Count} material(s) to {path}.";
    }

    /// <summary>Loads one material from an XML file and adds or replaces it in the library.</summary>
    private void ImportMaterialXml()
    {
        var path = _dialogService.AskOpenPath("Import material (XML)", FileFilters.MaterialXml);
        if (path is null)
        {
            return;
        }

        if (!MaterialXmlSerialization.loadFromFile(path).TryUnwrap(out var material, out var error))
        {
            ShowError(error);
            return;
        }

        // Upsert rather than Create so re-importing a corrected file is not an error.
        if (!_repository.Upsert(material).TryUnwrap(out var change, out var upsertError))
        {
            ShowError(upsertError);
            return;
        }

        RefreshMaterials(material.Id);
        StatusMessage = $"{change.Message} from {path}.";
    }

    /// <summary>Saves the selected material to its own XML file.</summary>
    private void ExportMaterialXml()
    {
        var selected = SelectedMaterial;
        if (selected is null)
        {
            StatusMessage = "Select a material to export.";
            return;
        }

        if (!_repository.Read(selected.Id).TryUnwrap(out var material, out var readError))
        {
            ShowError(readError);
            return;
        }

        var path = _dialogService.AskSavePath(
            "Export material (XML)",
            FileFilters.MaterialXml,
            selected.Id + ".xml");

        if (path is null)
        {
            return;
        }

        if (!MaterialXmlSerialization.saveToFile(path, material).TryUnwrap(out _, out var writeError))
        {
            ShowError(writeError);
            return;
        }

        StatusMessage = $"Exported {material.Id} to {path}.";
    }

    /// <summary>
    /// Imports a staged XML data file into the selected material and records the reference.
    /// </summary>
    /// <remarks>
    /// The staged files live under a data root and are addressed by a path relative to it, so the
    /// chosen file must sit inside that root. The root is taken to be the file's own directory,
    /// which makes the relative path the bare file name.
    /// </remarks>
    private void ImportXmlData()
    {
        var selected = SelectedMaterial;
        if (selected is null)
        {
            StatusMessage = "Select a material to import XML data into.";
            return;
        }

        var path = _dialogService.AskOpenPath("Import XML data file", FileFilters.DataXml);
        if (path is null)
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var dataRoot = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            _dialogService.ShowError("Could not determine the data root for the selected file.");
            return;
        }

        var result = _repository.ImportXmlDataIntoMaterial(dataRoot, selected.Id, Path.GetFileName(fullPath));

        if (!result.TryUnwrap(out var change, out var error))
        {
            ShowError(error);
            return;
        }

        RefreshMaterials(selected.Id);
        StatusMessage = $"{change.Message} ({Path.GetFileName(fullPath)}).";
    }

    /// <summary>Opens the database manager and merges anything the user imported.</summary>
    private void ManageDatabase()
    {
        var imported = _dialogService.ManageDatabase(_repository.List().ToReadOnlyList());

        if (imported is null || imported.Count == 0)
        {
            return;
        }

        foreach (var material in imported)
        {
            if (!_repository.Upsert(material).TryUnwrap(out _, out var error))
            {
                ShowError(error);
                return;
            }
        }

        RefreshMaterials(imported[^1].Id);
        StatusMessage = $"Imported {imported.Count} material(s) from the database.";
    }

    /// <summary>Deletes the selected material after asking for confirmation.</summary>
    private void DeleteMaterial()
    {
        var selected = SelectedMaterial;
        if (selected is null)
        {
            StatusMessage = "Select a material to delete.";
            return;
        }

        if (!_dialogService.ConfirmDelete(selected.Id))
        {
            return;
        }

        if (!_repository.Delete(selected.Id).TryUnwrap(out var change, out var error))
        {
            ShowError(error);
            return;
        }

        RefreshMaterials();
        StatusMessage = change.Message;
    }

    /// <summary>
    /// Rebuilds the grid from the repository, sorted by ID, optionally restoring a selection.
    /// </summary>
    /// <param name="idToSelect">Material ID to re-select after the rebuild, or <c>null</c> to clear the selection.</param>
    /// <remarks>
    /// The repository returns an immutable F# list with no change notifications, so the observable
    /// collection is repopulated wholesale rather than diffed. The material count in this
    /// application is small enough that the simpler approach is preferable.
    /// </remarks>
    private void RefreshMaterials(string? idToSelect = null)
    {
        Materials.Clear();

        var ordered = _repository.List()
            .ToReadOnlyList()
            .OrderBy(material => material.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var material in ordered)
        {
            Materials.Add(new MaterialRowViewModel(material));
        }

        SelectedMaterial = idToSelect is null
            ? null
            : Materials.FirstOrDefault(row => string.Equals(row.Id, idToSelect, StringComparison.Ordinal));

        RaisePropertyChanged(nameof(WindowTitle));
    }

    /// <summary>Reports a domain error through a dialog and the status bar.</summary>
    /// <param name="error">Error returned by a CRUD operation.</param>
    private void ShowError(MaterialError error)
    {
        var message = MaterialErrorFormat.Format(error);
        _dialogService.ShowError(message);
        StatusMessage = message;
    }
}

