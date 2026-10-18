# Importazione XML di proprietà fisiche e diagrammi

Il CRUD e l'add-in Excel possono importare E, Alpha medio, Alpha istantaneo, c, k, a, densità, Nu e diagrammi External Pressure dal database ASME 2025. Si lavora sulla copia `.working.db` condivisa; il riferimento 2025 e l'archivio 2023 restano separati.

## Procedura grafica

1. Nel CRUD aprire **Proprietà ASME 2025 → Importa proprietà / diagrammi da XML…**. In Excel usare **Materiali ASME 2025 → Importa XML proprietà / chart**.
2. Scegliere una cartella: vengono letti anche gli XML nelle sottocartelle. Lo stato riporta i file non leggibili.
3. Selezionare un record nell'elenco. Controllare testo/XML originale, note, famiglia, unità e punti numerici. Le due espansioni termiche sono famiglie indipendenti: `Alpha` è il coefficiente medio, `AlphaInstantaneous` quello istantaneo.
4. La tabella numerica è modificabile e usa sempre le unità di destinazione indicate. Ogni riga contiene `temperatura valore`, oppure `temperatura A B` per EP; usare il punto decimale. Premere **Aggiorna anteprima** dopo la modifica. Il grafico mostra XML in blu e dati del materiale selezionato in grigio. Per EP usa log10(A), con una curva per temperatura.
5. Premere **Trova associazioni**. Le proposte mostrano identità MAIN, composizione, punteggio e motivazione. Si confrontano Specification/Grade e qualificatori dichiarati, UNS esplicitamente presente nel record, composizione nominale esatta (anche con diverso ordine degli elementi), oppure riferimento originale ASME al diagramma EP. Non si deduce l'equivalenza di leghe da nomi simili.
6. **Seleziona univoche** seleziona le proposte senza altri record candidati per lo stesso materiale/famiglia. Per i casi ambigui scegliere manualmente la casella **Importa**. Il filtro cerca identità, composizione e motivazione; l'importazione riguarda le righe selezionate attualmente visibili. Un record può essere associato a più materiali. Per un gruppo grezzo verificato, i campi composizione/UNS permettono di fornire la chiave esplicita da usare nella proposta.
7. Confermare la verifica di record, unità e note. Per sostituire dati preesistenti non appartenenti alla stessa importazione, abilitare **Sostituisci esplicitamente…**. Il messaggio finale indica famiglia, numero di punti e numero di materiali prima della scrittura.

Le proposte automatiche non scrivono nel database. Se più record dello stesso folder potrebbero applicarsi allo stesso materiale/famiglia, la scelta resta manuale. Se il database cambia dopo l'anteprima, l'importazione viene rifiutata: aggiornare le associazioni e controllare di nuovo.

## XML strutturati

Esempio di formato supportato (valori illustrativi, non dati di progetto):

```xml
<Properties>
  <Record id="young-304" property="E" unit="GPa" uns="S30400"
          composition="18Cr-8Ni" description="Gruppo verificato">
    <notes>Testo integrale delle note applicabili al record.</notes>
    <Point temperature="20" value="200" />
    <Point temperature="100" value="190" />
  </Record>
  <Record id="expansion-instant" property="AlphaInstantaneous" unit="10^-6/K" uns="S30400">
    <Point temperature="20" value="16.5" />
    <Point temperature="100" value="17.1" />
  </Record>
  <Record id="density-304" property="density" unit="kg/m3" uns="S30400" constant="true">
    <Point value="7900" />
  </Record>
  <Record id="pressure-example" property="EP" chart="CS-example" unit="MPa">
    <Point temperature="150" A="1e-5" B="1.0" />
    <Point temperature="150" A="1e-4" B="10.0" />
  </Record>
</Properties>
```

Gli attributi possono anche essere elementi figli diretti; i nomi non distinguono maiuscole/minuscole. Campi di associazione opzionali: `specification`, `grade`, `condition`, `uns`, `composition`. Usare ID univoci e stabili nello stesso file. Il percorso del file contribuisce alla chiave: spostarlo crea una nuova provenienza e può richiedere la sostituzione esplicita.

Unità riconosciute: E in MPa/GPa; espansioni in `1/degC`, `1/K` o `10^-6/K`; c in `J/(kg*K)`; k in `W/(m*K)`; a in `m2/s` o `mm2/s`; densità in `kg/m3`; Nu in `dimensionless`; EP con B in MPa e A adimensionale. Le temperature sono in °C. L'importatore converte nei valori SI mostrati dall'editor; unità sconosciute e punti incompleti impediscono l'importazione numerica. Densità e Nu possono essere costanti (`constant="true"`) o curve tabulate. Il passaggio fra costante e tabella sostituisce esplicitamente la rappresentazione precedente.

## XML grezzi già presenti nel repository

Le cartelle `data/physical-properties-xml` e `data/external-pressure-xml` contengono estrazioni PDF con testo e token, non curve già validate. L'importatore propone blocchi numerici riconoscibili di TE (A istantaneo, B medio), TM, TCD e PRD, conservando il testo da verificare. Ogni blocco richiede revisione: intestazioni, gruppi, note e limiti di applicabilità possono richiedere una scelta manuale. Il coefficiente C di allungamento TE non viene trasformato in c (calore specifico).

Per i diagrammi EP grezzi gli esponenti possono essere omessi nelle righe successive del PDF. Non vengono ricostruiti automaticamente: il record è disponibile per associazione e consultazione, ma occorre inserire punti T/A/B verificati oppure fornire un XML strutturato. Nessuna proprietà mancante viene calcolata o copiata dal database 2023.

## Provenienza e aggiornamenti

L'importazione conserva documento XML completo, SHA-256 dei byte sorgenti, percorso, note e record numerico effettivamente approvato. `Xml_Import_Documents`, `Xml_Property_Associations` e `Xml_Import_History` registrano fonti, associazioni e snapshot prima/dopo. Il documento XML conservato è serializzato dal parser; l'hash identifica il file originale. Note e provenienza delle curve fisiche sono consultabili nell'editor e in `MatPropertyTable`.

Ripetere l'importazione dello stesso record aggiorna la sua curva senza duplicarla. Un lotto è transazionale: un errore annulla tutte le sue scritture. I diagrammi EP sono versionati in base al contenuto, così aggiornare un materiale non modifica il diagramma usato da altri materiali. DTD ed entità esterne XML sono disabilitate.

Excel ricarica il database dopo la chiusura della finestra. Usare `MatPropertyValue`/`MatPropertyTable` con `Alpha` o `AlphaInstantaneous`; `MatExternalPressureImportedTable(materialId)` elenca CurveId, nome diagramma, temperatura, A e B importati. Le funzioni di pressione esterna preesistenti accedono anche alle curve native attraverso il repository.
