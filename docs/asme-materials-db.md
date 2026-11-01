# Struttura del database ASME

**Percorso 1: dati.** Per operare sui dati vedere [app desktop](desktop-app.md).

## 1. Quale file contiene cosa

| File/dataset | Ruolo |
|---|---|
| [asme_materials_2025.db](../database/data/asme_materials_2025.db) | Riferimento attivo, con schema nativo normalizzato |
| `%LOCALAPPDATA%\MaterialLibrary\ASME2025\asme_materials_2025.working.db` | Copia locale predefinita modificabile dall'app e dall'editor Excel |
| [XML fisici](../database/data/physical-properties-xml/) | Dati candidati con fonte e chiavi di associazione; non automaticamente presenti in SQLite |
| [XML pressione esterna](../database/data/external-pressure-xml/) | Dataset di tipi diversi, non tutti importabili come proprietà materiale |
| Libreria JSON/XML | Documenti del modello `Material`, per scambio e materiali personalizzati; non un backup completo del database nativo |
| [ASME_Materials_2023.db](../database/data/archive/2023/ASME_Materials_2023.db) | Archivio legacy, aperto solo esplicitamente |

Il progetto copia e impacchetta il riferimento e gli XML sotto `data/`. L'archivio 2023
non è distribuito come fallback.

**Stato del riferimento attuale:** 2.532 righe `Main`; nessun punto in
`Physical_Property_Values`. I dataset fisici XML sono disponibili separatamente:
le associazioni candidate non equivalgono a proprietà già importate o approvate.
Per una copia modificata, verificare sempre il suo contenuto effettivo.

## 2. Identità del materiale

La chiave naturale di `Main` è composta da:

1. `Specification`
2. `Grade`
3. `ClassConditionTemper`
4. `AlloyDesignation` (UNS)

`Main.id` è la chiave tecnica SQLite. `Material_Identity.NaturalKey` impone l'unicità
della combinazione; `Material_Source` collega l'identità alle righe sorgenti.
`NominalComposition` è utile per ricerca e associazioni, ma non sostituisce la chiave naturale.

Forma di prodotto, dimensione e variante della curva **non creano nuove identità Main**.
Ad esempio, una stessa identità può avere più curve S1 per forma e intervallo dimensionale.
Un UNS comune non autorizza a fondere specifiche, condizioni o trattamenti diversi.

`SMYS` e `SMUS` sono minimi scalari separati dalle curve Sy/Su. Restano `NULL` quando
non è possibile rappresentare un unico valore senza perdere le distinzioni della fonte;
i minimi delle singole righe restano in `Property_Source`.

## 3. Come leggere le relazioni

```text
Main
 +-- Material_Identity / Material_Source
 +-- Material_Properties -- Sizes
 |       +-- Property_Values
 |       +-- Property_Source / Property_Note_Links
 +-- Physical_Properties -- Physical_Property_Values
 +-- Extension (costanti densità/Poisson)
 +-- Material_EP_Charts -- External_Pressure_Charts
 |       +-- EP_Chart_Curves -- EP_Chart_Points
 +-- ASME_Data
```

| Gruppo | Tabelle/viste principali | Significato |
|---|---|---|
| Curve meccaniche | `Material_Properties`, `Property_Values`, `Sizes` | Una curva per famiglia e variante, con punti temperatura/valore |
| Bolting | `Sb1`, `Sb2` | Dati dedicati delle Table 3 e 4, distinti |
| Viste di tensione ammissibile | `S1`, `S2` | Accesso alle famiglie Division 1 e Division 2 |
| Proprietà fisiche | `Physical_Properties`, `Physical_Property_Values`, `Extension` | Curve fisiche e costanti; tabella esistente non significa dati presenti |
| Pressione esterna | `External_Pressure_Charts`, `Material_EP_Charts`, `EP_Chart_Curves`, `EP_Chart_Points` | Diagrammi condivisi, collegamenti ai materiali e punti A/B |
| Applicabilità normativa | `ASME_Data`, `ASME_Data_Source`, `Property_Source` | P-No., Group No., chart e limiti per Section, con provenienza |
| Note | `Property_Notes`, `Source_Notes`, `Property_Note_Links`, `VW_Property_Notes_All` | Testo e collegamenti delle note originali |
| Audit dell'importazione ASME | `Source_Documents`, `Import_Files`, `Import_Omitted_Rows`, `Database_Metadata` | Fonti, file importati, righe escluse e metadati |

