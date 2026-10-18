# Requirements and Dependencies

## Requirements | Requisiti

- .NET SDK 10.0.100 or later in the .NET 10 SDK line (`global.json` uses `latestMinor`)
- F# language version: `latest`
- Package dependencies:
  - `FSharp.Core` 11.0.100
  - `Ganfoss.ROP` 1.2.0
  - `Microsoft.Data.Sqlite` 10.0.13
  - `System.Text.Json` is provided by the .NET 10 shared framework (no separate NuGet reference)

## Dependencies | Dipendenze

- `Ganfoss.ROP` 1.2.0
- `System.Text.Json` from the .NET 10 shared framework; keep the installed runtime patched
  (self-contained deployments must be republished with a patched runtime)
- `ExcelDna.AddIn` 1.9.0 (`src/MaterialLibrary.Excel` only)
- `NumericInput.Core` 1.3.3 and `NumericInput.WPF` 1.3.3 (`src/MaterialLibrary.CrudApp` only;
  package references only, not yet integrated into UI controls)
- Test dependencies: `Microsoft.NET.Test.Sdk` 18.11.0, `xunit` 2.9.3,
  `xunit.runner.visualstudio` 4.0.1 (VSTest; existing xUnit v2 tests retained)
- Dependencies updated during the .NET 10 migration use the latest stable releases returned by
  NuGet at update time; NumericInput packages are pinned to the requested 1.3.3.
- Transitive dependencies use the versions selected by their upstream packages; they are not
  independently pinned to newer major releases.

## Notes | Note

- Target frameworks: `net10.0` (libraries, examples, core tests) and `net10.0-windows`
  (WPF application, WPF tests, Excel add-in)
- EN: This upgrade drops .NET 8 consumer compatibility; applications must target .NET 10 or later.
- IT: Questo aggiornamento rimuove la compatibilita con .NET 8; le applicazioni devono usare .NET 10 o successivo.
- EN: Install the .NET 10 Desktop Runtime for the WPF application and the matching .NET 10
  runtime architecture for Excel. Packed XLL files bundle add-in dependencies, not the .NET runtime.
- IT: Installare il Desktop Runtime .NET 10 per WPF e il runtime .NET 10 con architettura
  corrispondente a Excel. I file XLL includono le dipendenze, non il runtime .NET.
- F# language version: `latest`
- Output: DLL + NuGet package
- EN: Treat NuGet warnings as release blockers when publishing
- IT: Considera i warning NuGet come blocco rilascio prima della pubblicazione

## Upgrade validation | Verifica aggiornamento

- SDK used: 10.0.201.
- Release builds: Excel add-in (packed x86/x64 XLLs) and examples succeeded; the WPF application
  and both libraries were also built through the test projects.
- Tests: 51 core/CRUD tests and 45 WPF tests passed.
- Compiler nullability warnings remain in existing source code and are not suppressed.
- Interactive Excel loading and worksheet execution were not verified as part of this upgrade.
