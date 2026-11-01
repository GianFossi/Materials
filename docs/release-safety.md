# Backup, controlli e rilascio

Questa è una checklist tecnica, non una certificazione dei valori normativi.

## Prima di modificare dati

1. Controllare il percorso della copia `.working.db` nell'app.
2. Usare **Backup Working Copy** per conservare l'intero database.
3. Annotare riferimento/edizione e origine dei dati da importare.
4. Non modificare direttamente il riferimento distribuito o l'archivio.

I salvataggi delle curve e le importazioni XML scrivono subito nella copia di lavoro.
**Save Working Copy As...** esporta quella copia completa: non è necessario per rendere
persistente ogni precedente salvataggio dell'editor.

## Cosa coprono backup e cronologia

- Prima delle operazioni SQL/schema previste dal gestore vengono creati backup
  `*.pre-sql-*.db` accanto alla copia; la pulizia conserva gli ultimi dieci.
- Non tutti i percorsi di scrittura usano quei backup automatici. Fare un backup
  esplicito prima di importazioni, cancellazioni e modifiche importanti.
- Undo/redo del gestore riguarda operazioni registrate e supportate. Non annulla
  genericamente modifiche native, importazioni XML, SQL arbitrario o scritture esterne.
- `Property_Edit_History` e `Xml_Import_History` sono audit separati in SQLite:
  conservarli è utile, ma non equivale a un ripristino automatico.
- Esportare una query in JSON/XLSX o salvare una libreria JSON non salva tutto il database.

Per il ripristino seguire [app desktop](desktop-app.md#6-aggiornamento-e-ripristino).

## Prima di pubblicare un nuovo riferimento

1. Creare un file nuovo con il [DatabaseUpdater](database-updater.md), senza sovrascrivere
   il precedente.
2. Rivedere report di identità, curve, note, conflitti e assenze.
3. Eseguire `PRAGMA integrity_check` e `PRAGMA foreign_key_check` sulla copia finale.
4. Verificare valori contro la fonte, unità, limiti e condizioni; l'integrità SQLite
   non rileva un valore ingegneristicamente errato.
5. Documentare le importazioni fisiche/Code Cases eventualmente aggiunte.
6. Conservare hash e report della versione effettivamente distribuita.

Non trasferire silenziosamente dati dal 2023 né trattare le associazioni XML candidate
come dati già approvati. Non usare vecchi conteggi di test come prova del file finale.

## Prima di distribuire app, NuGet o Excel

- Compilare il componente modificato e usare i test pertinenti.
- Verificare che output/pacchetto contengano configurazione, riferimento e XML previsti;
  l'archivio 2023 non deve diventare un fallback.
- Controllare i warning NuGet prima del rilascio.
- Provare l'app su una copia di lavoro, verificando salvataggio, riapertura e backup.
- Provare la XLL sull'architettura Excel destinataria, con Desktop Runtime corrispondente,
  e verificare ricerca, tabella, valore e messaggio su variante ambigua.
- Verificare che una copia locale esistente non venga sovrascritta da una nuova installazione.

I test che scrivono in SQLite devono usare copie temporanee, mai il riferimento del repository.

## Diagnostica

I log WPF sono in:

```text
%LOCALAPPDATA%\MaterialLibrary.CrudApp\logs\crudapp-YYYY-MM-DD.log
```

In caso di errore conservare messaggio, operazione, percorso della copia e log pertinente,
evitando di pubblicare dati sensibili. Dopo un conflitto esterno ricaricare prima di scrivere.
L'annullamento delle operazioni non è un ripristino delle transazioni già completate.