Lo schema comprende anche tabelle per altri modelli (stress-strain, creep, ciclico e
Larson–Miller). Non presumere che siano popolate per ogni materiale.
La definizione per i nuovi database è in [Schema.sql](../tools/MaterialLibrary.DatabaseUpdater/Schema.sql).

## 4. Famiglie e unità native

| Codice API/editor | Quantità | Unità | Codice fisico SQLite, se diverso |
|---|---|---|---|
| Sy / Su | Snervamento / resistenza a trazione dipendenti dalla temperatura | MPa | — |
| S1 / S2 | Tensione ammissibile Division 1 / Division 2 | MPa | — |
| Sb1 / Sb2 | Tensione ammissibile bolting Table 3 / Table 4 | MPa | — |
| E | Modulo elastico | MPa | — |
| Alpha | Coefficiente medio di dilatazione | 1/degC | — |
| AlphaInstantaneous | Coefficiente istantaneo di dilatazione | 1/degC | — |
| c | Calore specifico | J/(kg*K) | Cp |
| k | Conduttività termica | W/(m*K) | TC |
| a | Diffusività termica | m2/s | TD |
| density | Densità | kg/m3 | Density |
| Nu | Coefficiente di Poisson | adimensionale | — |

Temperature in °C; dimensioni in mm. Nei diagrammi materiali EP, A è adimensionale
e B è in MPa. Alpha medio richiede la temperatura di riferimento della fonte.
La dilatazione lineare C delle tabelle TE non è il calore specifico `c`.

Una curva meccanica può avere limiti dimensionali aperti/chiusi. `ProductForm` si applica
solo a S1/S2 nel modello nativo. La selezione deve considerare curva, dimensione, forma,
temperatura e restrizioni delle note. Una famiglia assente non vale zero; curve multiple
non autorizzano a scegliere la prima. Il lookup nativo non estrapola.

## 5. Fonte ufficiale e modifiche locali

I limiti applicabili a una curva vengono dalla sua riga `Property_Source`, non da un
valore appiattito per materiale in `ASME_Data`. `NP`, campo vuoto e limite qualificato
non sono equivalenti a un limite numerico generale.

Nella copia di lavoro l'editor aggiunge:

- `Property_Curve_Metadata`: note utente, copia modificata delle note originali, asse
  dimensionale, livello S1 Normal/High e override dei limiti della curva;
- `Property_Edit_History`: snapshot delle modifiche native.

Le note e i documenti sorgenti restano immutati. Una copia locale modificata delle note
non diventa una nuova nota ufficiale ASME.

L'importazione XML aggiunge `Xml_Import_Documents`, `Xml_Property_Associations` e
`Xml_Import_History`, conservando documento, hash SHA-256, associazioni e prima/dopo.
Vedere [importazione XML](xml-physical-import.md).

## 6. Cosa resta legacy

Il database 2023 usa `Materials` e tabelle pivotate `T_*`. Il percorso CRUD legacy
può aggiungere `MaterialDocumentStore` e proiezioni SQL del documento `Material`.
**Questo non è lo schema centrale del riferimento 2025.**

La serializzazione completa JSON usa attualmente schema 17 e legge anche 15/16.
Il numero dello schema JSON non è l'edizione ASME né la versione dello schema SQLite.
Scrivere un documento legacy nel database nativo tramite **Write Library to DB** è
rifiutato: perderebbe l'identità delle curve e le varianti della fonte.

## 7. Verifica e aggiornamento

- [DatabaseUpdater](database-updater.md) crea/confronta database da export ASME.
- [Editor](asme-2025-editor.md) modifica singole curve nella copia di lavoro.
- [Importatore XML](xml-physical-import.md) associa esplicitamente dati aggiuntivi.
- [Backup e rilascio](release-safety.md) descrive i controlli prima di sostituire un riferimento.

[asme-2025-import-verification.json](asme-2025-import-verification.json) è una registrazione
della verifica di un'importazione specifica: hash, conteggi e test lì riportati non sono
una certificazione permanente di qualunque copia di lavoro.
