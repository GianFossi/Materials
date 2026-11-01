# MaterialLibrary.Crud

Persistence and maintenance helpers for `MaterialLibrary`, targeting **.NET 10**
(`net10.0`). This package contains the service layer, **not the WPF desktop UI**.

## Install

```powershell
dotnet add package MaterialLibrary.Crud --version 1.0.2
```

The core `MaterialLibrary` dependency supplies the domain model and packaged
`data/materials_2025.db` / XML content.

## 1. Database structure

There are two distinct persistence workflows:

| Workflow | Helpers | Source of truth |
|---|---|---|
| Native ASME 2025 | `Asme2025Crud`, `XmlPropertyReader`, `XmlPropertyMatching`, `XmlPropertyImport` | Normalized SQLite identities, curve variants, source links and local metadata |
| Custom/legacy material documents | `MaterialCrudRepository`, `MaterialDatabaseCrud`, core serialization | Complete `Material` documents and, for legacy databases, SQL projections |

Do not use a legacy document export to overwrite native ASME curves:
the backend rejects this to preserve curve identity and provenance.
The [database guide](https://github.com/GianFossi/Materials/blob/main/docs/asme-materials-db.md)
explains the active schema and the explicitly archived 2023 format.

## 2. Editing, importing and maintaining data

- Native writes require an existing **`.working.db`** copy.
- `prepareWorkingCopy` creates the default local copy on first use and subsequently
  preserves existing edits; it does not refresh it automatically from a new reference.
- Native curve edits are validated, transactional and recorded with before/after snapshots.
- Physical-property/pressure XML imports require explicit assignments, review where
  required, and separate replacement consent. Matching is not engineering approval.
- Configuration helpers manage `MaterialLibrary.config.xml`; staged XML helpers and
  in-memory material CRUD remain separate from native database import.

### F# example: prepare a native working copy

The reference path below is relative to an application output containing packaged data.
Handle errors before continuing; creating a working copy does not import XML datasets.

```fsharp
open MaterialLibrary.Crud

match Asme2025Crud.prepareWorkingCopy @"data\materials_2025.db" with
| Ok path -> printfn "Working database: %s" path
| Error error -> eprintfn "Cannot open working database: %s" error
```

The default copy is
`%LOCALAPPDATA%\MaterialLibrary\ASME2025\materials_2025.working.db`.
An explicit existing `.working.db` path is used directly.
Back up local changes before resetting or replacing a copy.

## Documentation

- [Project map](https://github.com/GianFossi/Materials/blob/main/docs/overview.md)
- [Database structure](https://github.com/GianFossi/Materials/blob/main/docs/asme-materials-db.md)
- [App operation and maintenance](https://github.com/GianFossi/Materials/blob/main/docs/desktop-app.md)
- [Native editor](https://github.com/GianFossi/Materials/blob/main/docs/asme-2025-editor.md)
- [XML import and review](https://github.com/GianFossi/Materials/blob/main/docs/xml-physical-import.md)
- [Edition creation/comparison](https://github.com/GianFossi/Materials/blob/main/docs/database-updater.md)
- [Installation](https://github.com/GianFossi/Materials/blob/main/docs/installation.md)
- [License](https://github.com/GianFossi/Materials/blob/main/LICENSE)
