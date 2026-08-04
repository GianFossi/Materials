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

---

## Manual CRUD Operations on the Database

### 1. The linking key

`Materials.ID` (integer, ASME table) is the **central hub**. Every application extension table declares:

```sql
MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE
```

The ownership chain is:

```
Materials  (ASME, integer ID)
  ├── MaterialLibraryExtension        ← also holds MaterialKey (string, e.g. "SA-516-70")
  ├── MaterialDocumentStore           ← full JSON document, source of truth
  ├── MaterialAllowableStressDatasetRows
  ├── MaterialElasticModulusRows
  ├── MaterialThermalExpansionRows
  ├── MaterialAsmeCodeRows
  └── ... all other *Rows tables
```

`MaterialLibraryExtension` is the **secondary** bridge: it maps the string domain key (`MaterialKey`) back to the integer `MaterialID`, and the unique index `IX_MaterialLibraryExtension_MaterialKey` enforces that mapping.

---

### 2. Which tables are safe to edit manually?

**Projection tables** (`MaterialElasticModulusRows`, `MaterialAllowableStressDatasetRows`, `MaterialThermalExpansionRows`, etc.) are **rebuilt from scratch by "Write Library to DB"**. Editing them directly in the Raw Tables tab will be overwritten the next time a write is triggered. They are read-only from a data-authoring perspective.

**The only two tables you should author manually are:**

- `MaterialDocumentStore` — the JSON payload is the canonical source of truth for all property curves (Sy, Su, allowable stresses, creep, fatigue, stress-strain, external-pressure charts).
- `MaterialLibraryExtension` — scalar metadata per material: family, welding P/G numbers, maximum allowable temperatures, reduction of area, thermal-expansion reference temperature.

**Safe-edit checklist for the Raw Tables tab:**

1. Always work on the **working copy** — the app enforces this and never writes to the reference database.
2. Run **Integrity Check** after any manual SQL or raw-grid edit.
3. Use **Save Working Copy As…** to export the result to a permanent file.

---

### 3. Where to perform each CRUD operation

| Goal | Where to act |
|---|---|
| Add / edit a material | **Materials tab → edit fields → Write Library to DB** |
| Edit allowable stress data | **Materials tab → select material → Allowable Stress editor** |
| Manual SQL on extension tables | **SQL tab** (auto-creates a timestamped backup before each run) |
| Inspect raw row data | **Raw Tables tab** (read-only intent; Save Changes commits to working copy) |
| Persist the result permanently | **Save Working Copy As…** → overwrite or save to a new file |

> **Never edit `asme_materials.db` directly.** The app opens it read-only; all writes target `asme_materials.working.db`. Only **Save Working Copy As…** exports that back to a permanent file.

