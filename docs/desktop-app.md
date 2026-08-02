# Desktop App

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
- A **database manager** ("Database...") over an ASME `ASME_Materials.db`: see below.

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

### CRUD app examples | Esempi nell'app CRUD

Example 1 - modify material data:

1. Open the app and load an existing library with **Open Library...**, or use **Database...** to import
   materials from an ASME database into the current library.
2. Select the material row to revise.
3. Use **Edit Material...** to change identity and scalar fields such as specification, grade, product
   form, family, basic properties, maximum allowable temperatures, welding numbers, and notes.
4. Use **Edit Tables...** for tabular data:
   - To revise the complete elastic modulus table, open the **Elastic modulus** page, edit the
     temperature/modulus/Poisson-ratio rows, use **Add row** or **Delete row** where needed, then confirm
     with **OK**.
   - To modify one external pressure row, open the **External pressure** page, select the table and point,
     change the selected Factor A / Factor B values, then confirm with **OK**.
   - To add, delete, or modify allowable stress data, open the **Allowable stresses** page, edit the
     temperature/stress rows, add a new row for a new temperature point, or delete the selected row.
5. Save the result with **Save** or **Save JSON As...**. If the data came from a database working copy,
   use the database manager's save/export command for the working database.

Example 2 - create a new material:

1. Click **New Material...**.
2. Fill the material identity fields first: Id, name/specification, grade, product form, UNS, family,
   and any basic properties that are known.
3. Confirm the editor with **OK**. The new material appears in the main **Materials** grid.
4. Select the new material and use **Edit Tables...** to populate physical, strength, external pressure,
   creep, fatigue, or Code Case 2964 data.
5. Save the library.

Example 3 - delete an existing material:

1. Select the material in the **Materials** grid.
2. Click **Delete Material**.
3. Confirm the delete prompt.
4. Save the library after checking that the row was removed.

### Database manager | Gestione database

EN: The manager opens an ASME SQLite database and provisions it. A stock `ASME_Materials.db` has no home for
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

IT: Il gestore apre un database SQLite ASME e lo predispone. Un `ASME_Materials.db` originale non ha tabelle
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


## Logging and diagnostics | Log e diagnostica

EN: The app writes a rolling log to `%LOCALAPPDATA%\MaterialLibrary.CrudApp\logs\crudapp-<date>.log`,
one file per day. Each session starts with a header naming the build configuration, app version, and
OS, so a file sent by a user is self-describing.

- Logging works in **Release**, which is the point of it. The earlier implementation used
  `System.Diagnostics.Debug.WriteLine`, which the compiler removes from Release builds, so an
  exception inside an async command left no trace at all in the configuration users actually run.
- `Information`, `Warning`, and `Error` entries are always recorded. `Error` entries expand the whole
  exception chain, including inner exceptions and stack traces.
- Verbose `Debug`-level tracing is on by default in Debug builds only. To capture it from a stock
  Release build, start the app with `--diagnostic`:

```powershell
.\MaterialLibrary.CrudApp.exe --diagnostic
```

- Three global handlers guarantee nothing escapes unrecorded: UI-thread exceptions (reported to the
  user and survivable), background-thread exceptions, and faulted tasks nobody awaited.
- The logger never throws. If the log file cannot be written the entry still goes to the trace
  listener, and the application continues.

IT: L'app scrive un log giornaliero in `%LOCALAPPDATA%\MaterialLibrary.CrudApp\logs\`. Il logging
funziona anche in **Release**; la tracciatura dettagliata si attiva avviando l'app con
`--diagnostic`. Tre gestori globali registrano le eccezioni del thread UI, dei thread in background e
dei task non attesi. Il logger non solleva mai eccezioni.
