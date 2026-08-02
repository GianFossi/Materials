using MaterialLibrary.Domain;

namespace MaterialLibraryCrudApp.Services;

/// <summary>File-picker filters used across the application.</summary>
public static class FileFilters
{
    /// <summary>Material library JSON files.</summary>
    public const string LibraryJson = "Material library JSON (*.json)|*.json|All files (*.*)|*.*";

    /// <summary>Material library XML files.</summary>
    public const string LibraryXml = "Material library XML (*.xml)|*.xml|All files (*.*)|*.*";

    /// <summary>Single-material XML files.</summary>
    public const string MaterialXml = "Material XML (*.xml)|*.xml|All files (*.*)|*.*";

    /// <summary>Staged XML data files imported into a material.</summary>
    public const string DataXml = "XML data file (*.xml)|*.xml|All files (*.*)|*.*";

    /// <summary>SQLite material databases.</summary>
    public const string Database = "SQLite material database (*.db;*.sqlite)|*.db;*.sqlite|All files (*.*)|*.*";
    /// <summary>Comma-separated exports of query or table results.</summary>
    public const string Csv = "Comma-separated values (*.csv)|*.csv|All files (*.*)|*.*";

    /// <summary>JSON exports of query or table results.</summary>
    public const string Json = "JSON (*.json)|*.json|All files (*.*)|*.*";

    /// <summary>Excel workbook exports of query or table results.</summary>
    public const string ExcelXml = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*";
}

/// <summary>
/// Abstraction over every user-facing interaction that has a side effect: file pickers,
/// message boxes, and the modal editors.
/// </summary>
/// <remarks>
/// The view models depend on this interface rather than on <c>MessageBox</c> or
/// <c>OpenFileDialog</c> directly, which keeps their logic free of WPF side effects and testable
/// with a stub implementation.
/// </remarks>
public interface IDialogService
{
    /// <summary>Asks the user to pick an existing file.</summary>
    /// <param name="title">Dialog caption.</param>
    /// <param name="filter">File-type filter, from <see cref="FileFilters"/>.</param>
    /// <returns>The chosen absolute path, or <c>null</c> when the user cancelled.</returns>
    string? AskOpenPath(string title, string filter);

    /// <summary>Asks the user where to write a file.</summary>
    /// <param name="title">Dialog caption.</param>
    /// <param name="filter">File-type filter, from <see cref="FileFilters"/>.</param>
    /// <param name="suggestedPath">Path to pre-fill, or <c>null</c> for none.</param>
    /// <returns>The chosen absolute path, or <c>null</c> when the user cancelled.</returns>
    string? AskSavePath(string title, string filter, string? suggestedPath);

    /// <summary>Shows a blocking error message.</summary>
    /// <param name="message">Text to display.</param>
    void ShowError(string message);

    /// <summary>Shows a blocking informational message.</summary>
    /// <param name="message">Text to display.</param>
    void ShowInformation(string message);

    /// <summary>Asks the user to confirm deletion of a material.</summary>
    /// <param name="materialId">Identifier shown in the prompt.</param>
    /// <returns><c>true</c> when the user confirmed.</returns>
    bool ConfirmDelete(string materialId);
    /// <summary>Asks the user to confirm a statement that can modify or drop data.</summary>
    /// <param name="sql">Statement shown in the prompt so the user sees exactly what will run.</param>
    /// <returns><c>true</c> when the user confirmed.</returns>
    bool ConfirmDestructiveSql(string sql);

    /// <summary>
    /// Asks the user to confirm overwriting the original reference database.
    /// </summary>
    /// <param name="path">Path that would be overwritten.</param>
    /// <returns><c>true</c> when the user confirmed.</returns>
    /// <remarks>
    /// The working-copy design exists so the shipped reference data cannot be damaged by accident.
    /// Writing over it is still allowed, but only as a deliberate, separately confirmed act.
    /// </remarks>
    bool ConfirmOverwriteReference(string path);

    /// <summary>Asks the user whether to abandon unsaved edits before another action.</summary>
    /// <param name="context">Short description of the action that would discard the edits.</param>
    /// <returns><c>true</c> when the user agreed to discard.</returns>
    bool ConfirmDiscardChanges(string context);

    /// <summary>Shows the modal create/edit dialog.</summary>
    /// <param name="existing">Material to edit, or <c>null</c> to create a new one.</param>
    /// <returns>The confirmed material, or <c>null</c> when the dialog was cancelled.</returns>
    Material? EditMaterial(Material? existing);

    /// <summary>Shows the modal editor for the numeric tables stored inside a material.</summary>
    /// <param name="material">Material whose tables are edited; never mutated.</param>
    /// <returns>The material carrying the confirmed edits, or <c>null</c> when cancelled.</returns>
    Material? EditMaterialTables(Material material);

    /// <summary>Shows the database manager.</summary>
    /// <param name="currentMaterials">Materials of the in-memory library, offered for export.</param>
    /// <returns>
    /// Materials the user imported from the database, or <c>null</c> when nothing was imported.
    /// </returns>
    IReadOnlyList<Material>? ManageDatabase(IReadOnlyList<Material> currentMaterials);
}
