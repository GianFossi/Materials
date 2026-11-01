# Mappa del progetto

Questa pagina serve a orientarsi, non a descrivere ogni API.

## Percorso 1: capire il database

1. [Struttura del database](asme-materials-db.md): quale file usare, come si identifica un materiale,
   come sono collegate curve, limiti e note; catalogo di tutte le tabelle; esempi di creazione, modifica e cancellazione; controlli di integrità.
2. [Modelli e tabelle](material-models.md): oggetti di calcolo della libreria, distinti dalle tabelle SQLite.
3. [API](library-api.md): accesso dai programmi.

**Idea chiave:** una riga `Main` identifica un materiale; ogni proprietà può avere più
curve. UNS o composizione da soli non identificano una variante normativa completa.

## Percorso 2: usare e mantenere l'app

1. [Installazione](installation.md) e [requisiti](requirements.md).
2. [App desktop](desktop-app.md): aprire, consultare, modificare e salvare.
3. [Editor ASME 2025](asme-2025-editor.md): identità e singole curve native.
4. [Importazione XML](xml-physical-import.md): aggiungere proprietà fisiche/diagrammi verificati.
5. [DatabaseUpdater](database-updater.md): creare una nuova edizione dagli export ASME.
6. [Backup e rilascio](release-safety.md): controlli tecnici e revisione dei dati.

Per usare gli stessi dati nei fogli di calcolo: [Excel](excel-addin.md).

## Dove si lavora

| Posizione | Contenuto | Quando modificarlo |
|---|---|---|
| `database/data/materials_2025.db` | Riferimento SQLite distribuito | Solo dopo creazione, confronto e verifica di un nuovo riferimento |
| `database/data/physical-properties-xml/` | Costanti e curve fisiche con provenienza | Durante manutenzione dei dataset; importazione successiva esplicita |
| `database/data/external-pressure-xml/` | Diagrammi materiali, geometrici e Code Cases | Dopo verifica della fonte e del tipo di diagramma |
| `%LOCALAPPDATA%\MaterialLibrary\ASME2025\` | Copia SQLite locale dell'utente | Dall'editor e dal gestore database |
| `src/` | Librerie, WPF ed Excel | Sviluppo applicativo |
| `tools/MaterialLibrary.DatabaseUpdater/` | Importazione degli export meccanici ASME | Manutenzione del processo di creazione/confronto |
| `database/sources/` e `tools/Build-Reference.ps1` | CSV ASME di partenza e script che ricostruisce `materials_2025.db` (con gli XML fisici senza revisione manuale) | Quando cambiano i CSV, gli XML o le regole di importazione |
| `tools/Generate-ProductSheet.ps1` | Scheda prodotto Markdown di un materiale (tutte le dimensioni, temperature di progetto/esercizio, note) | Quando serve il documento di un materiale |
| `tools/Generate-DatabaseCatalog.ps1` | Rigenera il catalogo delle tabelle in `docs/asme-materials-db.md` (capitolo 2) | Dopo ogni modifica dello schema o delle sue regole |
| `tests/` | Test e progetto esempi | Verifica delle modifiche |
| `database/data/archive/2023/` | Database precedente | Solo consultazione/confronto esplicito |

Nei build e nei pacchetti i dati si trovano sotto `data/`, non sotto `database/data/`.
Il percorso nel repository e quello a runtime hanno quindi prefissi diversi.

## Se rientri dopo una pausa

- Controlla il percorso **Working copy** mostrato dall'app prima di scrivere.
- Non aspettarti che una nuova versione del riferimento aggiorni la copia locale: le modifiche
  esistenti vengono conservate.
- Controlla le proprietà effettivamente presenti con l'editor o `MatPropertyTable`, non contando gli XML.
- Consulta [AI_HISTORY](../AI_HISTORY.md) per le decisioni passate, non come istruzioni operative attuali.
- Usa [README](../README.md) come indice e le guide tematiche come riferimento operativo.
