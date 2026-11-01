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

Oltre ai dati PRD, TM, TC e TE normalizzati descritti di seguito, le cartelle `data/physical-properties-xml` e `data/external-pressure-xml` contengono ancora estrazioni PDF con testo e token. Il lettore mantiene il supporto ai blocchi grezzi riconoscibili di TE (A istantaneo, B medio), TM, TCD e PRD, conservando il testo da verificare. Ogni blocco grezzo richiede revisione: intestazioni, gruppi, note e limiti di applicabilità possono richiedere una scelta manuale. Il coefficiente C di allungamento TE non viene trasformato in c (calore specifico).

Per i diagrammi EP grezzi gli esponenti possono essere omessi nelle righe successive del PDF. Non vengono ricostruiti automaticamente: il record è disponibile per associazione e consultazione, ma occorre inserire punti T/A/B verificati oppure fornire un XML strutturato. Nessuna proprietà mancante viene calcolata o copiata dal database 2023.

## PRD: densità e coefficiente di Poisson per materiale

[PRD-Density.xml](../database/data/physical-properties-xml/Density/PRD-Density.xml) contiene 158 voci materiale ricavate dalle 115 righe della Table PRD ASME II-D Metric 2025 (pagine PDF 1244-1245). Gli elenchi UNS espliciti sono espansi in voci distinte; le famiglie generiche restano gruppi con il nome originale. Le voci sono ordinate per categoria, sottogruppo e nome. `name`, `sourceTable`, `sourcePdf`, pagine, riferimento e note restano in testa.

Il formato compatto `schema="material-constants-v1"` usa una sola riga per materiale. Le unità sono dichiarate una volta in testa: `densityUnit="kg/m3"` e `poissonRatioUnit="dimensionless"`. Ogni `Material` conserva `id`, `name`, `category`, eventuale `group` e `uns`, `sourceMaterial` e `sourcePdfPage`, con i valori negli attributi `density` e `poissonRatio`. Non sono presenti separatori delle migliaia o token impliciti. La tabella non dichiara una temperatura per queste costanti.

Esempio di voce:

```xml
<Material id="S15500" name="S15500" uns="S15500" association="uns"
          category="Ferrous Materials" sourceMaterial="PH stainless: S15500, S17400, S17700"
          sourcePdfPage="1244" density="7750" poissonRatio="0.31" />
```

L'associazione automatica usa l'UNS esplicito. Il lettore supporta anche `nominalChemicalComposition="..."` come chiave di composizione esatta, normalizzata con le stesse regole di `composition` negli XML strutturati. Nessuna composizione viene dedotta dall'UNS o da descrizioni generiche/intervalli Cr. Le 16 voci senza UNS restano `association="manual"`: il lettore le segnala come da verificare e richiedono associazione manuale. `association` è informativo; il lettore determina la disponibilità delle chiavi dai valori effettivi di UNS/composizione.

Il lettore CRUD espande le voci in 158 densità e 158 coefficienti di Poisson costanti, conservando gli UNS espliciti, il documento originale e le note comuni con riferimento alla riga/pagina. Le chiavi `prd-density-{id}` e `prd-poisson-{id}` restano quelle del precedente formato: la semplificazione non cambia la provenienza dei record. Valori mancanti, non finiti, densità non positive, Poisson fuori dall'intervallo aperto (0, 0.5), unità non valide e ID duplicati producono errori espliciti.

L'estrazione grezza Poisson separata è stata eliminata dopo aver verificato che tutte le 115 coppie originali sono presenti nel file unificato. Nell'indice entrambe le quantità puntano allo stesso XML. L'eventuale temperatura interna del punto costante è una convenzione del lettore, non un dato dichiarato dalla Table PRD. Verificare applicabilità e associazioni prima della scrittura nel database; rispetto al vecchio file Poisson eliminato, la nuova provenienza può richiedere la sostituzione esplicita di un'importazione precedente.

