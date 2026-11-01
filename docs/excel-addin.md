# Excel: installazione e uso dei dati ASME

L'add-in [MaterialLibrary.Excel](../src/MaterialLibrary.Excel/) usa Excel-DNA e
`net10.0-windows`. Espone funzioni di ricerca/lettura e lo stesso editor WPF dell'app.
Richiede Excel desktop Windows e .NET 10 **Desktop Runtime** dell'architettura di Excel.

## Compilare e caricare

Dalla radice del repository:

```powershell
dotnet build .\src\MaterialLibrary.Excel\MaterialLibrary.Excel.fsproj -c Release
```

Il build produce nella cartella
`src\MaterialLibrary.Excel\bin\Release\net10.0-windows\publish\`:

| Excel | XLL da caricare |
|---|---|
| 32 bit | `MaterialLibrary.Excel-AddIn-packed.xll` |
| 64 bit | `MaterialLibrary.Excel-AddIn64-packed.xll` |

Le XLL includono le dipendenze gestite e SQLite, **non il runtime .NET**.
Tenere disponibili configurazione e `data/` accanto all'add-in:
la sola XLL non contiene il database e i dataset XML come file esterni.
Per una distribuzione mantenere sotto la directory dell'add-in il contenuto dati del build,
senza copiare il database archiviato come ripiego.

In Excel verificare i bit in **File → Account → Informazioni su Excel**.
Poi **File → Opzioni → Componenti aggiuntivi → Gestisci: Componenti aggiuntivi di Excel
→ Vai... → Sfoglia...** e selezionare la XLL corrispondente.
Seguire le policy aziendali per add-in scaricati e percorsi attendibili.

## 1. Scegliere il database

Il default deriva da `MaterialLibrary.config.xml` accanto all'add-in o da
`data/asme_materials_2025.db` nella directory runtime.

```text
=MatOpenDatabase("C:\percorso\asme_materials_2025.working.db")
=MatLibraryStatus()
=MatSearch("SA-516";"70")
```

Il percorso nell'esempio è un segnaposto. `MatLibraryStatus` permette di controllare
quale database/documento sia caricato. `MatSearch` restituisce gli ID; non usare ID
inventati per i calcoli.

Il caricamento iniziale può leggere il riferimento. Quando si apre **Modifica proprietà**
o **Importa XML proprietà / chart**, viene preparata la copia di lavoro; alla chiusura
Excel carica quella copia e ricalcola. Per leggere modifiche locali senza aprire l'editor,
caricare esplicitamente la `.working.db`.

## 2. Leggere tutte le varianti prima del valore

```text
=MatPropertyFamilies()
=MatPropertyTable("123";"S1")
=MatPropertyValue("123";"S1";200;25;"Plate";456)
=MatPropertyTable("123";"Sb1")
=MatPropertyTable("123";"Sb2")
=MatExternalPressureImportedTable("123")
```

`123` e `456` sono segnaposto per Main ID e CurveId effettivi; `;` è il separatore
delle installazioni Excel che lo usano (altrove usare `,`).

| Funzione | Risultato |
|---|---|
| `MatPropertyFamilies()` | Famiglie native e unità fisse |
| `MatPropertyTable(materialId; propertyCode)` | Tutte le varianti/punti: CurveId, forma, dimensioni e inclusività, asse, temperatura, valore, unità, fonte e note |
| `MatPropertyValue(materialId; propertyCode; temperatureC; sizeMm; productForm; curveId)` | Valore della variante selezionata; ultimi tre argomenti opzionali solo quando non necessari alla selezione |
| `MatExternalPressureImportedTable(materialId)` | Diagrammi materiali importati, con CurveId, chart, temperatura, A e B |

Dimensioni in mm, temperature in °C; tensioni ed E in MPa. Le altre unità sono
elencate da `MatPropertyFamilies` e nella [guida database](asme-materials-db.md).
`Alpha` è medio; `AlphaInstantaneous` è istantaneo. `c` è calore specifico, non
l'allungamento C delle tabelle TE.

Un errore di assenza, ambiguità o fuori campo viene restituito come messaggio:
**non viene scelta la prima curva e non si estrapola**. Verificare sempre note e limiti.
Density/Nu costanti sono lette da `Extension` solo quando non esistono curve della famiglia.
Table 3 Sb1 e Table 4 Sb2 non sono intercambiabili.

## 3. Modificare e importare

Nella scheda **Materiali ASME 2025**:

- **Modifica proprietà:** apre l'[editor condiviso](asme-2025-editor.md).
- **Importa XML proprietà / chart:** apre revisione e associazione dei [dataset XML](xml-physical-import.md).
- **Ricarica database:** rilegge il database corrente e ricalcola dopo modifiche esterne.

La copia locale predefinita è
`%LOCALAPPDATA%\MaterialLibrary\ASME2025\asme_materials_2025.working.db`.
Il riferimento non viene modificato. Le copie esistenti non si aggiornano automaticamente:
prima di sostituirle seguire [backup e ripristino](desktop-app.md#6-aggiornamento-e-ripristino).

## Documenti JSON e funzioni preesistenti

`MatOpenJsonLibrary` può caricare una libreria completa di materiali personalizzati e
modelli di calcolo. Nelle funzioni preesistenti, un ID JSON può prevalere sul materiale
caricato da SQLite; `MatLibraryStatus` mostra entrambe le fonti.

Le funzioni native `MatPropertyTable`/`MatPropertyValue` leggono invece il database
corrente direttamente. Non affermare che SQLite non possa contenere curve avanzate:
la disponibilità dipende dal dataset e dal percorso di lettura.
Non usare un JSON come backup completo del database nativo.

## Verifica e rimozione

In VS Code sono disponibili i task **test-excel-demo: x86** e **test-excel-demo: x64**
in [tasks.json](../.vscode/tasks.json). Usano
[Test-ExcelAddIn.ps1](../tools/Test-ExcelAddIn.ps1) con una copia temporanea di
[Demo.xlsx](../tests/MaterialLibrary.Examples/Demo.xlsx), database esplicito e architettura
selezionata. Richiedono Excel installato; non sostituiscono la revisione normativa.

Per rimuovere l'add-in disabilitarlo nell'elenco di Excel, chiudere Excel e poi rimuovere
i file distribuiti. Conservare prima copie di lavoro e documenti personali.
