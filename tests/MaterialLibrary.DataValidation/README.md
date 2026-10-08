# MaterialLibrary.DataValidation

Progetto xUnit che valida i **dati** di `ASME_Materials.db` (non il codice). Va eseguito dopo ogni modifica del database: un test rosso indica un materiale mancante, un valore assente o un valore che contraddice il Codice.

```powershell
dotnet test tests/MaterialLibrary.DataValidation                                  # tutti i test
dotnet test tests/MaterialLibrary.DataValidation --logger "console;verbosity=detailed"   # con il dettaglio dei valori
dotnet test tests/MaterialLibrary.DataValidation --filter "Category!=CodeCriteriaScreen" # senza i controlli di plausibilità sul Codice
```

Il database provato è `src/MaterialLibrary/data/ASME_Materials.db`; per provarne un altro imposta la variabile d'ambiente `ASME_MATERIALS_DB`. Il file viene copiato in un file temporaneo e mai modificato.

## Cosa si controlla

| Gruppo di test | Controllo |
| --- | --- |
| `ProntuarioTests` | Per ogni riga del Prontuario (`Data/prontuario-materials.csv`): il materiale esiste nel DB; SMYS, SMTS e UNS uguali al Prontuario; allungamento (se presente nel DB); Tmax delle tabelle ammissibili ≥ Tmax del Prontuario; **ogni tabella proprietà arriva fino alla Tmax del Prontuario**; **i valori oltre la Tmax del Prontuario sono completi** (nessun buco nella griglia di temperatura, nessun valore ≤ 0, E e Sy non crescenti); il modulo elastico copre la Tmin. |
| `CodeReferenceTests` | Modulo elastico, dilatazione termica (istantanea e media), conducibilità, diffusività, densità e Poisson confrontati con il **testo del Codice** (`src/MaterialLibrary/data/physical-properties-xml`, estratto dal PDF ASME II-D Metric 2025: Tabelle TM-1..5, TE-1..5, TCD, PRD). La colonna del Codice è scelta dal **codice UNS** (leghe, titanio) o dal **gruppo che le note del Codice assegnano alla composizione** (acciai); il collegamento materiale→gruppo salvato nel DB non viene mai usato. Il test fallisce se i valori del DB non coincidono con quella colonna e indica se sono *traslati* (valore di 50 °C memorizzato a 20 °C) o riempiti dove il Codice stampa "…". |
| `BaselineTests` | Istantanea dei valori di **ogni tabella** (Sy, Su, ammissibili Div. 1 e Div. 2, E, dilatazione istantanea e media, Cp, conducibilità, diffusività, SMYS, SMTS, densità, Poisson) a **Tmin, Tmax, 100, 200, 300, 400, 500, 600 °C**, con i punti oltre la Tmax del Prontuario marcati: il DB deve riprodurre `Baseline/prontuario-baseline.csv`. Le proprietà fisiche della baseline sono i valori del **Codice**, non quelli del DB (colonna `Source`); tutte le altre tabelle sono i valori attuali del DB, non ancora verificati. |
| `CatalogTests`, `MaterialDataTests` | Elenco precedente di materiali (acciai al carbonio e basso legati, inox, leghe di nichel, 5Cr/9Cr, 9Ni): presenza, completezza e plausibilità (Sy ≤ Su, E decrescente, Cp = λ/(ρ·a), ammissibili entro SMTS/3.5 – SMTS/2.4 e 2/3 Sy – 0.9 Sy con tolleranza 3 %). |
| `ReferenceValueTests` | Valori esatti del Codice per alcuni gradi (SMYS, SMTS, ammissibile Div. 1 a 40 °C, Tabella TE-1 Gruppo 1, E, densità, Poisson degli acciai al carbonio). |
| `SummaryTests` | Stampa il riepilogo dei materiali non trovati e dei controlli falliti. |

`Known database gaps are reported` non fallisce mai: documenta ciò che il DB non contiene (allungamento a rottura, External Pressure Chart, Kcss/Ncss).

## Contro cosa si confronta

- **Proprietà fisiche** (E, α istantanea e media, λ, a, ρ, ν) e **calore specifico**: contro il testo del Codice incluso nel repository.
  - E: Tabelle TM-1..5 (colonna di gruppo o di UNS). α: Tabelle TE-1..5, colonne A (istantanea) e B (media). λ e a: Tabella TCD. ρ e ν: Tabella PRD.
  - Il calore specifico non è tabulato dal Codice: è calcolato con la formula del DB, cp = λ / (ρ · a), sui valori del Codice.
  - Gli acciai sono assegnati al gruppo dalle liste di composizioni nelle note delle tabelle (Gruppi A–J di TM-1, Gruppi 1–4 di TE-1, Gruppi A–L di TCD). Le leghe sono assegnate dal codice UNS riportato nelle intestazioni del Codice.
  - Se il Codice non permette di determinare il gruppo (composizione non elencata nelle note, titanio per grado), la baseline tiene il valore del DB con `Source` = `Database (not verified ...)` e il test lo segnala come informazione, non come errore.
