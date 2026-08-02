# MaterialLibrary

[![NuGet](https://img.shields.io/nuget/v/MaterialLibrary.svg)](https://www.nuget.org/packages/MaterialLibrary)
[![Downloads](https://img.shields.io/nuget/dt/MaterialLibrary.svg)](https://www.nuget.org/packages/MaterialLibrary)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)

EN: Type-safe F# library for engineering material properties and creep analysis on .NET 8.

IT: Libreria F# type-safe per proprieta dei materiali e analisi creep su .NET 8.

This repository is a DLL/NuGet package project (no installer).

Current release:

- NuGet/package version: `1.0.1`
- Assembly/file version: `1.0.1.0`
- Library API and README examples are aligned with the current `src/MaterialLibrary` layout

## Features | Funzionalita

- EN: Material domain model with fixed (a priori) standard units
- IT: Modello dominio materiali con unita standard fissate a priori
- EN: Extended material identity metadata (ProductForm, NominalComposition, Specification, Grade, Class/Condition/Tempering, AlloyIdentification_UNS)
- IT: Metadati identita materiale estesi (ProductForm, NominalComposition, Specification, Grade, Class/Condition/Tempering, AlloyIdentification_UNS)
- EN: Canonical MaterialName composition: Specification + Grade + Class/Condition/Tempering + AlloyIdentification_UNS (non-empty parts only)
- IT: Composizione canonica MaterialName: Specification + Grade + Class/Condition/Tempering + AlloyIdentification_UNS (solo parti non vuote)
- EN: Code-design metadata: maximum allowable temperature by ASME VIII-I / VIII-1 / VIII-2, time-dependent start temperature, and welding P/G numbers
- IT: Metadati di progetto codice: temperatura massima ammissibile per ASME VIII-I / VIII-1 / VIII-2, temperatura di inizio campo dipendente dal tempo, e numeri di saldatura P/G
- EN: Interpolation for specific heat, unified stress-strain, creep, and stress-rupture tables
- IT: Interpolazione per calore specifico, stress-strain, creep, isocrone e stress-rupture
- EN: Creep models (Norton, Garofalo, Kachanov Omega)
- IT: Modelli creep (Norton, Garofalo, Kachanov Omega)
- EN: One creep-table type for database data or explicitly selected model generation
- IT: Un solo tipo di tabella creep per dati da database o generazione con modello selezionato
- EN: Unified external-pressure material tables from a database or Code Case 2964 generation
- IT: Tabelle materiale unificate per pressione esterna, lette da database o generate con Code Case 2964
- EN: Time-independent and isochronous data in one stress-strain table type
- IT: Curve stress-strain indipendenti dal tempo e curve isocrone dipendenti dal tempo
- EN: Dedicated builders for stress-strain, creep, and external-pressure tables
- IT: Builder dedicati per curve stress-strain, isocrone, creep e tabelle di pressione esterna
- EN: Explicit Engineering/True basis metadata on stress/strain-driven datasets
- IT: Metadati espliciti Engineering/True sui dataset basati su stress/deformazione
- EN: Console test suite with end-to-end validations
- IT: Suite test console con validazioni end-to-end
- EN: Excel-DNA add-in exposing material search and property lookups as worksheet functions (see [Excel Add-In](#excel-add-in--componente-aggiuntivo-excel))
- IT: Componente aggiuntivo Excel-DNA che espone ricerca materiali e proprieta come funzioni foglio di calcolo (vedi [Excel Add-In](#excel-add-in--componente-aggiuntivo-excel))
- EN: Windows desktop (WPF) CRUD application for browsing and editing a material library JSON file (see [Desktop CRUD App](#desktop-crud-app--applicazione-desktop-crud))
- IT: Applicazione desktop Windows (WPF) CRUD per sfogliare e modificare un file JSON di libreria materiali (vedi [Desktop CRUD App](#desktop-crud-app--applicazione-desktop-crud))

## Project Layout | Struttura Progetto

- `src/MaterialLibrary` - main DLL project
- `src/MaterialLibrary/builders/StressStrainTableBuilder.fs` - time-independent and isochronous stress-strain builders
- `src/MaterialLibrary/builders/CreepTableBuilder.fs` - validated creep-table construction and model generation
- `src/MaterialLibrary/builders/ExternalPressureTableBuilder.fs` - database and Code Case 2964 table construction
- `src/MaterialLibrary.Crud` - CRUD helpers (repository, table, configuration, and staged-XML-data operations) built on top of `src/MaterialLibrary`
- `src/MaterialLibrary.CrudApp` - WPF desktop application (`MaterialLibrary.CrudApp.exe`) exposing `MaterialLibrary.Crud` through a UI (see [Desktop CRUD App](#desktop-crud-app--applicazione-desktop-crud) below)
- `src/MaterialLibrary.Excel` - Excel-DNA add-in exposing material search and property lookups as worksheet functions (see [Excel Add-In](#excel-add-in--componente-aggiuntivo-excel) below)
- `tests/MaterialLibrary.Tests` - xUnit test project
- `tests/MaterialLibrary.Examples` - compiled usage examples
- `.vscode/tasks.json` - build/test/pack tasks
- `publish/nuget` - generated NuGet packages

## Requirements | Requisiti

- .NET SDK 8.0+ (`net8.0`)
- F# language version: `latest`
- Package dependencies:
  - `FSharp.Core` 10.1.301
  - `Ganfoss.ROP` 1.0.2
  - `Microsoft.Data.Sqlite` 9.0.0
  - `System.Text.Json` 9.0.4

## Standard Units (Fixed A Priori)

All numeric values are handled and persisted using these fixed units:

- Temperature: degC
- Stress / Strength / Allowable stress: MPa
- Time: hours
- Density: kg/m^3
- Specific heat: J/(kg*K)
- Thermal conductivity: W/(m*K)
- Thermal expansion coefficient: 1/degC
- Strain and elongation values: percent when documented as % in table descriptions

These units are also documented directly in XML comments inside the F# source files for Material properties and tables.

Stress/strain datasets also carry explicit basis metadata:

- StressStrainTable: `ReferenceDurationHours`, `StrainBasis`, `StressBasis`, `Source`
- CreepTable: `ReferenceTemperature`, `AppliedStress`, `Source`
- FatigueCurve: `StressBasis`

Isochronous tables use `StressStrainTable.ReferenceDurationHours = Some hours`.

External-pressure tables support both regimes through `ReferenceDurationHours`:

- `None` means time-independent.
- `Some hours` means time-dependent at that reference duration.

## External Pressure Table

There is one external-pressure material-table type:

- `ExternalPressureTable` always stores `Factor A -> allowable compressive stress` in MPa.
- `ExternalPressureTable.Source = MaterialDatabase` identifies database data.
- `ExternalPressureTable.Source = CodeCase2964` identifies Code Case 2964-generated data.
- `ReferenceDurationHours = None` represents a time-independent table.
- `ReferenceDurationHours = Some hours` represents an isochronous table.
- Tables are stored only in `Material.StrengthProperties.ExternalPressureTables`.
- `CompressionProperties` contains compressive strengths only and does not embed another table.

Code Case 2964 can generate either time-dependent or time-independent external-pressure tables. The time regime is a property of the input data and resulting optional duration, not a separate table type.
- `Material.CodeCase2964AppendixIIIConstants` stores reusable Appendix III `A_i` and `B_i` input rows by temperature.
- `Material.CodeCase2964AppendixIIIFactorRule` stores the reusable Appendix III material-family rule for `m2` and `ε′p`.

Stored Code Case 2964 factor evaluation:

- EN: `R = σ_y / σ_ult` is resolved from `TensileProperties` at the assessment temperature when available.
- EN: if no tensile row exists at that temperature, the library falls back to `BasicProperties` using `SMYS / SMUTS`.

Evaluation rule for Code Case 2964 charts:

- EN: because published Code Case 2964 charts use a logarithmic A-axis, prefer log-scale interpolation for `A -> Sc` lookup.
- IT: poiche i diagrammi pubblicati del Code Case 2964 usano un asse A logaritmico, per la ricerca `A -> Sc` e preferibile l'interpolazione su scala logaritmica.

Available query paths:

- `GetCodeCase2964CompressiveStress(...)` for ordinary interpolation on A.
- `GetCodeCase2964CompressiveStressLogA(...)` for interpolation on `log10(A)`.
- `GenerateExternalPressureTableFromStoredCodeCase2964Inputs(...)` to synthesize an `A -> Sc` table.
- `GenerateExternalPressureTableFromStoredCodeCase2964InputsWithCalibrationMode(...)` to synthesize a table with explicit calibration.

Stored-input chart synthesis rule:

- EN: the generated chart is computed from stored `A_i` and `B_i` polynomial coefficient rows plus evaluated factor inputs (`R`, `m2`, `ε′p`) at the requested temperature.
- EN: generated points are sampled on a log-spaced parameter band to align with the logarithmic A-axis behavior used in published Code Case 2964 charts.
- EN: when a stored Code Case 2964 chart already exists at the same `(temperature, referenceDurationHours)`, generation applies an automatic log-domain affine calibration step to improve consistency with that reference baseline.

Additional Code Case 2964 material-family initialization presets:

- `createCodeCase2964StainlessSteelOrNickelBasedAlloyFactorRulePublished()`
- `createCodeCase2964DuplexStainlessSteelFactorRulePublished()`
- `createCodeCase2964FactorRulePublishedByFamily(...)` for a single family-based entry point.

These two published-preset helpers currently return an explicit `InvalidOperation` until validated published rows are wired into this repository.

Calibration comparison workflow:

- EN: generate a chart from stored Appendix III inputs, then compare against reference Figure/Table points using `GetCodeCase2964CompressiveStressLogA(...)` on the same `A` coordinates.
- EN: track `MAPE` and `MaxAPE` as quantitative quality metrics during calibration.
- EN: define explicit acceptance thresholds for `MAPE` and `MaxAPE` in tests so calibration quality regressions fail fast.

Typical usage rule:

- Use `ExternalPressureTableBuilder.createFromDatabase` for database-read A-vs-stress data.
- Use the Code Case 2964 builder functions to generate the same `ExternalPressureTable` type.

## Stress-Strain Curve Models — CC 2964 Appendix I vs ASME VIII.2 §3-D

> **Software warning:** API 579-1/ASME FFS-1 Annex 10B.5 is not implemented.
> Do not use this library to generate isochronous stress-strain tables by that method.
> `Api579Annex10B5.ensureImplemented()` returns an explicit error until the licensed
> equations, tables, and coefficients are available and validated.

Two separate analytical models are implemented in `Domain.StressStrainModels.fs`.
They share the same underlying mathematical framework (Ramberg-Osgood / tanh blending)
but are applied in different design contexts.

### Symbol mapping

| CC 2964 Appendix I | ASME VIII.2-2025 §3-D | Formula |
|---|---|---|
| α₁ = R = σ_ys/σ_ult | R = σ_ys/σ_uts — Eq. 3-D.11 | identical |
| α₂ = m₁ — Eq. I-15 | m₁ — Eq. 3-D.7 | identical |
| α₃ = A₁ — Eq. I-16 | A₁ — Eq. 3-D.6 | identical |
| α₄ = K — Eq. I-17 | K — Eq. 3-D.13 | identical |
| α₅ = σ_ys + K(σ_ult−σ_ys) | reference in H numerator — Eq. 3-D.10 | identical |
| α₆ = K(σ_ult−σ_ys) | K(σ_uts−σ_ys) denominator in H — Eq. 3-D.10 | identical |
| α₇ = m₂ — Eq. I-20 | m₂ — Table 3-D.1 | identical |
| α₈ = A₂ — Eq. I-21 | A₂ = σ_uts exp(m₂)/m₂^m₂ — Eq. 3-D.9 | identical |
| σ_t = (1+ε_es)σ_es — Eq. I-22 | σ_t = (1+ε_es)σ_es — §3-D nomenclature | identical |
| tanh argument (2α₅−2σ_t)/α₆ | H = 2(σ_t−reference)/(K(σ_uts−σ_ys)) — Eq. 3-D.10 | sign-equivalent |

### Differences

| Aspect | CC 2964 Appendix I | ASME VIII.2 §3-D |
|---|---|---|
| **Design context** | Time-dependent (creep regime); minimum isochronous curves (0.8× average) | Time-independent tensile stress-strain curve |
| **Strain curve** | Not given — inputs come from isochronous data directly | ε_t = σ_t/E_y + Y₁ + Y₂ given explicitly (Eqs. 3-D.1 to 3-D.4) |
| **Tangent-modulus form** | E_t = 1/(H₁ + H₂ + H₃ + H₄) — Eq. I-10 | E_t = (1/E_y + D₁ + D₂ + D₃ + D₄)⁻¹ — Eq. 3-D.17 |
| **Component count** | Three H terms (elastic + two plastic branches combined) | Four D terms (micro and macro strain branches separated) |
| **ε_p reference** | Table III-2 limits; caller sets EpsilonPrimePlastic = 0 when exceeded | Table 3-D.1 limits; same zeroing rule applies |

### F# types and entry points

| Model | Input type | Result type | Entry point |
|---|---|---|---|
| CC 2964 Appendix I (tangent modulus only) | `CodeCase2964TangentModulusInput` | `CodeCase2964TangentModulusResult` | `CodeCase2964TangentModulusModel.compute` |
| ASME VIII.2 §3-D (strain curve) | `Asme3dStressStrainInput` | `Asme3dStressStrainResult` | `Asme3dStressStrainModel.computeStrain` |
| ASME VIII.2 §3-D.5.1 (tangent modulus) | `Asme3dStressStrainInput` | `Asme3dTangentModulusResult` | `Asme3dStressStrainModel.computeTangentModulus` |

### Table 3-D.1 — m₂ and ε_p parameters by material family

| Material | Temperature limit | m₂ | ε_p |
|---|---|---|---|
| Ferritic steel (incl. carbon, low-alloy, alloy, ferritic, martensitic, iron-based age-hardening SS) | 480 °C (900 °F) | 0.60 (1.00 − R) | 2.0E-5 |
| Stainless steel and nickel-base alloys | 480 °C (900 °F) | 0.75 (1.00 − R) | 2.0E-5 |
| Duplex stainless steel | 480 °C (900 °F) | 0.70 (0.95 − R) | 2.0E-5 |
| Precipitation-hardening, nickel-based austenitic alloys | 540 °C (1,000 °F) | 1.09 (0.93 − R) | 2.0E-5 |
| Aluminum | 120 °C (250 °F) | 0.52 (0.98 − R) | 5.0E-6 |
| Copper | 65 °C (150 °F) | 0.50 (1.00 − R) | 5.0E-6 |
| Titanium and zirconium | 260 °C (500 °F) | 0.50 (0.98 − R) | 2.0E-5 |

## Unified Stress-Strain Tables

There is one stored stress-strain type:

- `ReferenceDurationHours = None`: time-independent material behavior.
- `ReferenceDurationHours = Some hours`: isochronous stress-strain behavior.
- `Source = StressStrainDatabase`: data read from the material database.
- `Source = GeneratedAsmeVIII2Annex3D`: generated using ASME VIII-2 Annex 3-D.
- API 579-1/ASME FFS-1 Annex 10B.5 remains guarded as not implemented.

Both regimes are stored in `Material.StrengthProperties.StressStrainTables`.
Use `GetStressFromStrain` for time-independent data and
`GetStressFromStrainAtDuration` for isochronous data.

## Culture Invariant Number Format

Numeric values persisted to XML and JSON must always use culture-invariant formatting and parsing.

- EN: Always use `CultureInfo.InvariantCulture` when writing or reading numeric values in persisted XML/JSON payloads.
- IT: Usare sempre `CultureInfo.InvariantCulture` durante scrittura e lettura dei valori numerici nei payload XML/JSON persistiti.
- EN: Decimal separator is `.` independently of OS/user locale (for example, `1234.56`).
- IT: Il separatore decimale e `.` indipendentemente dalla lingua locale del sistema (ad esempio `1234.56`).

This rule avoids locale-dependent data corruption or parsing failures when exchanging files across environments.

## Quick Start | Avvio Rapido

Build:

```bash
dotnet build .\src\MaterialLibrary\MaterialLibrary.fsproj
```

Run tests:

```bash
dotnet test .\tests\MaterialLibrary.Tests\MaterialLibrary.Tests.fsproj
```

Compile examples:

```bash
dotnet build .\tests\MaterialLibrary.Examples\MaterialLibrary.Examples.fsproj
```

Pack NuGet:

```bash
dotnet pack .\src\MaterialLibrary\MaterialLibrary.fsproj -c Release -o .\publish\nuget
```

Generated package:

- `publish/nuget/MaterialLibrary.1.0.1.nupkg`

Publish to NuGet:

```bash
dotnet nuget push .\publish\nuget\MaterialLibrary.1.0.1.nupkg --api-key <NUGET_API_KEY> --source https://api.nuget.org/v3/index.json
```

## API Example | Esempio API (F#)

Real example aligned with the current code in `src/MaterialLibrary/Library.fs`, `src/MaterialLibrary/Domain/`, and `src/MaterialLibrary/Interpolations.fs`:

```fsharp
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

let cpTable =
		[ 	{ Temperature = 20.0;  SpecificHeat = 477.0 }
			{ Temperature = 100.0; SpecificHeat = 500.0 }
			{ Temperature = 200.0; SpecificHeat = 520.0 } ]

match SpecificHeatInterpolation.interpolate Linear 150.0 cpTable with
| Ok cp -> printfn "Cp @ 150degC = %.1f J/(kg*K)" (float cp)
| Error e -> printfn "Interpolation error: %A" e
```

MaterialLibrary constructor helpers:

```fsharp
open MaterialLibrary

let lib = MaterialLibrary.empty ()
printfn "Material count = %d" lib.Count
```

## Checked APIs and persistence

- `MaterialLibrary.create` rejects null materials, empty IDs, and duplicate IDs.
- Norton and Garofalo calculations return `Result<_, MaterialError>` for invalid or non-finite inputs.
- Adaptive Kachanov functions report the accepted time step and optional rupture time.
- Complete material and library JSON use schema version `14`.
- `MaterialLibrary.saveToFile`/`loadFromFile`/`loadFromFileComplete` (in `Library.fs`) read/write a whole
  `MaterialLibrary` instance as one JSON file, wrapping `MaterialLibrarySerialization` so callers do not
  need to convert to/from `Material list` themselves.
- `Configuration.resolveAsmeDatabasePath`/`resolveEnDatabasePath` resolve the default database paths
  (configuration file if present, else a sibling `.sqlite`/`.db` file); `checkFileAccessible` and
  `AsmeMaterialRepository.checkAccessible` confirm a path is not just present but actually openable
  (and, for the ASME path, a valid SQLite database) before use.
- `Configuration.setGeneralOptions`/`setInterpolationOptions`/`setCreepDefaults`/`setIoOptions`/
  `setInterpolationSection`/`setDatabaseFolder`/`set{Asme,En}DatabaseFileName` return an updated,
  immutable copy of a `LibraryConfiguration`; `Configuration.updateAndSave` loads-or-creates, applies
  one of these updates, validates, and saves in one call.

### Database Lookup

Search and filtering logic is located in `src/MaterialLibrary/Database.Lookup`.

- Optional criteria use AND semantics and support exact or contains matching.
- `findMany` returns deterministic ID ordering.
- `findUnique` rejects missing and ambiguous results.
- `SA-5116` is accepted as an alias for database specification `SA-516`.
- S1 maps to Division 1, S2 maps to Division 2, and S3 maps to bolting.
- G5 paired Division 1 curves are separated into normal `S1` and high `S1H` allowable-stress sources.
- `Material.AllowableStressLevel` identifies the selected standard/high case for Section I and VIII-1 only; VIII-2 continues to use its independent S2 data.
- `Material.ApplicableAsmeCodes` explicitly lists Section I, VIII-1, and VIII-2 applicability.
- `RequestedMaterialLibrary` loads the requested plates, tubes, and bolting; TP304 is returned as separate standard and high material instances.

## Creep Table Model Selection

`CreepTable` is the only stored time-versus-creep-strain table. Its temperature,
applied stress, `Source`, and `ApplicabilityWarning` identify the conditions and
the user-selected origin:

- `CreepDatabase`: values read from the material database; source phase coverage must be verified.
- `GeneratedNortonPowerLaw`: empirical power-time behavior; not a complete three-stage model.
- `GeneratedGarofalo`: empirical stress/power-time behavior; not a complete three-stage model.
- `GeneratedKachanovOmega`: secondary creep followed by damage-driven tertiary creep; primary creep is neglected.

The library does not automatically select a model. Callers must explicitly use
`generateWithNorton`, `generateWithGarofalo`, or `generateWithKachanovOmega`
after verifying applicability for the material, temperature, stress, and creep phase.

Garofalo table generation applies the Arrhenius term `exp(-Q/(R*T))` using
temperature in degC and activation energy in J/mol. The lower-level
`GarofaloModel.creepStrain` overload is only for an effective `A` coefficient
already calibrated at the assessment temperature.
- Repeated lookups on an unchanged `PropertyTable` reuse cached validation.

## Excel Add-In | Componente Aggiuntivo Excel

EN: `src/MaterialLibrary.Excel` is an [Excel-DNA](https://excel-dna.net/) add-in (`ExcelDna.AddIn` 1.9.0,
`net8.0-windows`) that exposes material search and property lookups as native Excel worksheet
functions. It is a thin, stateful wrapper around the pure `MaterialLibrary` API: all mutable state
(the loaded materials) lives in one cache module in this project, never in the core library.

IT: `src/MaterialLibrary.Excel` e un componente aggiuntivo [Excel-DNA](https://excel-dna.net/)
(`ExcelDna.AddIn` 1.9.0, `net8.0-windows`) che espone la ricerca materiali e le proprieta come
funzioni native di Excel. E un wrapper sottile e stateful sull'API pura di `MaterialLibrary`: tutto
lo stato mutabile (i materiali caricati) vive in un unico modulo cache in questo progetto, mai nella
libreria principale.

### Building and loading | Compilazione e caricamento

```powershell
dotnet build src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj -c Release
```

This produces `MaterialLibrary.Excel-AddIn-packed.xll` (32-bit) and `MaterialLibrary.Excel-AddIn64-packed.xll`
(64-bit) under `src/MaterialLibrary.Excel/bin/Release/net8.0-windows/publish/`, each a self-contained
package including the managed dependencies and the native SQLite provider. In Excel: File > Options >
Add-ins > Manage: Excel Add-ins > Go... > Browse..., then pick the `.xll` matching your Excel bitness
(File > Account > About Excel shows 32-bit/64-bit).

Data sources are resolved automatically on load, via `Configuration.resolveAsmeDatabasePath` in the
core library:

- The ASME SQLite database, from `MaterialLibrary.config.xml` next to the add-in if present, otherwise
  `ASME_Material_DB.sqlite` next to the add-in. This supplies identity, basic properties, and
  allowable-stress datasets.
- Optionally, a complete material-library JSON file (produced by `MatSaveLibrary` or
  `MaterialLibrarySerialization.saveToFile`) loaded explicitly via `MatOpenJsonLibrary`, supplying the
  richer curve data (stress-strain, creep, fatigue, stress-rupture, cyclic, external-pressure, Code
  Case 2964) that the SQLite database does not store. When a material ID exists in both sources, the
  JSON-sourced record is used.

### Worksheet functions | Funzioni foglio di calcolo

- `MatOpenDatabase(path)`, `MatOpenJsonLibrary(path)`, `MatLibraryStatus()` - (re)load data sources and report what is loaded.
- `MatSearch(specification, grade, productForm, classConditionTemper, uns, family)` - spills matching material IDs and identity columns; every argument is optional.
- `MatDescribe(materialId)` - formatted identity and data-inventory summary.
- Persistence (`MaterialLibrary` category): `MatSaveMaterial`/`MatLoadMaterial` (one material JSON file), `MatSaveLibrary` (every currently loaded material to one JSON library file).
- Configuration (`MaterialLibrary.Config` category): `MatConfigPath`, `MatDefaultAsmeDatabasePath`/`MatDefaultEnDatabasePath` (read default paths); `MatCheckFileAccessible`/`MatCheckDatabaseAccessible` (existence + actually-openable checks); `MatConfigTable` (read every configuration value); `MatConfigSetDatabasePaths`/`MatConfigSetGeneralOptions`/`MatConfigSetInterpolationMode`/`MatConfigSetCreepDefaults` (targeted read-modify-write updates, blank arguments keep the current value).
- Physical properties (`MaterialLibrary.Physical` category): `MatBasicPropertiesTable`, `MatDensity`/`MatDensityTable`, `MatElasticModulus`/`MatPoissonRatio`/`MatShearModulus`/`MatElasticModulusTable`, `MatSpecificHeat`/`MatSpecificHeatTable`, `MatThermalExpansion`/`MatThermalExpansionTable`/`MatThermalExpansionReferenceTemperature`, `MatThermalConductivity`/`MatThermalConductivityTable`.
- Strength properties (`MaterialLibrary.Strength` category): tensile/compression vs temperature, allowable stress vs temperature and size, stress-strain (time-independent and isochronous), cyclic strain-strain (amplitude and hysteresis-range tables), external pressure (database and Code Case 2964, Factor A vs Factor B), creep (experimental curves, stored Norton/Garofalo/Kachanov-Omega models, and average/minimum reference tables for both rupture-at-fixed-duration and stress-at-fixed-creep-rate), stress-rupture, fatigue (S-N, stress amplitude Sa), Larson-Miller, and Code Case 2964 Appendix III data/factor evaluation.
- Interpolation (`MaterialLibrary.Interpolation` category): `MatInterpolate`/`MatCubicSplineInterpolate`/`MatLagrangeInterpolate` - generic interpolation over arbitrary (x, y) ranges pasted into the sheet, reusing the same spline/Lagrange math as the core library's `PropertyTableMath`; `MatTemperatureGrid` - ASME preset or custom temperature grids; `MatStressFromStrainMode`/`MatCreepStrainFromCurveMode`/`MatStressRuptureMode` - mode-aware (CubicSpline/Lagrange-capable) counterparts of the always-linear `MatStressFromStrain`/`MatCreepStrainFromCurve`/`MatStressRupture`.

Every function returns either the requested value/table or a short `"#N/A ..."` / `"#VALUE! ..."` text
explaining why the lookup failed (missing material, no data at that condition, out-of-range query,
ambiguous selection), instead of a bare Excel error code. Interpolated-value functions default to
linear interpolation; most also accept an optional `mode` argument (`Linear`, `CubicSpline`,
`Constant`, `Lagrange`).

EN: Not yet automatically verified: the packaging step above succeeds and produces valid `.xll` files,
but this environment cannot open Excel, so actual worksheet-function registration and behavior inside
Excel have not been interactively tested. Please verify by loading the add-in and calling a few
functions before relying on it.

IT: Non ancora verificato automaticamente: il passo di packaging sopra riesce e produce file `.xll`
validi, ma questo ambiente non puo aprire Excel, quindi la registrazione effettiva delle funzioni e il
comportamento in Excel non sono stati testati interattivamente. Verificare caricando il componente
aggiuntivo e richiamando alcune funzioni prima di farvi affidamento.

## Desktop CRUD App | Applicazione Desktop CRUD

EN: `src/MaterialLibrary.CrudApp` is a Windows-only WPF desktop application (`net8.0-windows`) that wraps
`src/MaterialLibrary.Crud`'s `MaterialCrudRepository` in a UI. It is written in **C# with XAML and MVVM**,
because WPF's data binding, commands, and code-behind model are designed around C# and around mutable,
change-notifying view models that F# records cannot provide. It supports:

- Creating a new, empty in-memory library, or opening/saving a material library JSON file (the same
  format produced by `MaterialLibrary.saveToFile`/`loadFromFileComplete`).
- Listing every material's identification in the library: Id, Specification, Grade, Class/Condition/Tempering,
  UNS, Form, Product analysis (the domain's `NominalComposition`), Family (ASME code), and the composed Full name.
- Creating, editing (identity fields, ASME `Family`, `BasicProperties`, and notes), and deleting a material.
- Editing the property tables of a material ("Edit Tables..."). Twelve tables: thermal expansion, elastic
  modulus, density, specific heat, thermal conductivity, tensile properties, allowable stresses, compression
  properties, Norton power-law creep, Garofalo creep, Kachanov omega creep, and Code Case 2964 Appendix III.
  Column headers carry the fixed unit of measure; optional columns are marked `*` and a blank cell is stored
  as the F# `None`. Writes go through `MaterialLibrary.Crud`'s own helpers, so domain rules (sort by
  temperature, refresh `LastModified`) are applied by the library, not reimplemented in the UI.
- Reading and writing both a single material and a whole library as **XML**, alongside the JSON format.
- Importing a staged XML data file into the selected material ("Import XML data file...").
- A **database manager** ("Database...") over an ASME `asme_materials.db`: see below.

IT: `src/MaterialLibrary.CrudApp` e un'applicazione desktop WPF solo Windows (`net8.0-windows`) che
espone `MaterialCrudRepository` di `src/MaterialLibrary.Crud` tramite un'interfaccia utente. E scritta
in **C# con XAML e MVVM**, perche il data binding, i comandi e il modello code-behind di WPF sono
progettati attorno a C# e a view model mutabili con notifica di modifica, che i record F# non possono
fornire. Supporta:

- Creazione di una nuova libreria vuota in memoria, oppure apertura/salvataggio di un file JSON di
  libreria materiali (lo stesso formato prodotto da `MaterialLibrary.saveToFile`/`loadFromFileComplete`).
- Elenco dell'identificazione di ogni materiale nella libreria: Id, Specification, Grade, Class/Condition/Tempering,
  UNS, Form, Product analysis (il campo `NominalComposition` del dominio), Family (codice ASME) e Full name composto.
- Creazione, modifica (campi identita, `Family` ASME, `BasicProperties` e note) ed eliminazione di un materiale.
- Modifica delle tabelle numeriche di un materiale ("Edit Tables..."): dilatazione termica, modulo elastico,
  densita, calore specifico, conducibilita termica, proprieta a trazione, tensioni ammissibili e proprieta a
  compressione. Le intestazioni riportano l'unita di misura fissa; le colonne opzionali sono marcate `*` e una
  cella vuota viene salvata come `None` di F#.

Build and run | Compilazione ed esecuzione:

```powershell
dotnet build src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.csproj
dotnet publish src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.csproj -c Release -r win-x64 --self-contained false -o publish/crud-app
# then run publish/crud-app/MaterialLibrary.CrudApp.exe
```

### Database manager | Gestione database

EN: The manager opens an ASME SQLite database and provisions it. A stock `asme_materials.db` has no home for
most of the `Material` object - no density rows, tensile rows, compression properties, ASME family, welding
numbers, maximum allowable temperatures, creep models, or Code Case 2964 data - so the application **creates
the missing tables** and links them to the existing `Materials` table:

- 11 tables are created on demand (`CREATE TABLE IF NOT EXISTS`, so provisioning is idempotent), each with
  `MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE` and a covering index.
- Rows are stored in normalized long form, one row per temperature, rather than the legacy pivoted
  `T_40 ... T_900` layout, because the domain models these tables as `(temperature, value)` lists and the
  legacy temperature grids differ per table.
- Each material is persisted twice on purpose: the scalar identity into the ASME `Materials` row and the
  tabular data into the extension tables, so the values stay queryable with ordinary SQL; and the complete
  material into `MaterialDocumentStore` as its canonical JSON, which is the source of truth on read. That
  document is what guarantees tables with no dedicated schema - creep models, stress-strain curves, fatigue
  curves - survive a round trip without loss.
- **The file you pick is never written to.** Opening a database copies it to a `.working.db` beside the
  original and every operation targets the copy; "Save Working Copy As..." is the only route back to a
  permanent file.
- Materials that exist only in the shipped ASME rows show as `ASME reference`; ones written by the
  application show as `Application` and can be read back in full.

IT: Il gestore apre un database SQLite ASME e lo predispone. Un `asme_materials.db` originale non ha tabelle
per gran parte dell'oggetto `Material`, quindi l'applicazione **crea le tabelle mancanti** e le collega alla
tabella `Materials` esistente tramite chiavi esterne con `ON DELETE CASCADE`. Il file selezionato non viene
mai modificato: si lavora sempre su una copia `.working.db`.

### F#/C# interop notes | Note di interoperabilita F#/C\#

EN: The app consumes an F# domain from C#, so all F#-specific representations are confined to
`src/MaterialLibrary.CrudApp/Interop/`. The constraints that shaped the design:

| F# construct | How C# sees it | How the app handles it |
| --- | --- | --- |
| `Material` record (immutable, get-only) | Class with a 23-argument positional constructor, no setters | `MaterialFactory` emulates `{ record with ... }` via that constructor, in one file so a new field breaks the build instead of silently mis-assigning |
| `'T option` | `FSharpOption<T>`, where **`None` is a null reference** | `FSharpInterop` maps `None` <-> `null`; option-typed values are nullable-annotated so the compiler flags unchecked access. Never use `?? fallback` on an option: `None` is null, so the fallback fires when clearing a field |
| `Result<'T, 'TError>` | `FSharpResult<T, TError>`; reading the wrong branch throws | `FSharpInterop.TryUnwrap` exposes it as a try-pattern |
| `'T list` | `FSharpList<T>`: `IEnumerable<T>`, but O(n) indexing and no change notification | Projected into `ObservableCollection<MaterialRowViewModel>` for binding |
| Discriminated unions (`MaterialError`) | Nested subclasses; payloads named `Item`, `Item1`, `Item2`; nullary cases only via `IsX` | `MaterialErrorFormat` uses a C# type-pattern `switch` with explicit default arms (C# cannot check exhaustiveness) |
| Module `Material` beside type `Material` | Compiled class is renamed **`MaterialModule`** | Called as `MaterialModule.create(...)`; the record argument comes **last**, mirroring F# pipeline order |
| Type `MaterialLibrary.MaterialLibrary` | Shadows the `MaterialLibrary` namespace in nested namespaces | App CLR namespace is `MaterialLibraryCrudApp`, not `MaterialLibrary.CrudApp`, otherwise WPF-generated code fails with CS0426 (assembly name is unchanged) |

IT: L'applicazione consuma un dominio F# da C#, quindi tutte le rappresentazioni specifiche di F# sono
confinate in `src/MaterialLibrary.CrudApp/Interop/`. I vincoli principali: i record F# sono immutabili e
senza setter (nessun equivalente di `{ record with ... }` in C#), `None` e un riferimento null, i moduli
F# omonimi di un tipo vengono rinominati con il suffisso `Module`, e il tipo `MaterialLibrary.MaterialLibrary`
oscura il namespace `MaterialLibrary`: per questo il namespace CLR dell'app e `MaterialLibraryCrudApp`.

EN: The CRUD app includes master/detail editors for stress-strain, creep, stress-rupture, fatigue,
cyclic strain, external pressure, and Larson-Miller curves. The remaining physical/strength/special
tables continue to use the typed `MaterialLibrary.Crud` API.

IT: L'app CRUD include editor master/detail per curve stress-strain, creep, stress-rupture, fatica,
deformazione ciclica, pressione esterna e Larson-Miller. Le restanti tabelle fisiche/di resistenza/
speciali continuano a usare l'API tipizzata di `MaterialLibrary.Crud`.

## Release and safety notes | Note di rilascio e sicurezza

- The repository test fixture `tests/Fixtures/asme_materials.working.db` is a read-only copy of the
  supplied working database. Tests must copy it to a temporary path before any mutation; never edit
  the fixture in place.

- Opening `reference.db` creates `reference.working.db`; the selected reference file is never written.
- Modified working databases are persisted/exported only through **Save Working Copy As...**.
- Automatic backups are created beside the working database before destructive SQL/schema operations.
- The backup manager retains the newest generated backups; backup files should be included in release
  support instructions, not copied into source control.
- Raw-table undo/redo is transaction-based and persisted for supported rowid and primary-key tables.
- SQL/schema undo is restricted to recognized `CREATE TABLE`, `CREATE INDEX`, and table-rename forms;
  arbitrary SQL remains audited but is not automatically reversible.
- Foreign-key checks run before raw-table commits and before publishing.
- Excel publishing produces packed `.xll` add-ins for both 32-bit and 64-bit Excel targets. The
  packaged add-in contains managed dependencies and the SQLite native runtime; it must be tested on
  the installed Excel bitness before deployment.
- Native cancellation of an already-running SQLite command remains dependent on provider support;
  cancellation is guaranteed for pending application operations.

## Dependencies | Dipendenze

- `Ganfoss.ROP` 1.0.2 (validated in this repository)
- `System.Text.Json` 9.0.4
- `ExcelDna.AddIn` 1.9.0 (`src/MaterialLibrary.Excel` only)

## Notes | Note

- Target framework: `net8.0`
- F# language version: `latest`
- Output: DLL + NuGet package
- EN: Treat NuGet warnings as release blockers when publishing
- IT: Considera i warning NuGet come blocco rilascio prima della pubblicazione

## AI Instructions

Project instruction files:

- [.claude/CLAUDE.md](.claude/CLAUDE.md)
- [.codex/INSTRUCTIONS.md](.codex/INSTRUCTIONS.md)
- [.github/copilot-instructions.md](.github/copilot-instructions.md)

Contributor and memory files:

- [CONTRIBUTING.md](CONTRIBUTING.md)
- [AI_HISTORY.md](AI_HISTORY.md)

## License

MIT
