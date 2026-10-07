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
| `CodeReferenceTests` | Modulo elastico, dilatazione termica (istantanea e media), conducibilità, diffusività, densità e Poisson confrontati con il **testo del Codice** (`src/MaterialLibrary/data/physical-properties-xml`, estratto dal PDF ASME II-D Metric 2025: Tabelle TM-1..5, TE-1..5, TCD, PRD). Il test fallisce se nessuna colonna del Codice riproduce i valori del DB e indica se i valori sono *traslati* (valore di 50 °C memorizzato a 20 °C). |
| `BaselineTests` | Istantanea dei valori di **ogni tabella** (Sy, Su, ammissibili Div. 1 e Div. 2, E, dilatazione, Cp, conducibilità, diffusività, SMYS, SMTS, densità, Poisson) a **Tmin, Tmax, 100, 200, 300, 400, 500, 600 °C**, con i punti oltre la Tmax del Prontuario marcati: deve restare uguale a `Baseline/prontuario-baseline.csv`. |
| `CatalogTests`, `MaterialDataTests` | Elenco precedente di materiali (acciai al carbonio e basso legati, inox, leghe di nichel, 5Cr/9Cr, 9Ni): presenza, completezza e plausibilità (Sy ≤ Su, E decrescente, Cp = λ/(ρ·a), ammissibili entro SMTS/3.5 – SMTS/2.4 e 2/3 Sy – 0.9 Sy con tolleranza 3 %). |
| `ReferenceValueTests` | Valori esatti del Codice per alcuni gradi (SMYS, SMTS, ammissibile Div. 1 a 40 °C, Tabella TE-1 Gruppo 1, E, densità, Poisson degli acciai al carbonio). |
| `SummaryTests` | Stampa il riepilogo dei materiali non trovati e dei controlli falliti. |

`Known database gaps are reported` non fallisce mai: documenta ciò che il DB non contiene (allungamento a rottura, External Pressure Chart, Kcss/Ncss).

## Contro cosa si confronta

- **Proprietà fisiche** (E, α, λ, a, ρ, ν): contro il testo del Codice incluso nel repository. È un confronto con il PDF, tramite l'estrazione già presente in `physical-properties-xml`.
- **Sy, Su, ammissibili, SMYS/SMTS, Cp**: il testo del Codice non è nel repository. Si confrontano con i valori del Prontuario (SMYS, SMTS, UNS, Tmax) e con la **baseline**. La baseline è una fotografia: protegge dalle modifiche involontarie, ma è corretta solo se i valori sono stati verificati sul PDF prima di congelarli. Per verificare anche queste tabelle servono le pagine del Codice (Tabelle Y-1, U, 1A, 1B, 5A, 5B) in formato testo/XML.
- Il coefficiente di dilatazione medio nel DB non esiste: è derivato per integrazione da quello istantaneo (colonna A) e confrontato con la colonna B del Codice (tolleranza 0.1).

## Come estendere l'elenco dei materiali

1. **Prontuario**: aggiungi una riga a `Data/prontuario-materials.csv`.
   - `Specs`, `Grades`, `Classes`, `Uns`: alternative separate da `|`; vuoto = qualsiasi valore. Grado e classe devono essere scritti come nel DB (`TypeGrade`, `ClassConditionTemper`).
   - `UnsFilter = Y` seleziona il materiale per UNS (utile quando il DB lascia il grado vuoto).
   - `Smys`, `Smts` (MPa), `ElongationPct`, `TminC`, `TmaxC` come nel Prontuario.
2. **Altri materiali usati nell'anno**: aggiungi voci a `Catalog.frequentlyUsed` in `Catalog.fs` (`specGrade`, `uns`, `composition`) oppure crea una nuova lista e aggiungila a `Catalog.all`.
3. Rigenera la baseline (vedi sotto) dopo aver verificato i valori nuovi.

## Baseline

`Baseline/prontuario-baseline.csv` contiene i valori verificati. Se il test `Handbook materials equal the verified baseline` segnala differenze:

1. controlla ogni differenza sul Codice;
2. se i nuovi valori sono corretti: `UPDATE_BASELINE=1 dotnet test tests/MaterialLibrary.DataValidation --filter "FullyQualifiedName~Handbook materials equal"` (PowerShell: `$env:UPDATE_BASELINE=1; dotnet test ...`) e versiona il file.

La baseline attuale è stata generata dal database così com'è, **prima** della verifica sul PDF: contiene anche i valori errati segnalati dai test `CodeReferenceTests`; va rigenerata dopo la correzione del DB.

## Lettura dei risultati

Un FAIL non dice se l'errore è nel DB o nel Prontuario: lo mostra il confronto. Esempi riportati dai test: gruppi di conducibilità/diffusività di N08800, N08810, N08811 e N06690 (e di dilatazione di N06690, N07718, N07750) con valori traslati di una riga di temperatura rispetto al Codice; densità 7850 kg/m³ in SA-508 contro 7750 della Tabella PRD; SMYS/SMTS del DB diversi da quelli del Prontuario per alcune righe (ad esempio SA-193 B7, SA-537 Cl.2, SA-542 D Cl.4a, SA-240 2507).
