# MaterialLibrary

F# engineering-material library targeting **.NET 10** (`net10.0`).
Provides native ASME 2025 database reads, typed material models, interpolation,
checked calculations and versioned document serialization.

## Install

```powershell
dotnet add package MaterialLibrary --version 1.0.2
```

The consuming project must target .NET 10 or a compatible later framework.
The WPF editor and Excel add-in are separate applications, not part of this core package.

## 1. Database and data model

- The package includes `data/materials_2025.db` as a NuGet content file, plus
  physical-property and external-pressure XML datasets and configuration.
- The active database uses native `Main`, mechanical curves, physical-property and
  provenance tables. A material can have multiple independent curve variants.
- XML datasets are **candidates for explicit review and import**, not properties
  automatically populated for every material.
- The archived 2023 database is not a fallback and is not packaged.
- Complete `Material` JSON documents are a separate model/persistence workflow.
  They are not a lossless backup of the native SQLite database.

Read the [database structure guide](https://github.com/GianFossi/Materials/blob/main/docs/asme-materials-db.md)
before selecting material IDs, curves or associations.

## 2. Using the library and maintaining data

Native reads are exposed by `Asme2025Data`: material identities, curve variants and
explicit lookups with fixed units and no extrapolation. Use `MaterialLibrary.Crud`
for native working-copy edits and reviewed XML imports.

The domain API also provides:

- temperature/size property tables and interpolation;
- stress-strain basis and optional isochronous duration;
- creep, fatigue, cyclic, rupture and external-pressure models;
- explicit `Result` errors for checked operations;
- complete JSON schema 17 serialization, with schema 15/16 readable.

### F# example

This synthetic table illustrates interpolation only; it is not an ASME material assignment.

```fsharp
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

let cpTable =
    [ { Temperature = 20.0;  SpecificHeat = 477.0 }
      { Temperature = 100.0; SpecificHeat = 500.0 }
      { Temperature = 200.0; SpecificHeat = 520.0 } ]

match SpecificHeatInterpolation.interpolate Linear 150.0 cpTable with
| Ok cp -> printfn "Cp @ 150degC = %.1f J/(kg*K)" (float cp)
| Error error -> printfn "Interpolation error: %A" error
```

## Engineering limitations

Select and validate the applicable creep model explicitly. Norton/Garofalo do not
represent a complete primary-secondary-tertiary curve; the implemented Kachanov-Omega
model neglects primary creep. Generated creep tables carry applicability warnings.

API 579-1/ASME FFS-1 Annex 10B.5 generation is **not implemented**;
`Api579Annex10B5.ensureImplemented()` reports an error.
Source data and generated curves do not replace verification against the applicable code.

## Documentation

- [Project and documentation map](https://github.com/GianFossi/Materials/blob/main/docs/overview.md)
- [Database structure](https://github.com/GianFossi/Materials/blob/main/docs/asme-materials-db.md)
- [API and persistence](https://github.com/GianFossi/Materials/blob/main/docs/library-api.md)
- [Models and units](https://github.com/GianFossi/Materials/blob/main/docs/material-models.md)
- [App operation and maintenance](https://github.com/GianFossi/Materials/blob/main/docs/desktop-app.md)
- [XML import](https://github.com/GianFossi/Materials/blob/main/docs/xml-physical-import.md)
- [Requirements](https://github.com/GianFossi/Materials/blob/main/docs/requirements.md)
- [License](https://github.com/GianFossi/Materials/blob/main/LICENSE)
