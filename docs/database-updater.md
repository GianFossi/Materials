# Database ASME 2025 e tool di aggiornamento

Il progetto console F# `tools/MaterialLibrary.DatabaseUpdater` crea un database SQLite per edizione oppure confronta gli export ASME metrici con un database esistente. Legge una directory con sottocartelle denominate come le tabelle ASME, oppure un archivio ZIP. **MAIN è sempre l'unione univoca delle identità presenti in tutte le tabelle sorgenti**, comprese quelle di cui non si importano ancora le curve. Le famiglie meccaniche importate sono:

| Proprietà | Export di origine |
| --- | --- |
| Sy | `Sy/*.csv` — Table Y-1 |
| Su | `Su/*.csv` — Table U |
| S1 | unione di `1A/*.csv` e `1B/*.csv` |
| S2 | unione di `5A/*.csv` e `5B/*.csv` |
| Sb1 | `3/*.csv` — ammissibili bolting Table 3 |
| Sb2 | `4/*.csv` — ammissibili bolting Table 4 |

Le cartelle 2A/2B contribuiscono a MAIN e alla provenienza completa, senza essere mescolate a S1/S2 o Sb1/Sb2. Le loro curve non sono ancora importate; i campi normativi ASME vengono invece importati. Lo stesso criterio vale per altre tabelle sorgenti con intestazioni di identità ASME valide. Le tabelle 6A–6D sono completamente ignorate, incluse identità e note. I file non CSV/note vengono elencati come ignorati.

## Esecuzione

Sono sufficienti il .NET SDK 8 previsto da `global.json` e NuGet. Non servono Excel, Windows o servizi esterni.

Nel pacchetto distribuibile la console già compilata è in `console/`. Per usarla basta il runtime .NET 8, senza SDK:

```bash
dotnet console/MaterialLibrary.DatabaseUpdater.dll --help
dotnet console/MaterialLibrary.DatabaseUpdater.dll --sourcedir './ASME Database' --compare --report differenze.txt
```

Dal repository, con SDK:

```bash
dotnet run --project tools/MaterialLibrary.DatabaseUpdater -- --help
```

| Argomento | Comportamento |
| --- | --- |
| `--help` | Mostra guida, valori predefiniti ed esempi senza eseguire importazioni |
| `--sourcedir <directory>` | Directory contenente `1A/`, `1B/`, `5A/`, `5B/`, `Sy/`, `Su/`, ecc.; legge `.csv` e `.cvs` e i rispettivi `notes.txt` |
| `--newdatabasefile [file.db]` | Crea un nuovo database; senza nome usa `asme_materials_<anno>.db`, ad esempio `asme_materials_2025.db` |
| `--compare [file.db]` | Usa il database soltanto per il confronto, senza modificarlo; senza nome usa `asme_materials.db` |
| `--report <Nomefile.txt>` | Salva il report testuale completo UTF-8 anziché stamparlo sul terminale |
| `--edition <anno>` | Facoltativo: anno a quattro cifre; per default è rilevato dall'intestazione degli export, non dalla data corrente |
| `--report-dir <directory>` | Facoltativo: conserva anche CSV, JSON e README dettagliati |

Creazione con nome predefinito e report a video:

```bash
dotnet run --project tools/MaterialLibrary.DatabaseUpdater -- \
  --sourcedir './ASME Database' --newdatabasefile
```

Confronto con `asme_materials.db` nella directory corrente e salvataggio su file:

```bash
dotnet run --project tools/MaterialLibrary.DatabaseUpdater -- \
  --sourcedir './ASME Database' --compare --report differenze.txt
```

Creazione e confronto combinati, con nomi espliciti:

```bash
dotnet run --project tools/MaterialLibrary.DatabaseUpdater -- \
  --sourcedir './ASME Database' \
  --compare precedente.db \
  --newdatabasefile nuovo_2025.db \
  --report differenze_2025.txt \
  --report-dir dettagli_2025
```

