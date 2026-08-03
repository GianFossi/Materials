# ASME_Materials.db Schema

`ASME_Materials.db` is the SQLite database shipped with the repository and the `MaterialLibrary` NuGet package. It contains the ASME reference material tables plus the application-owned extension tables used by `MaterialLibrary.Crud` and `MaterialLibrary.CrudApp`.

The packaged file is located at:

- Repository: `src/MaterialLibrary/data/ASME_Materials.db`
- NuGet content file: `contentFiles/any/any/data/ASME_Materials.db`

The default configuration now points to `ASME_Materials.db`. The desktop app still works on a copied `*.working.db` file before saving/exporting changes.

## Main Relationship

`Materials.ID` is the central primary key. Most reference tables and all application extension tables link to it through `MaterialID`.

```text
Materials
  |-- YieldStrengthTable
  |-- UltimateStrengthTable
  |-- AllowableStress1Table
  |-- AllowableStress2Table
  |-- AllowableStress3Table
  |-- SpecificHeatTable
  |-- ExternalPressureTable
  |-- DataTableASME
  |-- MaterialGroupMap
  |     |-- ElasticModulusTable
  |     |-- ThermalExpansionTable
  |     |-- ThermalConductivityTable
  |     |-- ThermalDiffusivityTable
  |
  |-- MaterialLibraryExtension
  |-- MaterialDocumentStore
  |-- MaterialThermalExpansionRows
  |-- MaterialElasticModulusRows
  |-- MaterialDensityRows
  |-- MaterialSpecificHeatRows
  |-- MaterialThermalConductivityRows
  |-- MaterialTensileRows
  |-- MaterialAllowableStressRows
  |-- MaterialAllowableStressDatasetRows
  |-- MaterialCompressionRows
  |-- MaterialAsmeCodeRows
```

## Reference Tables

These tables are the ASME/reference side of the database.

| Table | Purpose | Key columns |
| --- | --- | --- |
| `Materials` | Base material identity and scalar properties. | `ID`, `Revision`, `NominalComposition`, `ProductForm`, `Specification`, `TypeGrade`, `ClassConditionTemper`, `AlloyDesignationNumber`, `SMTS`, `SMYS`, `Density`, `PoissonFactor`, `Notes` |
| `YieldStrengthTable` | Yield strength values by temperature and size range. | `MaterialID`, `SizeThkMIN`, `SizeThkMAX`, `T_40` ... `T_900` |
| `UltimateStrengthTable` | Ultimate tensile strength values by temperature and size range. | `MaterialID`, `SizeThkMIN`, `SizeThkMAX`, `T_40` ... `T_900` |
| `AllowableStress1Table` | ASME Section I / VIII-1 standard allowable stress data. | `MaterialID`, size limits, maximum-temperature fields, `T_40` ... `T_900` |
| `AllowableStress2Table` | ASME VIII-2 allowable stress data. | `MaterialID`, size limits, maximum temperature, `T_40` ... `T_900` |
| `AllowableStress3Table` | Bolting / S3 allowable stress data. | `MaterialID`, size limits, maximum-temperature fields, `T_40` ... `T_900` |
| `SpecificHeatTable` | Specific heat by material and temperature. | `MaterialID`, `T_20` ... `T_900` |
| `ExternalPressureTable` | External-pressure factor data by material and temperature. | `MaterialID`, `T_40` ... `T_900` |
| `DataTableASME` | Welding numbers and ASME metadata. | `MaterialID`, `Pnum`, `PnumSizeThk`, `Gnum` |
| `MaterialGroupMap` | Maps a material to shared physical-property groups. | `MaterialID`, `ElasticModulusGroupID`, `ThermalExpansionGroupID`, `ThermalConductivityGroupID`, `ThermalDiffusivityGroupID` |
| `ElasticModulusTable` | Shared elastic modulus group values. | `ID`, `T_-200` ... `T_900` |
| `ThermalExpansionTable` | Shared thermal expansion group values. | `ID`, `T_20` ... `T_900` |
| `ThermalConductivityTable` | Shared thermal conductivity group values. | `ID`, `T_20` ... `T_900` |
| `ThermalDiffusivityTable` | Shared thermal diffusivity group values. | `ID`, `T_20` ... `T_900` |
| `NotesDatabase` | Reusable notes keyed by source table and note code. | `ID`, `SourceTable`, `NoteCode`, `NoteText` |
| `AssociationRules` | Optional rule table for property-table association logic. | `ID`, `PropertyTable`, `MatchField`, `MatchValue`, `GroupID`, `RuleName` |
| `sqlite_sequence` | SQLite autoincrement bookkeeping. | `name`, `seq` |

Temperature columns named `T_40`, `T_100`, and similar are pivoted temperature columns. Temperatures are in degC and stress values are in MPa unless a table-specific source note says otherwise.

## Application Extension Tables

The application creates these tables with `CREATE TABLE IF NOT EXISTS`. They are normalized, one row per temperature or item, and every table references `Materials(ID)` with cascade delete.

