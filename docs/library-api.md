# API della libreria

Questa guida distingue **lettura nativa SQLite** e **modello Material/documenti**.
Per lo schema vedere [database](asme-materials-db.md); per l'interfaccia utente vedere
[app desktop](desktop-app.md).

## 1. Leggere il database nativo

[Asme2025Data](../src/MaterialLibrary/Database.Lookup/Asme2025Data.fs) espone:

| Funzione | Uso |
|---|---|
| `openRead path` | Apre un database esistente in sola lettura; il chiamante deve chiudere la connessione |
| `isNormalized db` | Riconosce lo schema nativo |
| `materials db` | Elenca le identità Main |
| `curves db materialId code` | Restituisce tutte le varianti della famiglia |
| `lookupValue db materialId code temperature size productForm curveId` | Valore esatto/interpolato nella variante selezionata, con errore su assenza, ambiguità o fuori campo |
| `families` | Codici e unità fisse |

Esempio F# per ispezionare i dati senza scegliere arbitrariamente una curva:

```fsharp
open MaterialLibrary.Domain.Database.Lookup

let inspectDatabase path =
    use db = Asme2025Data.openRead path
    for material in Asme2025Data.materials db do
        printfn "%d | %s | %s | %s | %s"
            material.Id material.Specification material.Grade material.Condition material.UNS

inspectDatabase @"database\data\materials_2025.db"
```

Il percorso è quello del repository; a runtime il riferimento distribuito è normalmente
`data/materials_2025.db`. Gestire le eccezioni di apertura al confine I/O dell'applicazione.
Per il lookup passare una selezione verificata:

```fsharp
let readSelectedCurve db materialId code temperatureC sizeMm productForm curveId =
    Asme2025Data.lookupValue db materialId code temperatureC
        (Some sizeMm) (Some productForm) (Some curveId)
```

I parametri ID/dimensione/forma provengono dalla selezione della variante, non da valori
di esempio da copiare in un calcolo. `Result.Error` va mostrato/gestito, non sostituito
con zero. I limiti normativi e le note richiedono verifica anche con CurveId esplicito.
Il lookup non estrapola. Sy/Su/S1/S2/Sb1/Sb2 restano famiglie distinte;
S1 Normal/High è metadato della curva, non una famiglia nativa S1H.

## 2. Modello Material e interpolazione

[Domain](../src/MaterialLibrary/Domain/), [Tables](../src/MaterialLibrary/Tables/) e
[Interpolation](../src/MaterialLibrary/Interpolation/) contengono i tipi e gli algoritmi.
Le loro tabelle non sono una descrizione delle colonne SQLite.

Esempio con dati sintetici:

```fsharp
open MaterialLibrary.Domain
open MaterialLibrary.Interpolation

let cpTable =
    [ { Temperature = 20.0; SpecificHeat = 477.0 }
      { Temperature = 100.0; SpecificHeat = 500.0 }
      { Temperature = 200.0; SpecificHeat = 520.0 } ]

match SpecificHeatInterpolation.interpolate Linear 150.0 cpTable with
| Ok cp -> printfn "Cp = %.1f J/(kg*K)" (float cp)
| Error error -> eprintfn "Interpolation error: %A" error
```

`MaterialLibrary.empty ()` crea una libreria in memoria. `MaterialLibrary.create`
valida materiali null, ID vuoti e duplicati. Gli esempi eseguibili sono nel
[progetto esempi](../tests/MaterialLibrary.Examples/).
Per unità, basi stress/strain e limiti dei modelli vedere [modelli](material-models.md).

## 3. Documenti e configurazione

- [Library.fs](../src/MaterialLibrary/Library.fs) espone
  `MaterialLibrary.saveToFile`, `loadFromFile` e `loadFromFileComplete` per una libreria.
- [MaterialSerialization](../src/MaterialLibrary/Serialization/MaterialSerialization.fs)
  gestisce il singolo materiale completo: schema JSON **17**, con **15/16** leggibili.
- Un documento completo conserva il modello Material; non ricostruisce automaticamente
  tutta la provenienza/varianti del database nativo.
- [Configuration](../src/MaterialLibrary/Configuration.fs) gestisce
  `MaterialLibrary.config.xml`, aggiornamenti immutabili e `updateAndSave`.
  `resolveAsmeDatabasePath` risolve il riferimento configurato/predefinito, non crea
  una copia di lavoro.
- I percorsi nativi non attingono automaticamente all'archivio 2023.

Le API di ricerca in [Database.Lookup](../src/MaterialLibrary/Database.Lookup/)
offrono criteri combinati e ricerca univoca. Per preservare tutte le varianti e scegliere
una curva nativa usare `Asme2025Data`, non un documento legacy appiattito.
La proiezione al modello Material ha limiti: una famiglia fisica con più curve non viene
scelta automaticamente, e i minimi scalari `NULL` diventano `0` nei campi numerici
SMYS/SMUTS. Non interpretare questo zero come valore normativo; verificare i dati nativi.

## 4. Scrittura e importazione

Il pacchetto [MaterialLibrary.Crud](../src/MaterialLibrary.Crud/README-NUGET.md) fornisce:

- `Asme2025Crud`: copia di lavoro, salvataggio identità/curve/costanti;
- `XmlPropertyReader` / `XmlPropertyMatching`: lettura e proposte di associazione;
- `XmlPropertyImport.apply`: lotto di associazioni approvate in transazione;
- `WorkingCopyHardening`: rafforzamento idempotente delle copie di lavoro (applicato da `Asme2025Crud.ensureSchema`), `integrityReport` (rapporto delle incoerenze), `removeEmptyCurves` (cancella le curve senza punti);
- `SqlAudit`: registro opzionale delle modifiche fatte con SQL diretto (`enable`, `read`, `disable`, `purgeOlderThan`);
- `ProductSheet.build`: scheda prodotto Markdown di un materiale (`Result<string,string>`), con colonne Room, Design (max 3) e Operating (max 3), tutte le dimensioni e tutte le note; le tabelle sono allineate nel sorgente Markdown (padding delle colonne, resa invariata); colonne di temperatura con codice colore, unità sotto la tabella, note elencate sotto ogni tabella;
- `SourceRowComparison`: affianca una curva alla riga ASME stampata (`compare`, `findInconsistent`);
- dettagli, esempi e limiti in [database §13](asme-materials-db.md#13-rendere-larchitettura-più-robusta);
- `MaterialCrudRepository`: CRUD di oggetti Material in memoria;
- `MaterialDatabaseCrud`: percorso generico/legacy, con protezioni specifiche per il nativo.

Scritture native e importazioni richiedono `.working.db`. Il matching XML non scrive
e non certifica applicabilità. Vedere [flusso XML](xml-physical-import.md).
L'esportazione di un documento legacy sopra una curva nativa viene rifiutata.