## TM-1–TM-5: curve del modulo elastico per materiale

I cinque XML nella cartella [ElasticModulus](../database/data/physical-properties-xml/ElasticModulus/) sono normalizzati dalle tabelle ASME BPVC II-D Metric 2025, Part 2 (pagine PDF 1238-1244). `schema="material-curves-v1"` conserva le curve una volta sola: ciascuna può avere più identità `Material`, associate per UNS o `nominalChemicalComposition` esatta.

| Tabella | Righe fonte | Curve condivise | Identità materiale | Punti unici |
|---|---:|---:|---:|---:|
| TM-1 | 19 | 17 | 104 | 241 |
| TM-2 | 18 | 4 | 18 | 28 |
| TM-3 | 47 | 11 | 47 | 77 |
| TM-4 | 36 | 23 | 36 | 417 |
| TM-5 | 15 | 5 | 15 | 38 |
| Totale | 135 | 60 | 220 | 801 |

Nome tabella, fonte, pagine, note e unità restano in testa. Ogni `Curve` dichiara `property="E"`, `unit="MPa"` e `temperatureUnit="degC"`, con punti numerici `temperature`/`value`. I valori fonte, espressi in migliaia di MPa, sono moltiplicati esplicitamente per 1000. Le celle mancanti non sono trasformate in zero, interpolate o estrapolate.

```xml
<Curve id="example" property="E" unit="MPa" temperatureUnit="degC">
  <Materials>
    <Material id="first" name="Identità UNS" uns="S30400" />
    <Material id="second" name="Identità per composizione"
              nominalChemicalComposition="18Cr-8Ni" />
  </Materials>
  <Points>
    <Point temperature="25" value="195000" />
    <Point temperature="100" value="189000" />
  </Points>
</Curve>
```

Questo esempio illustra il formato: le identità nei file distribuiti derivano esclusivamente dalla fonte. Per TM-1 le composizioni dei gruppi A–J provengono dalle note applicabili, con testo originario, numero nota e pagina conservati nelle voci. Le frazioni PDF sono ripristinate come frazioni numeriche esplicite; il matching riconosce ad esempio `½Mo` e `0.5Mo`, senza equiparare `Mo` a `1Mo` o correggere composizioni corrotte nel database (`?`). Se un'identità dichiara sia UNS sia composizione, l'UNS resta un qualificatore obbligatorio: una composizione simile non autorizza un UNS diverso.

Le tre famiglie TM-1 senza identità esatta (carbonio ≤0.30%, carbonio >0.30%, ghisa sferoidale) rimangono ad associazione manuale. L'intervallo di carbonio non è una composizione nominale esatta. Le variazioni di 9Cr–Mo citate dalla nota non ricevono equivalenze inventate. Le note 8 e 9 ripetono i membri del gruppo H; la curva rimanda espressamente alla nota 9. La nota 13 di S15500 dichiara `15Cr–5Ni–3Mo`: il valore fonte è preservato e segnalato, non sostituito con un alias del database. S45000 richiede verifica della condizione di invecchiamento a 480 °C. TM-5 conserva la limitazione al modulo longitudinale della lamiera lavorata e il riferimento A-804.

Il lettore espande le 60 curve in 220 record associabili, con chiavi `{percorso}/{curva-id}/{materiale-id}`, note e XML originale. Un singolo record può essere importato in più materiali; se più identità competono per lo stesso materiale/proprietà, resta necessaria una scelta manuale. Coordinate non finite/non positive, temperature sotto lo zero assoluto o non strettamente crescenti, unità errate e ID mancanti/duplicati producono errori espliciti.

## TCD: conduttività termica per materiale

[TCD-ThermalConductivity.xml](../database/data/physical-properties-xml/ThermalConductivity/TCD-ThermalConductivity.xml) normalizza tutte le 65 colonne TC della Table TCD ASME II-D Metric 2025 (pagine PDF 1224-1235; note 1236-1237). Il formato `material-curves-v1` riusa 63 curve, con 1361 punti unici e 178 identità materiale. Tutti i 1390 punti delle colonne fonte sono preservati; le curve identiche non sono duplicate.

