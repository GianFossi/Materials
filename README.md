# MaterialLibrary

Type-safe F#/.NET material-property library for ASME Section II Part D workflows, with a CRUD layer, Excel add-in, and Windows desktop app.

## Documentation

- [Overview](docs/overview.md) - features and repository layout.
- [Requirements and dependencies](docs/requirements.md) - SDK, platforms, packages, units, and project notes.
- [Installation, build, and removal](docs/installation.md) - restore/build/test, NuGet package commands, Excel/app publishing, and cleanup.
- [ASME_Materials.db schema](docs/asme-materials-db.md) - packaged SQLite database layout, table relationships, row counts, and safety rules.
- [Library API](docs/library-api.md) - F# examples, checked APIs, persistence, and database lookup.
- [Material models and tables](docs/material-models.md) - fixed units, external pressure, stress-strain, unified tables, and creep model selection.
- [Excel add-in](docs/excel-addin.md) - build/load steps and worksheet functions.
- [Desktop app](docs/desktop-app.md) - WPF CRUD app, examples, database manager, and F#/C# interop notes.
- [Release and safety notes](docs/release-safety.md) - database working-copy safety, backups, undo/redo, and publishing notes.Excel installation is 32-bit.
- [AI instructions](AI_Instructiions/ai-instructions.md) - project-specific assistant workflow notes.
- [License](docs/license.md).

## Quick Start

```powershell
dotnet restore
dotnet build src/MaterialLibrary/MaterialLibrary.fsproj
dotnet run --project tests/MaterialLibrary.Tests/MaterialLibrary.Tests.fsproj
```

## Main Projects

- `src/MaterialLibrary` - core F# domain library and NuGet package.
- `src/MaterialLibrary.Crud` - CRUD helpers and database persistence support.
- `src/MaterialLibrary.Excel` - Excel-DNA worksheet add-in.
- `src/MaterialLibrary.CrudApp` - Windows WPF desktop CRUD app.
- `tests/MaterialLibrary.Tests` - compiled tests.
- `tests/MaterialLibrary.Examples` - compiled examples.

## NuGet

The package readmes are intentionally short and point back to these repository docs:

- `src/MaterialLibrary/README-NUGET.md`
- `src/MaterialLibrary.Crud/README-NUGET.md`

## GitHub

GitHub renders this root README as the repository landing page. Detailed topics live under `docs/` so direct links remain stable and easier to maintain.
