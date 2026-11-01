# Modelli e tabelle della libreria

Questa guida descrive gli **oggetti di calcolo Material**, non lo schema SQLite.
Per file, identità Main e relazioni native leggere [struttura del database](asme-materials-db.md).
Per l'uso da codice leggere [API](library-api.md).

## Unità e dati mancanti

| Quantità | Unità |
|---|---|
| Temperatura | °C |
| Tensione, resistenza, tensione ammissibile, E | MPa |
| Tempo/durata | ore |
| Dimensione/spessore/diametro | mm |
| Densità | kg/m3 |
| Calore specifico | J/(kg*K) |
| Conduttività | W/(m*K) |
| Diffusività | m2/s |
| Coefficienti di dilatazione | 1/degC |
| Poisson, fattore A, cicli | adimensionali |
| Deformazione nelle tabelle stress-strain/creep | percentuale (%) |

Controllare i commenti del tipo specifico: non convertire automaticamente una
percentuale in frazione né confondere coefficiente medio, istantaneo e allungamento.
Un dato opzionale assente non è zero e non è ricostruito da un altro materiale.

## Identità e proprietà di base

`Material` raggruppa identità, proprietà di base/fisiche, tabelle di resistenza e
eventuali dati/modelli avanzati. Non tutte le proprietà sono disponibili per ogni materiale.

`BasicProperties` distingue minimi scalari SMYS/SMUTS, allungamenti longitudinali/
trasversali e riduzione di area. Questi dati non sostituiscono le curve dipendenti
dalla temperatura `StrengthProperties.SyTable` e `SuTable`.
`BasicProperties.governingElongationPercent` seleziona il valore minore fra le direzioni
disponibili quando occorre un solo allungamento.

Nel database 2025 il minimo scalare può restare `NULL` quando le righe sorgenti
differiscono; non scegliere la prima banda dimensionale per riempirlo.
**Limite della proiezione Material attuale:** i campi SMYS/SMUTS di `BasicProperties`
sono numerici non opzionali; l'idratazione nativa rappresenta i `NULL` come `0`.
Questo zero non è un minimo normativo. Per distinguere assenza e valore reale leggere
`Main`/`Property_Source` e le curve native, non il solo oggetto proiettato.

## PropertyTable e bande dimensionali

`PropertyTable` rappresenta una tabella con asse X e colonne di valori. Per Sy/Su:
X è temperatura, Y è tensione, ogni colonna può rappresentare una banda dimensionale.

`SizeColumnRange` usa limiti opzionali `Lower`/`Upper` e inclusività esplicita.
Usare `SizeColumnRange.contains`, non l'indice della colonna, per verificare una dimensione.
La dimensione pertinente dipende da forma e fonte: spessore, diametro, OD o DN non sono
intercambiabili. Gli estremi aperti/chiusi devono restare quelli del dataset.

## Tensioni ammissibili

`StrengthProperties.AllowableStressDatasets` mantiene fonte, caso, curva e banda:

| Source del modello | Fonte | Corrispondenza nativa |
|---|---|---|
| `Division1AllowableStress` | Table 1A/1B, caso normale | S1 Normal |
| `Division1HighAllowableStress` | Table 1A/1B, alternativa con G5 | S1 High |
| `Division2AllowableStress` | Table 5A/5B | S2 |
| `BoltingAllowableStress` | Table 3 | Sb1 |
| `BoltingDivision2AllowableStress` | Table 4 | Sb2 |

Verificare applicabilità, note, temperatura e condizioni della singola variante.
G5 non autorizza universalmente l'uso del valore più alto. L'efficienza del giunto
mostrata dall'app è una guida, non una riduzione automatica dei dati.

S1H/S3 possono comparire nel vocabolario legacy; non sono nomi delle famiglie native
equivalenti senza considerare origine e metadati.

## Tabelle specializzate: assi e condizioni