Senza `--compare`, la modalità predefinita crea il database. Con il solo `--compare` non viene conservato alcun nuovo database: il confronto usa file temporanei che vengono eliminati al termine. Con `--compare` e `--newdatabasefile` insieme vengono prodotti sia il confronto sia il nuovo database, mantenendo gli ID dei materiali abbinati univocamente.

Senza `--report` il terminale mostra conteggi, differenze fra materiali e note, dettagli di Sy/Su mancanti, anomalie, copertura dimensionale e righe con revisione. Con `--report` lo stesso testo viene scritto nel file e il programma non stampa il report su stdout. Gli errori di esecuzione restano su stderr e restituiscono un codice diverso da zero. Per evitare il normale output di compilazione di `dotnet run`, compilare prima e usare `dotnet run --no-build`, oppure eseguire direttamente la DLL/apphost generata.

File e directory di output già esistenti non vengono sovrascritti. Scegliere **nuovi percorsi** per ripetere una creazione o salvare un nuovo report. I file finali vengono pubblicati solo dopo le verifiche e la generazione dei report.

Restano disponibili gli argomenti precedenti: `--input <archivio.zip>` al posto di `--sourcedir`, `--output <file.db>` come alias di `--newdatabasefile` e `--baseline <file.db>` come alias di `--compare`. `--updatedatabasefile` è sostituito da `--compare` e non è accettato. Non combinare due alias della stessa opzione.

```bash
dotnet test tests/MaterialLibrary.DatabaseUpdater.Tests
```

I file sotto `artifacts/` sono ignorati da Git. Conservare separatamente gli artefatti e l'archivio sorgente quando si desidera distribuirli o riprodurre l'importazione.

## Identità e dati mancanti

La chiave è composta esclusivamente da Specification, Grade, ClassConditionTemper e UNS. Un indice UNIQUE impedisce duplicati anche per campi NULL. ProductForm non appartiene a MAIN: distingue soltanto le curve S1/S2. NominalComposition resta unica per identità; composizioni realmente discordanti sono segnalate in composition-conflicts.csv e lasciate NULL in MAIN, senza creare materiali duplicati. Il tool normalizza maiuscole, trattini tipografici e spazi; non applica abbinamenti approssimativi. Le dimensioni e i minimi di resistenza appartengono alle singole curve, non alla chiave del materiale. Revisioni e campi originali sono conservati come provenienza.

Sono riconosciute sia `Class/Condition/Temper` sia `Class/Cond./Temper`. La provenienza originale conserva Product Form. Le tabelle 3/4 identificano il contesto BOLTING anche quando manca la colonna. I prospetti meccanici separano BOLTING (Sy/Su/Sb1/Sb2) dagli altri product form (Sy/Su/S1/S2). Una stessa identità può essere presente in entrambi i contesti: rimane unica in MAIN, ma compare in entrambi i prospetti. Le assenze sono verificate soltanto per le famiglie applicabili. Una curva senza valori viene conteggiata come riga, ma segnalata come dato mancante.

Per evitare di eliminare materiali con minimi mancanti o variabili per dimensione, `Main.SMYS` e `Main.SMUS` sono nullable, a differenza del primo schema proposto. In `Main` viene salvato un valore soltanto quando è univoco fra le righe sorgente. I valori esatti per riga rimangono sempre in `Property_Source.MinYieldMPa` e `MinTensileMPa`. `SMUS` corrisponde al minimo di resistenza a trazione dell'export, non alla curva Su.

La disponibilità di Sy/Su è determinata dall'esistenza di **valori di curva**, non dalla presenza dei minimi. Ogni riga della tabella è mantenuta come curva distinta, comprese alternative con note diverse e duplicati segnalati. Gli intervalli dimensionali mantengono estremi aperti/chiusi e asse (spessore/non specificato, diametro, OD, DN). Non si interpolano temperature e non si inventano valori.

