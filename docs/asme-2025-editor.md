# Editor ASME 2025 per CRUD ed Excel

Il database predefinito è `src/MaterialLibrary/data/asme_materials_2025.db`, nello schema normalizzato MAIN/Material_Properties/Physical_Properties. Il precedente database è archiviato in `src/MaterialLibrary/data/archive/2023/ASME_Materials_2023.db`: resta leggibile se aperto esplicitamente, ma non viene copiato nell'output o scelto come ripiego.

## Applicazione CRUD

1. Avviare `MaterialLibrary.CrudApp` su Windows con .NET 10 Desktop Runtime.
2. Premere **ASME 2025 Properties** nella finestra principale. In alternativa: **Database… → Open ASME 2025 → Edit 2025 Properties**.
3. Cercare il materiale per Specification, Grade, Class/Condition/Temper o UNS e selezionarlo.
4. Scegliere Sy, Su, S1, S2, Sb1, Sb2, E, Alpha, AlphaInstantaneous, c, k, a, density o Nu.
5. Select a variant from the dropdown. Each entry shows the curve ID, product form, size bounds and inclusivity, axis, source table, and point count. SMLS/WLD variants are identified as **Seamless**/**Welded** while retaining their source abbreviations. Curves with the same thickness range but different product forms remain separate. Unbounded ranges are labelled **no bounds specified**; curves are never merged or replaced automatically.
6. For **S1**, verify or change the explicit **Normal Stress (no G5)** / **High Stress (G5 note)** classification. The initial value is inferred from the curve's specific source-note links, can be explicitly selected, and is saved in working-copy metadata.
7. For welded **S1/S2** product forms, the guidance panel shows any joint-efficiency factors stated in the original notes and warns users to check their conditions and limits. When no explicit factor is found, it shows the configured default efficiency (initially **0.70**) as a reference only. It never changes or automatically reduces curve values.
8. Edit, add, or delete points in the grid. **New curve** creates a variant; **Save curve** saves only the selected variant; **Delete curve** deletes only that variant.
9. The **Original ASME Notes** tab lists the general and specific ASME notes linked to the curve, one row per note (source table, code, text). It is read-only until **Allow edit** is checked. Then **Add** inserts a blank row, **Copy** adds the note selected in the dropdown (relevant ASME tables only), and **Delete** removes the selected row. Edited rows are saved with the curve as a curve-level copy in `Property_Curve_Metadata.UserNoteItemsJson`. The official `Source_Notes` and note-link tables are never modified, and rows identical to the official notes are not stored. A message above the grid shows whether the curve displays the official notes or its edited copy.
10. The **User Notes** tab is a single free-text box saved in `Property_Curve_Metadata.UserNotes`, separate from the ASME notes.
11. For **S1/S2/Sb1/Sb2**, the **ASME Code Limits** tab shows the welding **P-No.** and **Group No.**, the external-pressure chart, and the maximum allowable temperature (°C) for Section I, III, VIII-1, VIII-2 and XII. Values are read per curve from the specific ASME table row (`Property_Source` → `ASME_Data_Source`), not from the flattened per-material `ASME_Data` table. A blank field means not applicable or not listed; **NP** means not permitted; a qualifier is allowed (for example `204 (Cl. 3 only)`). The P-No. and Section VIII limit also appear in the variant dropdown. Edits are saved in `Property_Curve_Metadata.CodeLimitsJson`, which is NULL when the values match the official row. During hydration, the dataset's `MaximumTemperature` is taken from VIII-1 (S1/Sb1) or VIII-2 (S2/Sb2) when it is a plain number.
12. In **Physical Constants**, enter or clear constant density and Poisson ratio; a blank field saves NULL.
13. **MAIN Identity** creates or edits the identity and composition. Uniqueness remains based on the four identity fields, not ProductForm. Sb1/Sb2 or S1/S2 families are hidden only when existing curves unambiguously identify the material as bolting or non-bolting; identities containing both categories retain both sets.

Product-form values and the default joint efficiency can be edited in **Options → Application options...**. See [CRUD Application Options and User Guidance](./crud-app-options.md) for XML storage, file-picker defaults, and field guidance.

Le note ASME generali e specifiche ufficiali restano immutate nelle tabelle sorgenti; le modifiche fatte nella scheda Original ASME Notes valgono solo per la curva. La classificazione S1 Normal/High e i riferimenti di calcolo continuano a derivare dai collegamenti ufficiali. Le righe legacy con tabella `User` vengono considerate note utente libere e non note originali. Le modifiche a una curva non alterano i documenti sorgenti, i collegamenti alle note o le altre varianti. I punti e i metadati vengono salvati in una transazione; temperature duplicate, valori non finiti, intervalli dimensionali impossibili e dati cambiati esternamente sono rifiutati. Una modifica non salvata richiede salvataggio o scarto prima di cambiare contesto.

L'editor nativo opera sui dati normalizzati. L'editor dei documenti JSON resta disponibile per librerie complete con modelli di creep, fatica e altri dati; l'esportazione di un documento JSON sopra una riga nativa viene rifiutata per evitare di perdere identità e provenienza delle curve.

## Excel

Compilare/caricare l'add-in per l'architettura di Excel. La scheda della barra multifunzione **Materiali ASME 2025 → Modifica proprietà** apre lo stesso editor. Alla chiusura ricarica la copia di lavoro e ricalcola le formule. **Ricarica database** permette di rileggere modifiche fatte esternamente. Anche Excel richiede .NET 10 Desktop Runtime dell’architettura corrispondente (x86 per Excel a 32 bit, x64 per Excel a 64 bit), perché ospita l’editor WPF.

Funzioni native:

```text
=MatPropertyFamilies()
=MatPropertyTable("123";"S1")
=MatPropertyValue("123";"S1";200;25;"Plate";456)
=MatPropertyTable("123";"Sb1")
=MatPropertyTable("123";"Sb2")
```

Gli ID nell'esempio sono segnaposto. `MatSearch` restituisce gli ID MAIN reali. `MatPropertyTable` restituisce CurveId, famiglia, product form, estremi dimensionali e inclusività, asse, temperatura, valore, unità, tabella di origine e testo completo delle note. `MatPropertyValue(materialId; propertyCode; temperatureC; sizeMm; productForm; curveId)` permette di omettere gli ultimi tre argomenti quando la variante è univoca. Le costanti density/Nu sono lette da Extension se non ci sono curve tabulate della stessa famiglia.

Una ricerca ambigua restituisce un messaggio: scegliere dimensione, product form e/o CurveId. Non viene scelta la prima curva. La temperatura deve essere nel campo della curva; non si estrapola. S1/S2 non vengono sostituite da dati bolting, e Sb1/Table 3 resta distinta da Sb2/Table 4. S1H non è dedotta dalla tipografia persa negli export CSV. Le funzioni preesistenti restano disponibili; per leggere tutte le varianti e scegliere esplicitamente usare le funzioni native. I materiali caricati da JSON mantengono la precedenza nelle funzioni preesistenti.

## Importazione XML

The **Import properties / charts from XML...** button opens the graphical comparison and manual/automatic association workflow. Excel also exposes **Import XML Properties / Chart** in the ribbon. See the [XML workflow and format](xml-physical-import.md), including PDF-extract limitations and note preservation.

## File e unità

CRUD ed Excel condividono normalmente `%LOCALAPPDATA%\MaterialLibrary\ASME2025\asme_materials_2025.working.db`. Viene creata al primo utilizzo con il backup SQLite del riferimento; le successive aperture conservano le modifiche. Una copia di lavoro selezionata esplicitamente viene utilizzata direttamente. Il file di riferimento e il database archiviato non vengono modificati dall'editor. Le scritture native richiedono un nome terminante in `.working.db`.

Se si vuole iniziare nuovamente dal riferimento aggiornato, chiudere CRUD ed Excel, conservare una copia della propria `.working.db`, quindi rinominarla/spostarla: la prossima apertura ne crea una nuova. Le modifiche personali non vengono eliminate automaticamente quando cambia il file di riferimento.

| Famiglia | Unità |
| --- | --- |
| Sy, Su, S1, S2, Sb1, Sb2, E | MPa |
| Alpha | 1/°C, coefficiente medio |
| AlphaInstantaneous | 1/°C, coefficiente istantaneo |
| c | J/(kg·K) |
| k | W/(m·K) |
| a | m²/s |
| density | kg/m³ |
| Nu | adimensionale |

Temperature in °C e dimensioni in mm, con estremi aperti/chiusi conservati. ProductForm si applica soltanto a S1/S2. Le proprietà fisiche non contenute nei file 2025 restano assenti fino all'inserimento di dati verificati; nessun valore viene copiato dal 2023.

`Property_Edit_History` conserva gli snapshot delle modifiche alle curve; `Property_Curve_Metadata` conserva asse dimensionale modificato e note utente. I file/source notes dell'importazione restano immutati. Lo schema dei documenti JSON passa a 17 per distinguere Sb2 e le note Table 4; le versioni 15 e 16 restano leggibili.

## Compilazione e verifica

```powershell
dotnet test tests/MaterialLibrary.Tests
dotnet test tests/MaterialLibrary.DatabaseUpdater.Tests
dotnet build src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.csproj -c Release
dotnet build src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj -c Release -p:Platform=x86
dotnet build src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj -c Release -p:Platform=x64
```

I test WPF e la verifica interattiva in Excel richiedono Windows. Su Linux è possibile compilare con `-p:EnableWindowsTargeting=true`, ma non eseguire WPF o Excel.