Ogni curva dichiara `property="k"`, `unit="W/(m*K)"` e `temperatureUnit="degC"`. I valori ASME in W/(m·°C) restano numericamente invariati in W/(m·K). I punti hanno attributi numerici `temperature` e `value`; nessuna cella mancante è trasformata in zero, interpolata o estrapolata. Il file di diffusività TD rimane separato e invariato.

Le identità includono 108 composizioni nominali esplicite dalle note dei gruppi A–L e 69 UNS. Un'unica famiglia (ghisa sferoidale) richiede associazione manuale. La nota 1 definisce il gruppo A tramite il valore esatto `Carbon steel` nella colonna Nominal Composition: questa chiave è quindi associabile senza inventare percentuali. I gradi di titanio sono associati agli UNS solo mediante le coppie esplicite della Table TM-5, pagina PDF 1244; la fonte aggiuntiva è dichiarata negli attributi `identitySourceTable`/`identitySourcePdfPage`. Nessuna composizione quantitativa è dedotta da un UNS o da denominazioni qualitative delle leghe.

Le note 9–11 limitano quattro composizioni a **425 °C**: `15Cr-5Ni-3Mo`, `15Cr-6Ni-Cu-Mo`, `17Cr-7Ni-1Al`, `13Cr-8Ni-2Mo`. Ogni identità conserva `maxTemperature="425"` e l'applicabilità. La curva fonte rimane completa nel documento, ma il lettore produce per quella identità solo punti entro il limite. Limiti non validi o che escludono tutti i punti generano un errore esplicito. Chi usa direttamente l'XML deve rispettare `maxTemperature`; il limite non autorizza extrapolazione oltre l'ultimo punto disponibile.

Sono mantenuti l'incertezza **±10%** della General Note (b), il testo originale delle composizioni, le note sui gradi H/L/N/LN e la variante Se esplicitamente ammessa dalla nota 10. `15Cr-5Ni-3Mo` resta la composizione stampata nella fonte, senza sostituirla con un alias del database. Il lettore genera record `k` associabili per UNS/composizione esatta; ciascuno può essere importato in più materiali. Le ambiguità restano da confermare manualmente.

## TE-1–TE-5: dilatazione termica per materiale

Tutti i cinque XML in [ThermalExpansion](../database/data/physical-properties-xml/ThermalExpansion/) sono normalizzati dalla Table TE-1–TE-5 ASME II-D Metric 2025, Part 2 (pagine PDF 1206-1223; introduzione 1205).

| Tabella | Curve A/B | Record materiale/proprietà |
|---|---:|---:|
| TE-1 | 30 | 210 |
| TE-2 | 2 | 36 |
| TE-3 | 10 | 28 |
| TE-4 | 60 | 74 |
| TE-5 | 6 | 26 |
| Totale | 108 | 374 |

Il formato condiviso `material-curves-v1` mantiene due curve distinte per ogni gruppo: A → `AlphaInstantaneous` (coefficiente istantaneo), B → `Alpha` (coefficiente medio da 20 °C alla temperatura indicata). I valori sono moltiplicati per `1E-6`, in **1/degC**, con temperature in **degC**. La temperatura di riferimento **20 °C** è dichiarata nella radice e in ogni curva, richiesta dal lettore e conservata nelle note d'importazione. Non viene aggiunto alcun nuovo calcolo o interpolatore.

C è la dilatazione lineare in **mm/m**, sempre riferita a 20 °C: viene conservata nei nodi `SourceLinearExpansion/Value`, collegati tramite `linearExpansionRef` delle curve A/B. Non è un coefficiente Alpha e non viene importata come `c` (calore specifico). Zero in C a 20 °C resta zero; celle mancanti restano assenti. Il lettore importa solo le curve A/B, non ricalcola C e non estrapola.