- **Sy, Su, ammissibili, SMYS/SMTS**: il testo del Codice non è nel repository (il PDF su Drive supera il limite di 10 MB del connettore). Si confrontano con i valori del Prontuario (SMYS, SMTS, UNS, Tmax) e con la baseline, che qui è una fotografia del DB: protegge dalle modifiche involontarie ma non prova che i valori siano giusti. Per verificarle servono le pagine delle Tabelle Y-1, U, 1A, 1B, 5A, 5B (e 3) in file sotto i 10 MB.

## Come estendere l'elenco dei materiali

1. **Prontuario**: aggiungi una riga a `Data/prontuario-materials.csv`.
   - `Specs`, `Grades`, `Classes`, `Uns`: alternative separate da `|`; vuoto = qualsiasi valore. Grado e classe devono essere scritti come nel DB (`TypeGrade`, `ClassConditionTemper`).
   - `UnsFilter = Y` seleziona il materiale per UNS (utile quando il DB lascia il grado vuoto).
   - `Smys`, `Smts` (MPa), `ElongationPct`, `TminC`, `TmaxC` come nel Prontuario.
2. **Altri materiali usati nell'anno**: aggiungi voci a `Catalog.frequentlyUsed` in `Catalog.fs` (`specGrade`, `uns`, `composition`) oppure crea una nuova lista e aggiungila a `Catalog.all`.
3. Rigenera la baseline (vedi sotto) dopo aver verificato i valori nuovi.

## Baseline

`Baseline/prontuario-baseline.csv` contiene, per ogni materiale del Prontuario, le proprietà fisiche del Codice e i valori correnti del DB per le altre tabelle (colonna `Source`). Quindi **il test della baseline fallisce finché il DB non riproduce il Codice**: le differenze sono gli errori del DB. L'elenco completo delle differenze viene scritto in `bin/<config>/net8.0/baseline-differences.txt`.

Dopo aver corretto il DB, o dopo aver verificato sul Codice le altre tabelle:

```powershell
$env:UPDATE_BASELINE = 1
dotnet test tests/MaterialLibrary.DataValidation --filter "FullyQualifiedName~Handbook materials equal"
```

La rigenerazione scrive sempre i valori del Codice per le proprietà fisiche (non quelli del DB) e i valori del DB per il resto: controlla `git diff` del file prima di versionarlo.

## Lettura dei risultati

Un FAIL non dice se l'errore è nel DB o nel Prontuario: lo mostra il confronto. Errori trovati sul DB dai test sulle proprietà fisiche:

- Collegamento materiale→gruppo del modulo elastico sbagliato per molti acciai: ad esempio gli inox 18Cr-8Ni (304, 304L, 316L, 321, 347…) usano il "Material Group D" (2¼–3Cr, E = 206 GPa a 100 °C) invece del "Material Group G" del Codice (austenitici, 189 GPa); gli acciai 1¼Cr e 2¼Cr-1Mo (T11, T22, SA-387 11/22) usano il Group B invece di C e D. Le leghe di nichel e il titanio usano un gruppo generico (189 o 191 GPa a 100 °C) invece della riga di UNS della Tabella TM-4/TM-5.
- Righe di gruppi traslate di una riga di temperatura: conducibilità e diffusività di N08800/N08810/N08811 e N06690, dilatazione di N06690, N07718, N07750; N08825 ha i valori a 75 °C ripetuti a 20 e 50 °C dove il Codice stampa "…". Il gruppo di diffusività assegnato a SB-443/444/446 N06625 è quello di N08330 (3 punti).
- Gruppo di dilatazione "Other Low Alloy Steels (Group 2)" del DB con gli stessi valori del Group 1 (il Codice ha valori diversi); gli inox duplex e a doppia fase usano il Group 3 invece del Group 2.
- Densità 7850 kg/m³ in SA-508, SA-533 e SA-553 contro 7750 della Tabella PRD; di conseguenza anche il calore specifico.
- SMYS/SMTS del DB diversi da quelli del Prontuario per alcune righe (ad esempio SA-193 B7, SA-537 Cl.2, SA-542 D Cl.4a, SA-240 2507).