Una differenza nella condizione può impedire un abbinamento esatto. I report elencano possibili altre identità con la stessa specifica e grado e con lega uguale o non specificata, indicando quali possiedono effettivamente Sy/Su. Questi candidati richiedono una verifica tecnica prima di essere uniti.

## Note e provenienza

Le note hanno chiave **tabella di origine + codice**: `1A:G5`, `1B:G5`, `5A:G1` e `5B:G1` rimangono indipendenti. Il parser conserva note generali, note specifiche, testi su più righe e documenti completi. Nessun contenuto dei documenti viene eseguito o interpretato come istruzione.

| Oggetto SQLite | Contenuto |
| --- | --- |
| `Main`, `Material_Identity` | Materiali e chiavi naturali stabili |
| `Material_Source` | Tutte le righe di tutte le tabelle sorgenti collegate a MAIN, con file, riga, JSON originale e impronta |
| `Sizes`, `Material_Properties`, `Property_Values` | Curve dimensionali, temperatura in °C e tensione in MPa |
| `S1`, `S2` | Viste delle due unioni con provenienza e dimensioni |
| `Sb1`, `Sb2` | Tabelle reali riservate ai bolting, una riga per curva rispettivamente di Table 3 e Table 4; riferimenti a `Material_Properties`, `Property_Source` e valori in `Property_Values` |
| `Property_Source` | File, riga CSV, pagina/linea ASME, minimi, campi originali JSON e SHA-256 della riga |
| `Property_Notes` | Codici grezzi e testo delle note specifiche risolte, con tabella nel tipo |
| `Source_Notes`, `Property_Note_Links` | Definizioni e collegamenti con integrità referenziale |
| `VW_Property_Notes_All` | Note specifiche e generali applicabili a ciascuna curva |
| `ASME_Data` | P-Number, Group Number, diagramma di pressione esterna e limiti per sezioni I/III/VIII-1/VIII-2/XII, quando univoci |
| `ASME_Data_Source` | Ogni campo normativo non mancante, testo originale e temperatura numerica solo se incondizionata, con collegamento alla riga sorgente |
| `Source_Documents`, `Import_Files` | Documenti delle note, conteggi e impronte dei file |
| `Import_Omitted_Rows` | Righe prive di identità e di qualsiasi dato tecnico, mantenute integralmente come evidenza |
| `Database_Metadata` | Edizione, unità, ambito, percorso/tipo sorgente e impronta SHA-256; per directory si usa il manifesto ordinato di percorsi e contenuti CSV/note |
| `VW_Material_Strength_Availability`, `VW_Missing_Strength` | Disponibilità di valori Sy/Su/S1/S2/Sb1/Sb2 e materiali incompleti |
| `VW_Main_Mechanical_Counts` | Ogni riga MAIN con conteggi Sy/Su/S1/S2/Sb1/Sb2 e indicatori di presenza di valori |
| `VW_Main_Physical_Availability` | Ogni riga MAIN con disponibilità effettiva di E, Alpha, c, k, a, density e Nu |

Esempio di lettura delle note di S1:

```sql
SELECT DISTINCT s.id_main, s.id_property, n.SourceTable, n.Code, n.Kind, n.NoteText
FROM S1 s
JOIN VW_Property_Notes_All n ON n.id_property = s.id_property
WHERE s.id_main = @materialId;
```

Il file CSV non conserva la tipografia alternativa usata da ASME per alcuni valori dipendenti dal tempo. Il tool conserva i valori e le note ma non ricostruisce quella distinzione grafica.

## Dati normativi ASME e note

`ASME_Data` contiene una riga per ogni materiale con almeno un campo normativo fornito dagli export. Il tool importa `P-No.`, `Group No.`, `External Pressure Chart No.` e i limiti per sezioni I, III, VIII-1, VIII-2 e XII. P-Number resta testuale (es. 5A, 10H). Le temperature sono in °C. Un campo scalare viene compilato solo se tutti i valori non mancanti concordano. Valori diversi per tabella, dimensione o product form non vengono ridotti arbitrariamente a un solo limite: lo scalare resta NULL e il conflitto viene segnalato.

