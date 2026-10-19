using System.Windows;
using MaterialLibrary;
using MaterialLibrary.Crud;
using MaterialLibraryCrudApp.Interop;
using MaterialLibraryCrudApp.Views;

namespace MaterialLibraryCrudApp.ViewModels;

/// <summary>Entry points for the 2025 working database and its native property editor.</summary>
public sealed partial class DatabaseViewModel
{
    /// <summary>Open the configured 2025 reference as a working copy.</summary>
    public AsyncRelayCommand Open2025Command { get; private set; } = null!;
    /// <summary>Edit every supported native curve for the selected MAIN identity.</summary>
    public RelayCommand Edit2025PropertiesCommand { get; private set; } = null!;
    private async Task Open2025Async()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!Asme2025Crud.prepareWorkingCopy(Configuration.resolveAsmeDatabasePath(null)).TryUnwrap(out var path, out var error)) { StatusMessage = error; return; }
            await OpenDatabasePathAsync(path, CancellationToken.None);
        }
        catch (Exception ex) { ShowRawError(ex); }
        finally { IsBusy = false; }
    }
    private void Edit2025Properties()
    {
        if (_workingPath is null) return;
        if (HasUnsavedTableChanges) { StatusMessage = "Save or discard raw-table changes before editing properties."; return; }
        try
        {
            var editor = new Asme2025EditorWindow(new Asme2025EditorViewModel(_workingPath, SelectedMaterial?.DatabaseId, _options));
            editor.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this);
            editor.ShowDialog();
            CaptureWorkingFingerprint();
            RunDetached(RefreshAllAsync, "Refreshing ASME 2025 data");
        }
        catch (Exception ex) { ShowRawError(ex); }
    }
}
