# Library API

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

