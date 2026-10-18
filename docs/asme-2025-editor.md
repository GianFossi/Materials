# Editor ASME 2025 per CRUD ed Excel

Il database predefinito è `src/MaterialLibrary/data/asme_materials_2025.db`, nello schema normalizzato MAIN/Material_Properties/Physical_Properties. Il precedente database è archiviato in `src/MaterialLibrary/data/archive/2023/ASME_Materials_2023.db`: resta leggibile se aperto esplicitamente, ma non viene copiato nell'output o scelto come ripiego.

## Applicazione CRUD

1. Avviare `MaterialLibrary.CrudApp` su Windows con .NET 10 Desktop Runtime.
2. Premere **Proprietà ASME 2025** nella finestra principale. In alternativa: **Database… → Apri ASME 2025 → Modifica proprietà 2025**.
3. Cercare il materiale per Specification, Grade, Class/Condition/Temper o UNS e selezionarlo.
4. Scegliere Sy, Su, S1, S2, Sb1, Sb2, E, Alpha, AlphaInstantaneous, c, k, a, density o Nu.
5. Selezionare la variante dall'elenco: numero curva, product form, fascia dimensionale e tabella sorgente identificano la selezione. Le curve non sono unite né sostituite automaticamente.
6. Modificare, aggiungere o eliminare punti nella griglia. **Nuova curva** crea una variante; **Salva curva** salva solo quella selezionata; **Elimina curva** elimina solo quella selezionata.
7. Nella scheda **Costanti fisiche** inserire o cancellare densità e Poisson costanti; una casella vuota salva NULL.
8. La scheda **Identità MAIN** permette di creare/modificare l'identità e la composizione. L'unicità resta sui quattro campi, senza ProductForm.

Le note ASME generali e specifiche sono mostrate integralmente e in sola lettura. Le **Note dell'utente** sono separate. Le modifiche a una curva non alterano i documenti sorgenti, i collegamenti alle note o le altre varianti. I punti vengono salvati in una transazione; temperature duplicate, valori non finiti, intervalli dimensionali impossibili e dati cambiati esternamente sono rifiutati. Una modifica non salvata richiede salvataggio o scarto prima di cambiare contesto.

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

Il pulsante **Importa proprietà / diagrammi da XML…** apre il confronto grafico e le associazioni manuali/automatiche. Excel espone anche **Importa XML proprietà / chart** nel ribbon. Vedere la [procedura e il formato XML](xml-physical-import.md), incluse le limitazioni degli estratti PDF e la conservazione delle note.

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