`ASME_Data_Source` conserva ogni valore originale con `id_main`, tabella, file e riga. `NP` e qualificatori come `204 (Cl. 3 only)` rimangono testo; non diventano limiti incondizionati. `NumericValue` è presente soltanto per temperature puramente numeriche. La colonna `Time_Dependent_Start_Temp` rimane NULL perché gli export non espongono questo campo e non conservano la tipografia necessaria a ricostruirlo. Le proprietà fisiche restano fuori dall’importazione corrente.

Le note non sono soltanto codici: `Source_Notes.NoteText` contiene il testo completo, incluse continuazioni, distinto per tabella di origine. `Source_Documents.Contents` conserva integralmente ogni notes.txt decodificato, con SHA-256 del file originale. `Property_Notes` conserva i testi delle note specifiche risolte; `VW_Property_Notes_All` include sia quelle specifiche sia le generali applicabili alla tabella della curva. Un codice non risolto è segnalato senza inventare testo. Gli export 6A–6D sono esclusi anche dal catalogo note.

```sql
SELECT m.Specification, m.Grade, a.*
FROM Main m JOIN ASME_Data a ON a.id_main = m.id;

SELECT SourceTable, FieldName, RawValue, NumericValue, File, CsvLine
FROM ASME_Data_Source WHERE id_main = @materialId;
```

## Report

Il report testuale va sul terminale oppure nel file indicato con `--report` e termina con il prospetto “MAIN - PRESENZA E, Alpha, c, k, a, density, Nu”. I prospetti stampati non hanno la colonna MissingData; l’ultima colonna del prospetto fisico è Nu. I seguenti file strutturati vengono conservati quando si specifica `--report-dir`:

| File | Risultato |
| --- | --- |
| `summary.json`, `README.md` | Conteggi, verifiche e limiti del confronto |
| `main-mechanical.csv` | MAIN completo: Specification, Grade, ClassConditionTemper, UNS, NominalComposition, ID e conteggi Sy/Su/S1/S2/Sb1/Sb2, indicatori HasData e lista delle assenze |
| `main-bolting.csv`, `main-nonbolting.csv` | Prospetti separati con i conteggi delle sole famiglie applicabili |
| `asme-data-source.csv` | Tutti i campi normativi originali, valori numerici validi e provenienza |
| `asme-data-conflicts.csv` | Campi normativi non scalari per identità MAIN, con evidenza dei valori originali |
| `composition-conflicts.csv` | Composizioni nominali discordanti per una stessa identità |
| `main-physical.csv` | MAIN completo con YES/NO per E, Alpha, c, k, a, density e Nu |
| `missing-mechanical.csv` | Ogni materiale con almeno una famiglia meccanica priva di valori, con elenco preciso delle famiglie mancanti |
| `material-changes.csv` | Identità aggiunte, assenti dal nuovo ambito, conservate o modificate; vecchi/nuovi ID e candidati |
| `source-row-changes.csv` | Impronte delle righe aggiunte/rimosse, anche per modifiche di valori, metadati o codici nota |
| `note-changes.csv` | Note aggiunte, rimosse o modificate anche senza cambiamenti alle curve |
| `missing-strength.csv` | Con S1/S2/Sb1/Sb2 ma senza Sy, senza Su o senza entrambe; candidati Sy/Su separati |
| `size-coverage.csv` | Curve S1/S2/Sb1/Sb2 non interamente coperte dagli intervalli dimensionali Sy/Su |
| `issues.csv` | Minimi discordanti a parità di fascia, Sy maggiore di Su, duplicati, varianti di curva e altre segnalazioni |
| `blank-revision-rows.csv` | Righe vuote con revisione, senza attribuzioni arbitrarie a materiali |
| `source-counts.csv`, `notes.csv` | Conteggi di tutte le tabelle sorgenti, ruolo meccanico/IdentityOnly e catalogo delle note |

