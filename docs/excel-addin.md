# Excel Add-In

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

VS Code smoke-test tasks are available for the demo workbook:

```text
test-excel-demo: x86
test-excel-demo: x64
```

Each task publishes the add-in, opens `tests/MaterialLibrary.Examples/Demo.xlsx`, registers the matching
packed `.xll`, rewrites the workbook's database formula to the repository `ASME_Materials.db` on a
temporary copy, recalculates, and fails if any formula cell returns an Excel error.

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

