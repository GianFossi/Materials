# Opzioni dell'app e percorsi

Per il flusso operativo vedere [app desktop](desktop-app.md).
Etichette e tooltip dell'interfaccia sono in inglese; le unità sono mostrate nei campi.

## Aprire e salvare le opzioni

**Options → Application options...**

```text
%LOCALAPPDATA%\MaterialLibrary\crud-options.xml
```

Il primo avvio crea i valori predefiniti. **Save** nella finestra opzioni salva le
preferenze; non salva curve o librerie. Valori non validi/incompleti sono segnalati.
Il file può essere ispezionato o conservato insieme al backup dei dati personali.

## Product forms

La lista definisce le forme proposte nell'editor Material e nelle curve native S1/S2.
**Add** aggiunge una voce, **Remove selected** la rimuove; deve restarne almeno una.
I default includono Plate, Pipe, Tube, Forging, Bar, Casting, Fittings, varianti
seamless/welded e Bolting.

Rimuovere una scelta non modifica materiali o curve già salvati: il valore esistente
resta visibile nel relativo editor. Aggiungere una voce non certifica la sua applicabilità
normativa e non estende ProductForm a famiglie che non lo supportano.

## Default efficiency (joint)

Valore iniziale **0,70**, intervallo accettato maggiore di zero e fino a uno.
È un riferimento per il giunto longitudinale o più sollecitato dalla pressione nelle
curve welded S1/S2, quando le note non espongono un fattore esplicito.

**Solo guida:** non modifica/riduce tensioni, non sovrascrive i fattori delle note e non
sceglie le condizioni di progetto. Verificare sempre la nota e il Construction Code.

## Cartelle e database

| Preferenza | Effetto |
|---|---|
| **Flex/property XML folder** | Cartella iniziale nei dialoghi XML fisici/pressione esterna |
| **SQLite database file** | File preselezionato nel dialogo manuale del gestore database |

Entrambi possono essere vuoti. Un percorso non disponibile non viene aperto
automaticamente; scegliere un'altra posizione nel dialogo.

Queste preferenze **non sostituiscono `MaterialLibrary.config.xml`**, che risolve
cartella dati e nomi database del core.
In particolare **Open ASME 2025** usa la configurazione della libreria, non il semplice
file preselezionato nel dialogo. Una preferenza di percorso non crea una nuova copia
di lavoro e non aggiorna quella esistente.

Vedere [database e copie](asme-materials-db.md), [installazione](installation.md) e
[Excel](excel-addin.md) per distinguere i percorsi del repository da quelli runtime.

## Note modificabili

Nell'[editor ASME 2025](asme-2025-editor.md), **Allow edit** sblocca una copia locale
delle note originali per la curva; le tabelle ufficiali restano immutate.
**User Notes** è un campo separato. Queste modifiche si salvano con **Save curve**,
non con il salvataggio delle opzioni.