Ogni esecuzione, compreso `--newdatabasefile` senza altre opzioni, verifica e stampa **i due prospetti meccanici per contesto e il prospetto fisico completo**. Il conteggio rappresenta righe/curve associate, non punti di temperatura: una curva con due temperature conta come una riga. `HasData` richiede almeno un valore; una curva vuota può quindi avere conteggio positivo ed essere comunque segnalata come mancante. Se manca un'intera cartella meccanica, il tool segnala `MissingSourceTable`, crea MAIN dalle altre tabelle e controlla la famiglia assente per ogni materiale del relativo contesto.

Sy/Su sono controllate per entrambi i contesti. Sb1/Sb2 sono controllate solo per BOLTING, S1/S2 solo per gli altri product form. L’assenza delle famiglie estranee al contesto non viene segnalata.

Nel prospetto fisico `c` indica il calore specifico (alias `Cp`), `k` la conducibilità (`TC`) e `a` la diffusività (`TD`). Sono considerate presenti soltanto proprietà con valori in `Physical_Property_Values`. Per density e Nu valgono anche le costanti non NULL di `Extension`; lo zero è un valore presente. Una definizione di proprietà senza valori non basta. L'archivio attuale non contiene queste curve fisiche: nel nuovo database sono quindi tutte NO. Questo stato descrive dati non ancora importati, non un'assenza nella norma ASME.

`summary.json.MainCoverage` riporta il numero di righe MAIN controllate, le assenze per ciascuna delle sei proprietà e i materiali con ciascuna proprietà fisica. `IdentitySourceRowCount` conta tutte le righe valide usate nell'unione; `SourceRowCount` conta soltanto le curve meccaniche importate. Gli input non meccanici sono indicati `IdentityOnly` in `source-counts.csv`.

Il database legacy consente il confronto delle identità e delle note disponibili: **non** quello delle vecchie curve tramite impronte. Il confronto completo delle righe funziona tra database generati dal tool. Modificare soltanto il corpo di una nota è segnalato in `note-changes.csv`, mentre le righe della curva possono restare invariate.

“RemovedFromSelectedScope” indica che la chiave precedente non compare nei nuovi input selezionati: non prova da sola una rimozione da ASME. Il tool segnala anche gradi legacy che sembrano date convertite da Excel. Le righe vuote `(25)` non forniscono un'identità da ricostruire con certezza. Una modifica del materiale o della nomenclatura richiede verifica dei candidati.

Il controllo dimensionale considera l'unione degli intervalli numerici e non sostituisce le ulteriori limitazioni contenute nelle note. Un intervallo coperto non garantisce che tutte le temperature S1/S2/Sb1/Sb2 siano coperte da Sy/Su.

L'importazione verifica edizione e unità, sequenza delle parti denominate `Exported_data (N).csv`/`.cvs`, intestazioni coerenti, valori numerici finiti, conteggi, note e chiavi esterne. Sono accettati anche altri nomi di file CSV/CVS; il contenuto deve mantenere il formato degli export ASME, comprese le intestazioni di edizione e tabella. La lettura delle directory non segue collegamenti simbolici. Un formato inatteso o una riga tecnica senza specifica causano un errore esplicito, anziché eliminazione silenziosa. Le note non risolte sono segnalate mantenendone i codici originali. Il database creato supera `PRAGMA integrity_check` e `PRAGMA foreign_key_check`.

## Compatibilità

Il nuovo database usa lo schema normalizzato richiesto e le tabelle di audit del tool. La libreria e l'app desktop esistenti continuano a usare il precedente schema `Materials`: l'integrazione del database 2025 in tali componenti richiederà un adeguamento separato. Il tool non trasferisce o ricostruisce le altre proprietà del vecchio database.