| Tipo | Asse X | Valore Y | Metadati da verificare |
|---|---|---|---|
| [StressStrainTable](../src/MaterialLibrary/Tables/StressStrainTable.fs) | Deformazione (%) | Tensione (MPa) | Temperatura, durata opzionale, fonte, basi Engineering/True di tensione e deformazione |
| [CreepTable](../src/MaterialLibrary/Tables/CreepTable.fs) | Tempo (ore) | Deformazione di creep (%) | Temperatura, tensione applicata, fonte e avvertenza di applicabilità |
| [FatigueTable](../src/MaterialLibrary/Tables/FatigueTable.fs) | Cicli | Ampiezza di tensione Sa (MPa) | Temperatura e durata opzionale |
| [ExternalPressureTable](../src/MaterialLibrary/Tables/ExternalPressureTable.fs) | Fattore A | Tensione compressiva (MPa) | Temperatura, fonte e durata opzionale |

Stress-strain non è sempre “True”: leggere `StrainBasis` e `StressBasis`.
La tabella di creep non è tensione contro tempo; la tensione applicata è una condizione.
La tabella di fatica è S-N, non ampiezza contro deformazione.

`ReferenceDurationHours = None` distingue il regime indipendente dal tempo;
`Some hours` identifica il riferimento isocrono dove il tipo lo supporta.
La durata non prova da sola che la fonte sia valida per la valutazione.

## Generazione dei modelli

### Stress-strain

[StressStrainModels.fs](../src/MaterialLibrary/Models/StressStrainModels.fs) contiene
modelli separati:

- `CodeCase2964TangentModulusModel.compute`: modulo tangente da input Appendix I.
- `Asme3dStressStrainModel.computeStrain`: deformazione da input Annex 3-D.
- `Asme3dStressStrainModel.computeTangentModulus`: modulo tangente Annex 3-D.

Non scambiare risultati/input fra contesti solo perché alcune formule sono affini.
Usare i parametri e limiti della fonte applicabile; questa guida non riproduce le tabelle normative.
`StressStrainTable.ReferenceDurationHours` consente dati indipendenti dal tempo o isocroni;
la fonte e il metodo scelto restano espliciti.

**API 579-1/ASME FFS-1 Annex 10B.5 non è implementato.**
`Api579Annex10B5.ensureImplemented()` restituisce un errore: non usarlo per generare
curve ingegneristiche.

### Creep

La libreria non sceglie automaticamente il modello:

| Source | Limite principale |
|---|---|
| `CreepDatabase` | Copertura delle fasi determinata dalla fonte, da verificare |
| `GeneratedNortonPowerLaw` | Legge empirica potenza-tempo; non un modello completo delle tre fasi |
| `GeneratedGarofalo` | Legge empirica stress/tempo; non un modello completo delle tre fasi |
| `GeneratedKachanovOmega` | Secondario e terziario con danno; trascura il primario |

Usare esplicitamente `generateWithNorton`, `generateWithGarofalo` o
`generateWithKachanovOmega` dopo verifica di materiale, temperatura e tensione.
Le tabelle conservano `ApplicabilityWarning`. L'integrazione Kachanov adattiva riporta
passo accettato e possibile tempo di rottura.
La generazione Garofalo applica il termine termico con temperatura convertita;
il metodo di basso livello `GarofaloModel.creepStrain` richiede invece un coefficiente A
effettivo già calibrato alla temperatura.

### Pressione esterna e Code Case 2964

`ExternalPressureTable.Source` distingue `MaterialDatabase` e `CodeCase2964`.
Le tabelle sono in `Material.StrengthProperties.ExternalPressureTables`; la durata
opzionale distingue i regimi senza introdurre un secondo tipo di tabella.

Gli input Appendix III memorizzati e la regola dei fattori possono alimentare i
builder dedicati. I preset “published” per famiglie non ancora cablate restituiscono
un errore esplicito, non coefficienti di ripiego.

Per curve Code Case 2964 le API di lookup ordinario su A e su `log10(A)` sono distinte.
La generazione dispone anche di selezione esplicita della calibrazione: confrontare
punti fonte, errore medio/massimo e intervallo prima dell'uso. Una curva calibrata
non equivale all'approvazione del Case per il progetto.

Non confondere i diagrammi geometrici G, le curve materiali A/B e i diagrammi A/Sc
dei Code Cases; i dataset XML hanno formati e importabilità distinti nella
[guida XML](xml-physical-import.md).
