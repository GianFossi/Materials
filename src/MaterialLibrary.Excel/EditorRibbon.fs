namespace MaterialLibrary.Excel

open System
open System.Runtime.InteropServices
open System.Windows
open ExcelDna.Integration
open ExcelDna.Integration.CustomUI
open MaterialLibrary.Crud
open MaterialLibraryCrudApp.ViewModels
open MaterialLibraryCrudApp.Views

/// <summary>Excel ribbon entry to the same native 2025 property editor used by the desktop application.</summary>
[<ComVisible(true)>]
type Asme2025Ribbon() =
    inherit ExcelRibbon()
    /// <summary>Expose a discoverable ASME 2025 editor and explicit database reload action.</summary>
    override _.GetCustomUI(_) =
        """<customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui"><ribbon><tabs><tab id="materials2025" label="Materiali ASME 2025"><group id="editionEditor" label="Database 2025"><button id="open2025editor" label="Modifica proprietà" size="large" imageMso="TableProperties" onAction="OpenEditor"/><button id="reload2025" label="Ricarica database" onAction="ReloadDatabase"/></group></tab></tabs></ribbon></customUI>"""
    /// <summary>Open the editor on a user-writable working copy and reload Excel after it closes.</summary>
    member _.OpenEditor(_: IRibbonControl) =
        try
            match Asme2025Crud.prepareWorkingCopy (LibraryCache.databasePath ()) with
            | Error error -> MessageBox.Show(error,"ASME 2025") |> ignore
            | Ok path ->
                let editor = Asme2025EditorWindow(Asme2025EditorViewModel(path,Nullable()))
                editor.ShowDialog() |> ignore
                match LibraryCache.loadDatabase (Some path) with
                | Ok _ -> ExcelAsyncUtil.QueueAsMacro(fun () -> XlCall.Excel(XlCall.xlcCalculateNow) |> ignore)
                | Error error -> MessageBox.Show(error,"ASME 2025") |> ignore
        with ex -> MessageBox.Show(ex.Message,"ASME 2025") |> ignore
    /// <summary>Reload the current database snapshot and recalculate formulas after external edits.</summary>
    member _.ReloadDatabase(_: IRibbonControl) =
        match LibraryCache.loadDatabase (Some(LibraryCache.databasePath ())) with
        | Ok _ -> ExcelAsyncUtil.QueueAsMacro(fun () -> XlCall.Excel(XlCall.xlcCalculateNow) |> ignore)
        | Error error -> MessageBox.Show(error,"ASME 2025") |> ignore
