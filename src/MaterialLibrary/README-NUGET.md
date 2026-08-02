# MaterialLibrary

Type-safe F# library for engineering material properties and creep analysis on .NET 8.

## Highlights

- Material domain model with fixed a-priori standard units
- Interpolation for temperature-dependent properties
- Creep models: Norton, Garofalo, Kachanov Omega
- Unified time-independent and isochronous stress-strain workflows
- Unified external-pressure material tables from database data or Code Case 2964 generation
- Time-independent and time-dependent tables represented by an optional reference duration
- One stress-strain table type with optional isochronous duration
- Explicit Engineering/True basis metadata for stress/strain datasets
- Dedicated builders for stress-strain, creep, and external-pressure data
- Explicit `Result` errors for checked creep calculations and library construction
- Versioned full-material JSON serialization
- Packaged `ASME_Materials.db` SQLite database as a NuGet content file
- Adaptive Kachanov integration with convergence and rupture metadata
- Explicit creep-table provenance and model-applicability warnings

## Creep Model Warning

The user must explicitly select the applicable creep model. Norton and Garofalo
do not represent a complete primary-secondary-tertiary curve. The current
Kachanov-Omega implementation neglects primary creep and represents secondary
and damage-driven tertiary behavior. Every generated `CreepTable` carries an
`ApplicabilityWarning`.

## Install

```bash
dotnet add package MaterialLibrary --version 1.0.2
```

## Packaged Data

The package includes `contentFiles/any/any/data/ASME_Materials.db`, plus the XML data files used by the library. The default configuration resolves the ASME SQLite database as `ASME_Materials.db`.

## Quick Example (F#)

> **Warning:** API 579-1/ASME FFS-1 Annex 10B.5 generation is not implemented.
> `Api579Annex10B5.ensureImplemented()` returns an error and the method must not be
> used for engineering calculations until its licensed source equations are validated.

```fsharp
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

let cpTable =
    [ { Temperature = 20.0;  SpecificHeat = 477.0 }
      { Temperature = 100.0; SpecificHeat = 500.0 }
      { Temperature = 200.0; SpecificHeat = 520.0 } ]

match SpecificHeatInterpolation.interpolate Linear 150.0 cpTable with
| Ok cp -> printfn "Cp @ 150degC = %.1f J/(kg*K)" (float cp)
| Error e -> printfn "Interpolation error: %A" e
```

## Repository

- Source: https://github.com/GianFossi/Materials
- Documentation index: https://github.com/GianFossi/Materials#documentation
- Requirements and installation: https://github.com/GianFossi/Materials/blob/main/docs/installation.md
- ASME_Materials.db schema: https://github.com/GianFossi/Materials/blob/main/docs/asme-materials-db.md
- Library API: https://github.com/GianFossi/Materials/blob/main/docs/library-api.md
- Material models and tables: https://github.com/GianFossi/Materials/blob/main/docs/material-models.md
- Excel add-in: https://github.com/GianFossi/Materials/blob/main/docs/excel-addin.md
- Desktop app: https://github.com/GianFossi/Materials/blob/main/docs/desktop-app.md