Sono conservati fonte, pagine, note, composizioni originali e l'incertezza **±10%** dell'introduzione. Le composizioni TE-1 provengono dagli elenchi ASME dei gruppi 1–4; TE-2/TE-4 usano gli UNS espliciti; TE-5 usa solo le coppie grado/UNS dichiarate in TM-5, pagina PDF 1244. Le famiglie di rame TE-3 hanno `unsPattern` esplicito (`C1XXXX`, ecc.) ma restano ad associazione **manuale**: non sono UNS esatti e il matching non inventa leghe. C97600 e C96200, esplicitamente dichiarati nelle note, sono associabili per UNS.

Le curve per `17Cr-4Ni-4Cu` nelle condizioni 1075 e 1150 mantengono la condizione in `applicability` e richiedono revisione manuale; non vengono equiparate automaticamente a un diverso stato metallurgico. Le due condizioni possono proporre la stessa composizione, quindi l'ambiguità resta esplicita. Un record può essere associato a più materiali; UNS e composizione nominale esatta usano le stesse regole degli altri XML normalizzati.

## Figura G: diagramma geometrico

Il file [G.xml](../database/data/external-pressure-xml/G.xml) contiene le 24 curve e i 393 punti della Table G ASME II-D Metric 2025 (pagine PDF 1326-1327, pagine stampate 1276-1277). Le figure sono alle pagine PDF 1247-1248. Il file è registrato nell'indice con `kind="geometric"`.

La radice è `ExternalPressureGeometricChart`. Ogni `Curve` ha un `diameterThicknessRatio` (Do/t) costante; ogni `Point` contiene `lengthDiameterRatio` (L/Do) e `A`. Tutti questi valori sono adimensionali. A è sempre espresso in notazione scientifica con esponente esplicito. Le continuazioni fra colonne e pagine sono riunite nella stessa curva.

Non si tratta di curve del materiale T/A/B: non sono presenti temperatura o fattore B, e il file non è un record `property="EP"` importabile come proprietà del materiale. I punti sono leggibili direttamente da un parser XML per un successivo calcolo geometrico; questo intervento non aggiunge un interpolatore o funzioni di calcolo. Verificare applicabilità e dati prima dell'uso ingegneristico; l'estrapolazione non è consentita salvo quanto espressamente ammesso dal Construction Code applicabile.

## Diagrammi dei Code Cases 2025

L'[indice Code Cases](../database/data/external-pressure-xml/code-cases-index.xml) registra 17 diagrammi numerici individuati nella scansione dell'intero volume locale (1584 pagine): 95 curve e 1955 punti. Le versioni customary/metric dello stesso diagramma non sono duplicate; quando disponibile viene usata la tabella metrica. I rimandi a figure già presenti nella Section II-D non generano copie aggiuntive.

| Case | XML | Dati |
|---|---|---|
| 2146 | [CC-2146.xml](../database/data/external-pressure-xml/CC-2146.xml) | S34565, A/B per temperatura |
| 2244-2 | [2244-2.xml](../database/data/external-pressure-xml/2244-2.xml) | S32760/J93380, A/B per temperatura; sostituisce la precedente trascrizione PVElite |
| 2430 | [CC-2430.xml](../database/data/external-pressure-xml/CC-2430.xml) | S31060, A/B per temperatura |
| 2458-3 | [CC-2458-3.xml](../database/data/external-pressure-xml/CC-2458-3.xml) | S31277, A/B per temperatura |
| 2671-2 | [CC-2671-2.xml](../database/data/external-pressure-xml/CC-2671-2.xml) | N06230, A/B a 982 °C |
| 2676 | [CC-2676.xml](../database/data/external-pressure-xml/CC-2676.xml) | 2.25Cr-1Mo ricotto, A/B a 538 °C per durata |
| 2682 | [CC-2682.xml](../database/data/external-pressure-xml/CC-2682.xml) | S30815, A/B a 900 °C per durata |
| 2684-5 | [CC-2684-5.xml](../database/data/external-pressure-xml/CC-2684-5.xml) | N06674, A/B per temperatura |
| 2794 | [CC-2794.xml](../database/data/external-pressure-xml/CC-2794.xml) | N06845, A/B per temperatura |
| 2831-1 | [CC-2831-1.xml](../database/data/external-pressure-xml/CC-2831-1.xml) | S34565, A/B per temperatura |
| 2964 | `CC-2964-II-1.xml` … `CC-2964-II-7.xml`, elencati nell'indice | Sette diagrammi A/Sc per durata e temperatura/intervallo |

