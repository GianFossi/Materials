namespace MaterialLibrary.Excel

open ExcelDna.Integration
open SQLitePCL

/// <summary>
/// Excel-DNA add-in lifecycle hooks. Excel-DNA discovers any type implementing
/// <see cref="IExcelAddIn"/> in this assembly and calls <c>AutoOpen</c> when the add-in loads and
/// <c>AutoClose</c> when it unloads or Excel closes.
/// </summary>
type AddIn() =
    interface IExcelAddIn with
        /// <summary>
        /// Eagerly loads the default ASME database so the first worksheet call a user makes is not
        /// slowed down by the initial load. Failures here are not fatal: <see cref="LibraryCache"/>
        /// leaves its state unloaded on error and every query function still reports a clear message
        /// through its normal <c>Result</c>-based error path on next use.
        /// </summary>
        member _.AutoOpen() =
            try
                Batteries_V2.Init()
                LibraryCache.ensureLoaded ()
            with _ ->
                ()

        member _.AutoClose() = ()
