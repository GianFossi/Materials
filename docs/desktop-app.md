# App desktop: uso e manutenzione

**Percorso 2: operazioni.** Prima di modificare dati, leggere la
[distinzione fra riferimento, copia di lavoro e XML](asme-materials-db.md).

## 1. Avviare e scegliere il percorso

L'app è WPF per Windows. Per avviarla dai sorgenti:

```powershell
dotnet run --project .\src\MaterialLibrary.CrudApp\MaterialLibrary.CrudApp.csproj
```

La finestra principale contiene due percorsi diversi:

| Operazione | Comando | Dove si lavora |
|---|---|---|
| Consultare/modificare ASME 2025 | **ASME 2025 Properties** | Database nativo `.working.db` |
| Backup, tabelle, SQL e controlli | **Database → Manage database...** | Gestore della copia SQLite |
| Creare/scambiare una libreria personalizzata | Menu **Library** e **Material** | Oggetti in memoria, salvabili come documenti JSON/XML |

Per la manutenzione dei dati ASME partire dall'editor nativo, non da **New Material...**
nel percorso della libreria personalizzata.

## 2. Aprire il database corretto

Nel gestore scegliere **Open ASME 2025**, poi **Edit 2025 Properties**.
**Open Database...** permette invece di selezionare un file esplicito.

La prima apertura del riferimento crea una copia SQLite tramite backup:

```text
%LOCALAPPDATA%\MaterialLibrary\ASME2025\asme_materials_2025.working.db
```

Le aperture successive riutilizzano quella copia, senza cancellare modifiche e importazioni.
Se si apre esplicitamente un file esistente `.working.db`, viene usato quel file.
Per un database legacy, il gestore prepara invece una copia sorella del file sorgente.

**Controllare sempre il percorso Working copy mostrato nella finestra.**
Aprire un nuovo riferimento nativo non sostituisce automaticamente la copia locale
predefinita già esistente. Per lavorare su un'altra edizione/copia seguire la
[procedura di manutenzione](#6-aggiornamento-e-ripristino).

## 3. Consultare e modificare un materiale

1. Nell'editor selezionare l'identità: specifica, grado, condizione e UNS.
2. Scegliere la famiglia (Sy, Su, S1, E, Alpha, ecc.).
3. Scegliere la **variante**: forma, banda dimensionale, fonte, livello S1 e note.
4. Verificare unità, punti e limiti normativi prima di usare il grafico o il valore.
5. Modificare la curva richiesta e usare **Save curve**. Per modificare l'identità usare
   il relativo comando separato.

I salvataggi nativi scrivono direttamente nella copia SQLite, in transazione.
Il pulsante **Save** della finestra principale salva invece la libreria JSON:
non è il comando di salvataggio delle curve native.
Un conflitto con una modifica esterna richiede ricaricamento, non un salvataggio forzato.

Le note utente e la copia locale delle note originali sono editabili; la fonte ufficiale
rimane intatta. Non scegliere arbitrariamente fra curve ambigue e non interpretare una
proprietà assente come zero. Dettagli: [editor ASME 2025](asme-2025-editor.md).

## 4. Aggiungere proprietà fisiche o diagrammi

Nell'editor usare **Import properties / charts from XML...**:

1. Scegliere la cartella XML.
2. Leggere record, fonte, unità e grafico.
3. Verificare le associazioni proposte per UNS/composizione e le condizioni.
4. Selezionare esplicitamente i materiali destinatari.
5. Confermare la revisione quando richiesta e autorizzare separatamente una sostituzione.
6. Importare e verificare le curve risultanti.

Gli XML non vengono importati all'avvio. I Code Cases richiedono sempre revisione;
alcuni dataset sono solo documentazione della fonte e non sono importabili.
Vedere [formati, limiti e provenienza XML](xml-physical-import.md).

## 5. Gestore database: strumenti di manutenzione

| Strumento | Uso e cautela |
|---|---|
| **Refresh / Reopen Last** | Ricarica dati / riapre la copia esistente |
| **Backup Working Copy** | Crea una copia di sicurezza completa |
| **Save Working Copy As...** | Salva l'intero database in un altro file; non solo il materiale selezionato |
| **Integrity Check** | Verifica strutturale SQLite, non validazione ingegneristica |
| **Raw Tables / Show Raw Rows** | Ispeziona/modifica righe SQL; preferire l'editor per le curve native |
| Scheda SQL e schema | Strumenti avanzati; scritture solo dopo backup e revisione |
| **Undo/Redo Last Transaction** | Opera sulle transazioni registrate dal gestore, non su qualunque scrittura esterna |
| Esportazione risultati CSV/JSON/XLSX | Esporta risultati visibili, non un backup completo del database |
| **Delete from DB** | Cancella il materiale e dati collegati dopo conferma; fare prima un backup |

Il gestore crea backup automatici prima delle operazioni SQL/schema che lo prevedono,
conservando gli ultimi dieci `*.pre-sql-*.db`. Non presumere che ogni salvataggio
dell'editor abbia un backup automatico o sia annullabile dal gestore.
Le modifiche native e le importazioni XML hanno propri registri nella copia SQLite.

**Import to Library** trasferisce un materiale al percorso in memoria.
**Write Library to DB** non è una modalità di aggiornamento nativo 2025: il backend
rifiuta l'esportazione legacy per non perdere varianti e provenienza.

## 6. Aggiornamento e ripristino

### Nuova edizione dagli export ASME

Usare il [DatabaseUpdater](database-updater.md) per creare un **nuovo file** e confrontarlo
con il precedente. Non aggiorna in-place il riferimento o la copia di lavoro, e non
trasferisce automaticamente proprietà fisiche, importazioni XML o modifiche locali.

### Nuova copia di lavoro o ripristino

1. Chiudere editor, gestore ed Excel che usano la copia.
2. Fare un backup completo della copia esistente.
3. Per una copia alternativa, usare un file verificato con nome `.working.db` e aprirlo
   esplicitamente tramite **Open Database...**.
4. Per rigenerare la copia predefinita, spostare al sicuro la vecchia copia e aprire
   il riferimento configurato. Non eliminare dati locali senza backup.
5. Verificare identità, curve, note e importazioni prima di riprendere il lavoro.

La riapertura del riferimento **non** azzera da sola la copia locale.
Vedere [backup e verifiche di rilascio](release-safety.md).

## 7. Librerie personalizzate, opzioni ed errori

I menu **Library → Open/Save JSON** e **Open XML/Save XML As...** gestiscono documenti
del modello `Material`. **Material → Edit Tables...** modifica le tabelle di quegli
oggetti, non le curve native per identità Main. Non usare questi documenti come backup
lossless del database 2025.

[Opzioni dell'app](crud-app-options.md) spiega forme di prodotto, efficienza del giunto
e cartelle predefinite. Le opzioni dell'app e `MaterialLibrary.config.xml` sono
configurazioni distinte.

Se il salvataggio/importazione fallisce, leggere il messaggio e ricaricare dopo un
conflitto esterno. I log dell'app sono descritti nelle [verifiche di manutenzione](release-safety.md).
Per l'accesso dallo stesso ambiente Excel vedere [add-in](excel-addin.md).