La radice `ExternalPressureCodeCaseChart` distingue questi dati dalle proprietà importabili automaticamente. Gli attributi `codeCase`, `figure`, `material`, `figurePdfPages`, `tablePdfPages` e `ordinate` identificano la fonte e la quantità. Le `Curve` conservano la temperatura o l'intervallo, la durata in `durationHours` oppure `curveKind="hot-tensile"`; `RT` resta `temperatureQualifier="roomTemperature"` senza inventare una temperatura numerica. `upTo` conserva il limite superiore della fonte.

I `Point` hanno A adimensionale e B o Sc in MPa, con esponenti espliciti. `sourceOrdinate`, `sourceOrdinateUnit`, `sourceHeaderOrdinateUnit` e `sourcePdfPage` conservano il valore originale e la provenienza; `psiToMPa` esplicita la conversione usata. **Sc non è B**: le tabelle del Case 2964 richiedono l'applicazione separata della procedura del Code Case. Questi XML non aggiungono interpolazione, formule, associazioni o importazione numerica automatica nel CRUD.

Le anomalie della fonte sono conservate e segnalate in `Diagnostics/Warning`: il Case 2146 presenta un punto non monotono a 800 °F; il Case 2244-2 riporta A=0.01 dopo A=0.05 a 70/200 °F; il titolo della colonna metrica del Case 2794 indica psi, mentre la Figura 1M indica MPa. In quest'ultimo caso `sourceHeaderOrdinateUnit="psi"` mantiene il titolo originale e `sourceOrdinateUnit="MPa"` indica l'unità utilizzata, da verificare. Non vengono corretti valori per supposizione o ordinate mancanti sostituite con zero. Tutti i record richiedono verifica della fonte, delle restrizioni e dell'accettazione del Code Case prima dell'uso ingegneristico.

I record PVElite 2245-1 e 2497-2 restano separati: questi identificativi non sono stati trovati come casi con diagrammi dedicati nel volume esaminato.

## Provenienza e aggiornamenti

L'importazione conserva documento XML completo, SHA-256 dei byte sorgenti, percorso, note e record numerico effettivamente approvato. `Xml_Import_Documents`, `Xml_Property_Associations` e `Xml_Import_History` registrano fonti, associazioni e snapshot prima/dopo. Il documento XML conservato è serializzato dal parser; l'hash identifica il file originale. Note e provenienza delle curve fisiche sono consultabili nell'editor e in `MatPropertyTable`.

Ripetere l'importazione dello stesso record aggiorna la sua curva senza duplicarla. Un lotto è transazionale: un errore annulla tutte le sue scritture. I diagrammi EP sono versionati in base al contenuto, così aggiornare un materiale non modifica il diagramma usato da altri materiali. DTD ed entità esterne XML sono disabilitate.

Excel ricarica il database dopo la chiusura della finestra. Usare `MatPropertyValue`/`MatPropertyTable` con `Alpha` o `AlphaInstantaneous`; `MatExternalPressureImportedTable(materialId)` elenca CurveId, nome diagramma, temperatura, A e B importati. Le funzioni di pressione esterna preesistenti accedono anche alle curve native attraverso il repository.