| Table | Purpose | Columns |
| --- | --- | --- |
| `MaterialLibraryExtension` | One scalar extension row per material. | `MaterialID`, `MaterialKey`, `Name`, `Family`, `AllowableStressLevel`, maximum allowable temperatures, welding numbers, reduction of area, thermal-expansion reference temperature, timestamps |
| `MaterialDocumentStore` | Lossless full-material JSON payload. | `MaterialID`, `Format`, `SchemaVersion`, `Payload`, `LastModified` |
| `MaterialThermalExpansionRows` | Normalized thermal expansion rows. | `MaterialID`, `Temperature`, `ExpansionCoefficient` |
| `MaterialElasticModulusRows` | Normalized elastic modulus rows. | `MaterialID`, `Temperature`, `ElasticModulus`, `PoissonRatio` |
| `MaterialDensityRows` | Normalized density rows. | `MaterialID`, `Temperature`, `Density` |
| `MaterialSpecificHeatRows` | Normalized specific heat rows. | `MaterialID`, `Temperature`, `SpecificHeat` |
| `MaterialThermalConductivityRows` | Normalized thermal conductivity rows. | `MaterialID`, `Temperature`, `Conductivity` |
| `MaterialTensileRows` | Legacy projection of the old flat tensile list. Defined for compatibility; **no longer written**, because Sy and Su are now 2D tables held in the material document. | `MaterialID`, `Temperature`, `YieldStrength`, `TensileStrength` |
| `MaterialAllowableStressRows` | Legacy projection of the old service-level list. Defined for compatibility; **no longer written**. | `MaterialID`, `Temperature`, `SectionIServiceLevelA`, `SectionIServiceLevelB`, `SectionIServiceLevelC`, `SectionIServiceLevelD`, `SectionIIWeld` |
| `MaterialAllowableStressDatasetRows` | ASME allowable stresses, one row per (division, case, size band, temperature). | `MaterialID`, `Division` (`VIII-1`/`VIII-2`/`Bolting`), `StressCase` (`Normal`/`High`), `SizeMinimum`, `SizeMaximum`, `MaximumTemperature`, `CreepTemperature`, `Temperature`, `AllowableStress` |
| `MaterialCompressionRows` | Normalized compression-property rows. | `MaterialID`, `Temperature`, `CompressiveStrength`, `CompressiveYield` |
| `MaterialAsmeCodeRows` | ASME code designations linked to a material. | `MaterialID`, `AsmeCode` |

`MaterialDocumentStore` is the canonical full-fidelity read-back source for application-written materials. The scalar and row tables keep common fields queryable from ordinary SQL.

For reference-material hydration, the physical-property loader now follows this order:

1. Use `MaterialGroupMap` group IDs when present (compiled/shared group rows).
2. If the grouped row is missing or empty, fall back to a material-linked row (`MaterialID`) when the table supports it.
3. If needed, fall back to `ID = MaterialID` for legacy variants.

Room-temperature values are preserved explicitly: density and Poisson ratio at `20 degC` are carried forward when available (from grouped room-temperature tables when present, otherwise from scalar reference fields).

`MaterialAllowableStressDatasetRows` repeats its division, case, and size bounds on every row on purpose: one row then answers "which allowable stress applies to this size at this temperature" without a join. Size bounds are in millimetres. Elongation and reduction of area are room-temperature scalars and live on the `Materials` row (`RuptureElongationLong`, `RuptureElongationTransv`) and in `MaterialLibraryExtension` (`ReductionOfAreaPercent`); Sy and Su are 2D tables and live only in the material document.

Schema provisioning also repairs older working copies: a reference column this build writes but the file lacks is added with `ALTER TABLE`, and an application-owned row table left in a superseded shape is dropped so the current definition recreates it. Only application-owned tables are ever dropped, and they are a projection of `MaterialDocumentStore`, which is never touched.

## View

`MaterialSummary` is a read-only summary view. It joins/counts the reference data so callers can inspect which materials have yield strength, ultimate strength, allowable stress, physical-property, external-pressure, and ASME metadata rows.

Important fields include:

- Identity: `ID`, `NominalComposition`, `ProductForm`, `Specification`, `TypeGrade`, `ClassConditionTemper`, `AlloyDesignationNumber`
- Strength counts: `n_Sy`, `n_Su`, `n_S1`, `n_S2`, `n_S3`
- Physical-property counts: `n_E`, `n_TE`, `n_TC`, `n_TD`, `n_Cp`
- Other counts: `n_EP`, `n_ASME`

## Row Counts in the Shipped File

The current packaged database contains:

| Object | Rows |
| --- | ---: |
| `Materials` | 2129 |
| `MaterialGroupMap` | 2129 |
| `YieldStrengthTable` | 2370 |
| `UltimateStrengthTable` | 2339 |
| `AllowableStress1Table` | 2903 |
| `AllowableStress2Table` | 781 |
| `AllowableStress3Table` | 211 |
| `SpecificHeatTable` | 1770 |
| `DataTableASME` | 1213 |
| `NotesDatabase` | 279 |
| Shared physical-property group tables | 19 to 62 rows each |

Application extension tables may be empty in a freshly packaged file. They are populated when materials are written through `MaterialLibrary.Crud` or the desktop app.

## Safety Rules

- Do not edit the packaged database in place.
- The desktop app creates and edits a working copy.
- `MaterialDatabaseSchema.ensureSchema` is idempotent and can be run whenever a connection opens.
- Foreign keys must be enabled per SQLite connection with `PRAGMA foreign_keys = ON`.
- Deleting a `Materials` row cascades to linked reference and extension rows when foreign keys are enabled.
