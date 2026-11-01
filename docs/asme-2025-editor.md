# Editor nativo ASME 2025

Dettagli dell'editor condiviso da WPF ed Excel. Per il percorso completo e i backup:
[app desktop](desktop-app.md). Per le tabelle sottostanti: [database](asme-materials-db.md).

## Aprire e selezionare

- Desktop: **ASME 2025 Properties**, oppure
  **Database → Manage database... → Open ASME 2025 → Edit 2025 Properties**.
- Excel: **Materiali ASME 2025 → Modifica proprietà**.

L'editor scrive solo in `.working.db`. La copia predefinita è condivisa sotto
`%LOCALAPPDATA%\MaterialLibrary\ASME2025\`; una copia esplicita esistente viene usata direttamente.
Aprire il riferimento non aggiorna una copia locale già esistente.

All'apertura della copia di lavoro l'app applica un rafforzamento idempotente (indici, regole sui valori, pulizia automatica degli orfani, vista `VW_Integrity_Report`); il riferimento non viene modificato. Dettagli in [database](asme-materials-db.md#133-rafforzamento-della-copia-di-lavoro-implementato).

1. Cercare specifica, grado, condizione o UNS e selezionare l'identità Main.
2. Scegliere la famiglia; le unità sono mostrate nell'editor.
3. Scegliere la variante dal menu, controllando CurveId, forma, dimensioni,
   inclusività, asse, fonte e numero di punti.

Seamless/Welded restano varianti distinte. **No bounds specified** significa limiti
non dichiarati, non una fusione automatica di curve. Le famiglie bolting/non-bolting
sono nascoste solo quando i dati esistenti classificano univocamente l'identità;
un'identità mista conserva entrambe.

## Modificare una curva

| Comando/campo | Effetto |
|---|---|
| **New curve** | Prepara una nuova variante della famiglia selezionata |
| **Save curve** | Valida e salva solo la variante corrente, punti e metadati |
| **Delete curve** | Elimina la variante dopo conferma, non tutto il materiale |
| **Reload** | Ricarica la famiglia; non è un ripristino di modifiche già salvate |
| Product form | Solo S1/S2 nel modello nativo |
| Min/Max e Inclusive | Limiti dimensionali in mm con estremi aperti/chiusi |
| Size axis | Dimensione cui si riferiscono i limiti, da verificare nella fonte |
| S1 stress level | **Normal Stress (no G5)** oppure **High Stress (G5 note)** |

La classificazione S1 iniziale viene dai collegamenti ufficiali alle note della curva.
L'export CSV non conserva la tipografia: non ricostruire valori alternativi mancanti
o S1H solo dall'aspetto dei numeri.

Il pannello del giunto mostra fattori/condizioni delle note; se non trova un fattore
esplicito mostra l'efficienza predefinita (inizialmente 0,70) come guida.
**Non riduce automaticamente i valori della curva.**

Valori non finiti, temperature duplicate, intervalli dimensionali invalidi e conflitti
di versione sono rifiutati. Le modifiche non salvate richiedono salvataggio o scarto
prima di cambiare contesto. Il salvataggio è transazionale e conserva le altre varianti.

## Note e limiti normativi

### Original ASME Notes

Le righe ufficiali sono bloccate finché non si seleziona **Allow edit**.
**Add**, **Copy** e **Delete** modificano una copia locale associata alla curva.
Il messaggio sopra la griglia indica se si vedono note ufficiali o copia modificata.
Le note sorgenti e i collegamenti ufficiali non vengono riscritti.

### User Notes

Testo libero separato dalle note ASME, salvato con la curva.

### ASME Code Limits

Per S1/S2/Sb1/Sb2: P-No., Group No., diagramma esterno e limiti di temperatura
per Section I, III, VIII-1, VIII-2 e XII. I dati vengono dalla **singola riga sorgente
della curva**, non dal riepilogo scalare per materiale.

- Vuoto: dato non indicato/non applicabile nella fonte; non un'autorizzazione illimitata.
- `NP`: non permesso.
- Numero: limite in °C.
- Testo qualificato, ad esempio `204 (Cl. 3 only)`: condizione da conservare e verificare.

Gli override sono in `Property_Curve_Metadata.CodeLimitsJson`. I limiti uguali alla fonte
non richiedono override. Nella proiezione al modello di libreria il limite VIII-1
(S1/Sb1) o VIII-2 (S2/Sb2) alimenta `MaximumTemperature` solo se puramente numerico.

## Costanti e identità

- **Physical Constants → Save constants:** densità kg/m3 e Poisson adimensionale;
  vuoto salva `NULL`. Il lettore usa curve tabulate della stessa famiglia, se presenti,
  prima delle costanti in `Extension`.
- **MAIN Identity → New material identity / Save identity:** crea/modifica specifica,
  grado, condizione, UNS e composizione. La combinazione dei quattro campi identificativi
  deve rimanere unica; ProductForm non ne fa parte.

Note utente, classificazione S1, asse e override restano in `Property_Curve_Metadata`;
gli snapshot delle modifiche sono in `Property_Edit_History`.

## Importazione e lettura da Excel

**Import properties / charts from XML...** apre il confronto e l'associazione esplicita.
In Excel il comando è **Importa XML proprietà / chart**. Seguire la
[guida XML](xml-physical-import.md): non tutti i dataset sono importabili.

Le funzioni native `MatPropertyFamilies`, `MatPropertyTable` e `MatPropertyValue`
conservano le varianti e non estrapolano. La chiusura dell'editor Excel ricarica la
copia utilizzata e ricalcola; **Ricarica database** rilegge il database corrente.
Formule ed esempi sono nella [guida Excel](excel-addin.md).

La libreria JSON rimane un percorso distinto: non può sovrascrivere una riga nativa
con **Write Library to DB** senza perdere la struttura delle curve, perciò questa
operazione è rifiutata.
