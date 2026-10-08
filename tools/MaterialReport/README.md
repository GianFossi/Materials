# MaterialReport – audit e importazione dati

Script F# (`dotnet fsi`, .NET SDK 8) per controllare la completezza di `ASME_Materials.db` e per importare i grafici di pressione esterna dagli XML.
Nessuno script modifica il database pacchettizzato: viene letto da una copia temporanea (niente file `-wal`/`-shm` accanto all'originale).

| Script | Cosa fa |
| --- | --- |
| `Export-MaterialReport.fsx` | Report per materiale (CSV), elenco dei materiali privi di dati per ogni gruppo, riepilogo finale. |
| `Import-ExternalPressureXml.fsx` | Parsing e validazione degli XML delle pressioni esterne; importazione opzionale in una **copia** del database con mappa materiale → figura. |
| `MaterialReportCore.fs` | Modulo comune (lettura DB, modello dati per materiale), usato anche dal progetto `tests/MaterialLibrary.DataValidation`. |

Dalla radice del repository:

```powershell
dotnet fsi tools/MaterialReport/Export-MaterialReport.fsx            # report + riepilogo
dotnet fsi tools/MaterialReport/Export-MaterialReport.fsx --long     # anche un CSV con un punto per riga
dotnet fsi tools/MaterialReport/Import-ExternalPressureXml.fsx       # solo parsing e validazione
```

I file generati vanno in `tools/MaterialReport/output/` (ignorata da git). Il CSV usa `;` come separatore (`--sep ","` per cambiarlo) e UTF-8 con BOM.

## Export-MaterialReport

Per ogni materiale: Specifica, Grado, Classe, UNS, Composizione nominale, Prodotto, SMYS, SMTS, allungamento, P/G Number (ASME IX), Tmax e temperatura di inizio campo tempo-dipendente per Div. 1 e Div. 2, ammissibili Div. 1/Div. 2 per range di spessore (con indicazione stress normale / alto G5), Sy e Su per range di spessore, modulo elastico, densità, Poisson, dilatazione termica istantanea e media, calore specifico, conducibilità, diffusività, External Pressure Chart, disponibilità dei dati per la curva stress-strain e per quella ciclica.

File prodotti: `material-report.csv`, `material-report-missing.csv` (un record per gruppo e materiale che ne è privo), `material-report-summary.txt`.

Come leggere i dati:

- **Dilatazione termica**: il DB contiene il coefficiente *istantaneo* (colonna A delle tabelle TE ASME). Il coefficiente *medio* 20 °C → T (colonna B) è ricavato per integrazione trapezoidale; il test lo confronta con la colonna B di TE-1 (Gruppo 1).
- **Stress alto (G5)**: il DB mette la nota G5 su entrambe le righe di un materiale; la riga alta è quella con le tensioni maggiori nel gruppo (stessa tabella, stesso range di spessore). `G5 riga unica` indica che manca la riga gemella. Su alcuni materiali (es. SA-240 316) la nota G5 non è presente nel DB e la riga alta risulta marcata come normale: il test lo segnala come WARN.
- **Div. 1** include solo le righe con Tmax VIII-1; se la tabella 1A/1B esiste ma senza Tmax VIII-1 il materiale è segnalato come *non ammesso in VIII-1*. La bulloneria (Tabella 3) compare in Div. 1/Div. 2 con fonte `T3 bulloneria`.
- **Allungamento a rottura**: campo vuoto nel DB per tutti i materiali; i range di spessore non sono modellati.
- **Curva ciclica** (VIII-2 3-D.4): servono Kcss/Ncss (Tabella 3-D.2M), che il DB non contiene.

## Test dei dati

I test sui valori del database (materiali del Prontuario, confronto con il testo del Codice, baseline) sono nel progetto xUnit `tests/MaterialLibrary.DataValidation`: vedi il [readme](../../tests/MaterialLibrary.DataValidation/README.md).

## Import-ExternalPressureXml

Gli XML sono estrazioni grezze dal PDF (`raw-tokenized-review-required`). Lo script:

1. ricostruisce le coppie (A, B) per temperatura e per curva (esponenti omessi nelle righe di continuazione, tabelle spezzate su più pagine, etichette `Class`, `Cl.`, `Curve`, `Up to`, `Room temp.`);
2. valida ogni figura (A strettamente crescente, B non decrescente, numero pari di valori, esponenti risolti, nessuna curva duplicata);
3. importa **solo** le figure validate; le altre sono elencate come *RIVEDERE* con il motivo. I layout a colonne affiancate (NFN-15/16/18/19, NFZ-2) non sono supportati e non vengono indovinati.

La validazione controlla la struttura, non i valori rispetto al Codice: confronta a campione alcuni punti con il PDF prima di usare i dati.

### Associare i materiali alle figure

Il DB non contiene il collegamento materiale → figura (nel Codice è la colonna "External Pressure Chart No." delle Tabelle 1A/1B/5A/5B). Lo script non lo ricava per inferenza: va fornito.

```powershell
# 1. Template con tutti i materiali e la colonna Figura vuota
dotnet fsi tools/MaterialReport/Import-ExternalPressureXml.fsx --make-template tools/MaterialReport/output/map.csv
# 2. Compila la colonna Figura (ID oppure regole per Specifica/Grado/Classe/UNS)
# 3. Importa in una copia del database
dotnet fsi tools/MaterialReport/Import-ExternalPressureXml.fsx --map tools/MaterialReport/output/map.csv --apply --target-db tools/MaterialReport/output/ASME_Materials.extpressure.db
```

Una riga della mappa con `ID` vale per quel materiale; una riga senza `ID` è una regola (campi vuoti = qualsiasi valore), per esempio `;SA-516;70;;;CS-2`. Vince la regola con più campi compilati e la riga con `ID` prevale sulle regole; due regole di pari peso con figure diverse sono segnalate e saltate.

Con `--apply` nella copia vengono scritte la tabella `ExternalPressureChart` (punti A–B) e, per ogni materiale mappato, una riga in `ExternalPressureTable` con `ReferenceData` = figura. `--target-db` deve essere diverso da `--db`. Il report `Export-MaterialReport.fsx --db <copia>` mostra poi la figura nella colonna `ExtPressure_Chart`.
