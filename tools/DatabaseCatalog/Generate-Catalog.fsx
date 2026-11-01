// Author: Claude
// Last revision: 2026-11-01
// Purpose: Regenerate chapter 2 (table catalog) of docs/asme-materials-db.md from the real SQLite schema, built through the project's own code.
//
// How it works (no hand-maintained column lists):
//   1. copies the reference database to a temporary .working.db copy;
//   2. runs the real code that creates every working-copy object: Asme2025Crud.ensureSchema (editor tables + hardening),
//      SqlAudit.enable (optional change log) and one XML probe import (Xml_* tables);
//   3. reads the schema of the reference and of that copy (PRAGMA table_info/foreign_key_list, sqlite_master);
//   4. writes the chapter between the catalog markers of the documentation file.
// Only purposes, unit notes and trigger descriptions below are written by hand.
// Run it through tools/Generate-DatabaseCatalog.ps1, which builds the Crud project into tools/DatabaseCatalog/bin first.
// Options: --reference <db> --doc <markdown> [--check]  (--check writes nothing and exits with 1 when the chapter is out of date).
// Units: temperatures degC, stresses MPa, sizes mm (documented in the generated text).

#r "bin/MaterialLibrary.dll"
#r "bin/MaterialLibrary.Crud.dll"
#r "nuget: Microsoft.Data.Sqlite, 10.0.13"

open System
open System.Globalization
open System.IO
open System.Text.RegularExpressions
open Microsoft.Data.Sqlite
open MaterialLibrary.Crud
open MaterialLibrary.Domain.Database.Lookup

let beginMarker = "<!-- catalog:begin"
let endMarker = "<!-- catalog:end -->"

/// <summary>Value of a named command line option (--reference, --doc).</summary>
let option name =
    let args = fsi.CommandLineArgs |> Array.skip 1
    match args |> Array.tryFindIndex ((=) name) with
    | Some i when i + 1 < args.Length -> args[i + 1]
    | _ -> failwithf "Missing option %s" name

let referencePath = Path.GetFullPath(option "--reference")
let docPath = Path.GetFullPath(option "--doc")

/// <summary>Execute a read query and return untyped rows.</summary>
let query (db: SqliteConnection) (sql: string) : obj[] list =
    use command = db.CreateCommand()
    command.CommandText <- sql
    use reader = command.ExecuteReader()
    [ while reader.Read() do yield Array.init reader.FieldCount (fun i -> reader.GetValue i) ]

/// <summary>Text of a nullable cell.</summary>
let str (v: obj) = if isNull v || Convert.IsDBNull v then "" else string v

/// <summary>Italian thousands separators, as used in the rest of the documentation.</summary>
let thousands (n: int64) = n.ToString("N0", CultureInfo("it-IT"))

let open' (path: string) readOnly =
    let mode = if readOnly then SqliteOpenMode.ReadOnly else SqliteOpenMode.ReadWrite
    let db = new SqliteConnection(SqliteConnectionStringBuilder(DataSource = path, Mode = mode, ForeignKeys = Nullable true, Pooling = false).ConnectionString)
    db.Open()
    db

// ---------------------------------------------------------------------------------------------------------------
// 1-2. Build a working copy with every object the project can create
// ---------------------------------------------------------------------------------------------------------------
let temp = Path.Combine(Path.GetTempPath(), "catalog-" + Guid.NewGuid().ToString("N"))
Directory.CreateDirectory temp |> ignore
let workPath = Path.Combine(temp, "catalog.working.db")
File.Copy(referencePath, workPath)

let work = open' workPath false
Asme2025Crud.ensureSchema work
SqlAudit.enable work |> Result.defaultWith failwith

// XML probe: creates Xml_Import_Documents / Xml_Property_Associations / Xml_Import_History through the real importer.
let probeFolder = Path.Combine(temp, "probe")
Directory.CreateDirectory probeFolder |> ignore
File.WriteAllText(Path.Combine(probeFolder, "probe.xml"),
    """<Properties><Record id="catalog-probe" property="E" unit="GPa" specification="SA-516" grade="70" description="Catalog probe"><Point temperature="20" value="200"/><Point temperature="100" value="190"/></Record></Properties>""")
let probeRecords, probeErrors = XmlPropertyReader.readFolder probeFolder
if probeErrors.Length > 0 then failwithf "Probe XML unreadable: %A" probeErrors
let probeProposal = XmlPropertyMatching.propose work probeRecords |> Array.head
let probe = { MaterialId = probeProposal.MaterialId; Record = probeRecords[0]; Reviewed = true; ReplaceExisting = true
              ExpectedVersion = XmlPropertyImport.version work probeProposal.MaterialId "E" }
XmlPropertyImport.apply workPath [| probe |] |> Result.defaultWith failwith |> ignore

let reference = open' referencePath true

// ---------------------------------------------------------------------------------------------------------------
// 3. Read both schemas
// ---------------------------------------------------------------------------------------------------------------
type Obj = { Kind: string; Name: string; Table: string; Sql: string }

let objects (db: SqliteConnection) =
    query db "SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY rowid"
    |> List.map (fun r -> { Kind = str r[0]; Name = str r[1]; Table = str r[2]; Sql = str r[3] })

let refObjs = objects reference
let workObjs = objects work
let isRef (kind, name) = refObjs |> List.exists (fun o -> o.Kind = kind && o.Name = name)
let refTables = refObjs |> List.filter (fun o -> o.Kind = "table") |> List.map (fun o -> o.Name)
let refViews = refObjs |> List.filter (fun o -> o.Kind = "view") |> List.map (fun o -> o.Name)
let workOnlyTables = workObjs |> List.filter (fun o -> o.Kind = "table" && not (isRef (o.Kind, o.Name))) |> List.map (fun o -> o.Name)
let workOnlyViews = workObjs |> List.filter (fun o -> o.Kind = "view" && not (isRef (o.Kind, o.Name))) |> List.map (fun o -> o.Name)
let isAudit (o: Obj) = o.Name.StartsWith "Audit_"
/// <summary>Indexes and triggers added by the hardening (not by SqlAudit, not belonging to a working-copy-only table).</summary>
let hardeningAdded =
    workObjs |> List.filter (fun o -> (o.Kind = "index" || o.Kind = "trigger") && not (isRef (o.Kind, o.Name)) && not (isAudit o) && not (List.contains o.Table workOnlyTables))
let dropped = refObjs |> List.filter (fun o -> (o.Kind = "index" || o.Kind = "trigger") && not (workObjs |> List.exists (fun w -> w.Kind = o.Kind && w.Name = o.Name)))

// ---------------------------------------------------------------------------------------------------------------
// Hand-written knowledge: groups, purposes, notes, trigger descriptions
// ---------------------------------------------------------------------------------------------------------------
let groups : (string * string list) list =
    [ "Identità e provenienza dell'identità", [ "Main"; "Material_Identity"; "Material_Source" ]
      "Curve meccaniche", [ "Sizes"; "Material_Properties"; "Property_Values"; "Property_Source"; "Sb1"; "Sb2" ]
      "Note", [ "Source_Notes"; "Property_Note_Links"; "Property_Notes" ]
      "Proprietà fisiche e costanti", [ "Physical_Properties"; "Physical_Property_Values"; "Extension" ]
      "Pressione esterna", [ "External_Pressure_Charts"; "Material_EP_Charts"; "EP_Chart_Curves"; "EP_Chart_Points" ]
      "Applicabilità normativa", [ "ASME_Data"; "ASME_Data_Source" ]
      "Audit dell'importazione ASME", [ "Source_Documents"; "Import_Files"; "Import_Omitted_Rows"; "Database_Metadata" ]
      "Modelli previsti ma non popolati", [ "Stress_Strain_Curves"; "Stress_Strain_Points"; "Creep_Rupture_Data"; "Larson_Miller_Parameters"; "Larson_Miller_Curve_Points"; "Stress_Strain_Model_Parameters"; "Cyclic_Curve_Header"; "Cyclic_Curve_Values"; "Omega_Method_Parameters" ]
      "Provenienza dell'importazione XML", [ "Xml_Import_Documents"; "Xml_Property_Associations"; "Xml_Import_History" ]
      "Tabelle delle copie di lavoro", [ "Property_Curve_Metadata"; "Property_Edit_History"; "Sql_Change_Log" ] ]

/// <summary>Purpose and writer of each table.</summary>
let purpose : Map<string, string * string> =
    Map.ofList [
      "Main", ("""Un materiale = una riga: l'identità a quattro campi (specifica, grado, classe/condizione/tempra, UNS) più composizione nominale e i minimi scalari. È il perno del database: ogni altra tabella dei dati del materiale vi si collega con `id_main`. Forma di prodotto, spessore e variante della curva **non** creano nuove righe.""", "Importatore ASME (DatabaseUpdater); editor (identità) nelle copie di lavoro.")
      "Material_Identity", ("""Chiave naturale testuale del materiale, array JSON `[Specification, Grade, ClassConditionTemper, UNS]` in maiuscolo, per confrontare identità fra edizioni e garantirne l'unicità. Una riga per `Main`.""", "Importatore ASME; editor (identità).")
      "Material_Source", ("""Righe CSV del documento ASME da cui ha origine ogni identità (anche più righe per un materiale). Conserva la riga grezza e un'impronta per ricostruire e confrontare l'importazione.""", "Importatore ASME.")
      "Sizes", ("""Intervalli di spessore/dimensione (mm) con estremi inclusi o esclusi. Le righe sono **condivise** da più curve (277 su 331 nel riferimento) e non hanno chiave naturale: per cambiare l'intervallo di una curva si crea una riga nuova e si ricollega la curva.""", "Importatore ASME; editor (una riga nuova per ogni salvataggio di curva).")
      "Material_Properties", ("""Testata di una **curva meccanica**: materiale, famiglia (`Sy`, `Su`, `S1`, `S2`, `Sb1`, `Sb2`), intervallo di spessore e, solo per S1/S2, forma di prodotto. È l'unità che si legge, si modifica e si cancella.""", "Importatore ASME; editor.")
      "Property_Values", ("""Punti di una curva meccanica: una coppia temperatura (°C) / tensione (MPa) per riga.""", "Importatore ASME; editor (riscrive tutti i punti della curva a ogni salvataggio).")
      "Property_Source", ("""Provenienza di una curva importata: tabella ASME, file e riga CSV, pagina, riga stampata (`RawFieldsJson`), asse dimensionale e minimi di snervamento/rottura della riga. I limiti applicabili alla curva si leggono qui, non in `ASME_Data`. Le curve create dall'utente non hanno questa riga (tranne Sb1/Sb2, per il trigger).""", "Importatore ASME; editor solo per Sb1/Sb2 nuove (riga con `Fingerprint = 'User'`).")
      "Sb1", ("""Marca le curve `Material_Properties` che sono bolting della Table 3 e ne ripete materiale e intervallo (dedicata e distinta da Sb2).""", "Importatore ASME; editor per nuove curve Sb1.")
      "Sb2", ("""Come `Sb1`, per il bolting della Table 4.""", "Importatore ASME; editor per nuove curve Sb2.")
      "Source_Notes", ("""Testo ufficiale delle note di ogni tabella sorgente ASME, identificate da `(SourceTable, Code)`. `Kind` distingue le note generali della tabella (`General`) da quelle specifiche di riga (`Specific`).""", "Importatore ASME. Non si modifica.")
      "Property_Note_Links", ("""Collega ogni curva alle note ufficiali che le si applicano (relazione n:m fra `Material_Properties` e `Source_Notes`). Le note `General` di una tabella si applicano senza collegamento esplicito (vista `VW_Property_Notes_All`).""", "Importatore ASME.")
      "Property_Notes", ("""Copia denormalizzata del testo delle note di ogni curva. `note_type` ha forma `Tabella:Codice` (es. `1A:G10`); `1A:RawCodes` è l'elenco grezzo dei codici stampati nella riga.""", "Importatore ASME. Per annotare una curva si usano i metadati dell'editor, non questa tabella.")
      "Physical_Properties", ("""Testata di una **curva fisica** di un materiale: `E`, `Alpha`, `AlphaInstantaneous`, `Cp`, `TC`, `TD`, `Density`, `Nu`. Non ha intervalli di spessore.""", "Editor; importazione XML. Nel riferimento: curve E, Alpha, AlphaInstantaneous, k (TC) e a (TD) importate dagli XML senza revisione manuale.")
      "Physical_Property_Values", ("""Punti di una curva fisica: temperatura (°C) e valore (unità della famiglia, vedi §5).""", "Editor; importazione XML. Popolata nel riferimento.")
      "Extension", ("""Costanti per materiale: densità e coefficiente di Poisson, validi a ogni temperatura. Una sola riga per materiale; `NULL` = assente, mai un valore ipotizzato.""", "Editor (costanti); importazione XML (costanti). Nel riferimento: densità e Poisson dei materiali associati agli XML senza revisione manuale.")
      "External_Pressure_Charts", ("""Diagramma di pressione esterna (A/B) identificato dal nome. È **condiviso**: più materiali possono usarlo tramite `Material_EP_Charts`.""", "Importazione XML (nome `Nome [XML hash]`). Vuota nel riferimento: gli XML EP distribuiti sono estrazioni grezze, non importabili.")
      "Material_EP_Charts", ("""Associa a ogni materiale al massimo un diagramma di pressione esterna.""", "Importazione XML. Vuota nel riferimento (stesso motivo).")
      "EP_Chart_Curves", ("""Una curva del diagramma per temperatura (°C), con modulo elastico opzionale.""", "Importazione XML. Vuota nel riferimento (stesso motivo).")
      "EP_Chart_Points", ("""Punti (A adimensionale, B in MPa) di una curva del diagramma.""", "Importazione XML. Vuota nel riferimento (stesso motivo).")
      "ASME_Data", ("""Sintesi per materiale dell'applicabilità normativa: P-No., Group No., diagramma EP e temperature massime per Section (I, III, VIII-1, VIII-2, XII). Contiene solo i valori scalari non ambigui; i limiti **per curva** stanno in `Property_Source`.""", "Importatore ASME.")
      "ASME_Data_Source", ("""Ogni campo normativo originale (anche se non riducibile a un solo valore) con file e riga di provenienza, valore grezzo e, se numerico, valore numerico.""", "Importatore ASME.")
      "Source_Documents", ("""Documento sorgente completo (testo) di ciascuna tabella ASME importata (1A, 1B, 2A, 2B, 3, 4, 5A, 5B, Sy, Su), con SHA-256.""", "Importatore ASME.")
      "Import_Files", ("""Elenco dei file CSV importati con hash e numero di righe: base per verificare l'importazione.""", "Importatore ASME.")
      "Import_Omitted_Rows", ("""Righe del CSV escluse dall'importazione, con motivo e contenuto grezzo (nel riferimento: righe vuote usate come marcatori di cancellazione).""", "Importatore ASME.")
      "Database_Metadata", ("""Metadati dell'importazione: sorgente, hash, edizione, unità, ambito, politica di identità e versione dello schema (`SchemaVersion`).""", "Importatore ASME.")
      "Stress_Strain_Curves", ("""Curve sforzo-deformazione per materiale e temperatura (isocrone o altro). Prevista dallo schema ma **non popolata**; nessun codice di accesso al database la usa.""", "Nessuno.")
      "Stress_Strain_Points", ("""Punti (deformazione, tensione) di una curva sforzo-deformazione.""", "Nessuno.")
      "Creep_Rupture_Data", ("""Dati di creep/rottura per materiale, temperatura e durata.""", "Nessuno.")
      "Larson_Miller_Parameters", ("""Costante di Larson–Miller per materiale.""", "Nessuno.")
      "Larson_Miller_Curve_Points", ("""Punti (tensione, parametro LMP) della curva di Larson–Miller.""", "Nessuno.")
      "Stress_Strain_Model_Parameters", ("""Parametri del modello sforzo-deformazione (limiti di temperatura, formula m2, ε_p).""", "Nessuno.")
      "Cyclic_Curve_Header", ("""Testata della curva ciclica (tipo di componente, riferimento di spessore).""", "Nessuno.")
      "Cyclic_Curve_Values", ("""Valori n_css e K_css della curva ciclica per temperatura.""", "Nessuno.")
      "Omega_Method_Parameters", ("""Coefficienti A0–A4 e B0–B4 del metodo Omega per materiale.""", "Nessuno.")
      "Property_Curve_Metadata", ("""Dati dell'editor per ogni curva: asse dimensionale, note utente (testo libero e copia modificata delle note ASME), livello S1 Normal/High, override locale dei limiti per Section. **Non ha chiave esterna**: si aggancia alla curva per `(Code, CurveId)`.""", "Editor. Creata da `Asme2025Crud.ensureSchema`. I trigger di pulizia la mantengono senza orfani.")
      "Property_Edit_History", ("""Giornale delle modifiche passate dall'editor (creazione, aggiornamento e cancellazione di curve, identità, curve vuote rimosse) con istantanea prima/dopo. Solo in aggiunta.""", "Editor; `removeEmptyCurves`. Creata da `Asme2025Crud.ensureSchema`.")
      "Xml_Import_Documents", ("""Documento XML integrale di ogni sorgente importata, con SHA-256 dei byte originali. Conservato una sola volta, anche se migliaia di associazioni lo condividono.""", "Importazione XML (dati distribuiti e app).")
      "Xml_Property_Associations", ("""Per ogni materiale e famiglia, quale record XML è stato importato e dove (`TargetId`). Il record è salvato in forma compatta: senza documento XML completo, senza testo grezzo e senza punti (che stanno nelle tabelle fisiche).""", "Importazione XML (dati distribuiti e app).")
      "Xml_Import_History", ("""Giornale delle importazioni XML con istantanea prima/dopo. Solo in aggiunta.""", "Importazione XML da una copia di lavoro. Vuota nel riferimento.")
      "Sql_Change_Log", ("""Registro **opzionale** delle modifiche fatte con SQL diretto (immagine JSON prima/dopo di ogni riga, su 15 tabelle). Non esiste finché non si attiva.""", "Trigger `Audit_*` installati da `SqlAudit.enable`. Vedere §13.9.") ]

/// <summary>Meaning, unit or allowed values of columns whose name alone does not say it.</summary>
let notes : Map<string, Map<string, string>> =
    let m l = Map.ofList l
    Map.ofList [
      "Main", m [ "id", "Chiave tecnica"; "Revision", "Revisione della fonte (spesso NULL)"; "NominalComposition", "Composizione nominale, es. `Carbon steel`"; "Specification", "Specifica, es. `SA-516`"; "Grade", "Grado"; "ClassConditionTemper", "Classe/condizione/tempra"; "AlloyDesignation", "UNS"; "SMYS", "Snervamento minimo scalare, MPa; NULL se la fonte ha più valori"; "SMUS", "Rottura minima scalare, MPa; NULL se ambigua"; "ElongationAtRuptureLong", "Allungamento longitudinale, %"; "ElongationAtRuptureTransv", "Allungamento trasversale, %" ]
      "Material_Identity", m [ "NaturalKey", """Array JSON `["SA-516","70","","K02700"]`; confrontare dopo l'analisi del JSON (l'escaping varia)""" ]
      "Material_Source", m [ "SourceTable", "Tabella ASME (1A, 1B, 2A, …)"; "File", "File CSV"; "CsvLine", "Riga nel CSV"; "RawFieldsJson", "Riga CSV originale come JSON"; "Fingerprint", "Hash della riga" ]
      "Sizes", m [ "SizeMin", "mm; NULL = nessun limite inferiore"; "SizeMinIncluded", "1 = estremo incluso (ignorato se SizeMin è NULL)"; "SizeMax", "mm; NULL = nessun limite superiore"; "SizeMaxIncluded", "1 = estremo incluso (ignorato se SizeMax è NULL)" ]
      "Material_Properties", m [ "id_size", "Riga di `Sizes` (obbligatoria in pratica: i lettori la uniscono con `Sizes`, una curva con `id_size` NULL non viene letta). Per «qualsiasi dimensione» si usa la riga con `SizeMin` e `SizeMax` entrambi NULL"; "PropertyCode", "Famiglia: Sy, Su, S1, S2, Sb1, Sb2"; "ProductForm", "Forma di prodotto (PLATE, FORGINGS, …); solo S1/S2" ]
      "Property_Values", m [ "Temperature", "°C"; "StressValue", "MPa" ]
      "Property_Source", m [ "SourceTable", "1A, 1B, 3, 4, 5A, 5B, Sy, Su"; "File", "File CSV"; "CsvLine", "Riga nel CSV"; "Page", "Pagina stampata"; "SourceLine", "Riga sulla pagina"; "SourceRowNumber", "Numero di riga della tabella"; "SizeText", "Testo dello spessore come stampato"; "SizeAxis", "`thickness-or-unspecified`, `diameter`, `OD`, `DN`"; "MinYieldMPa", "Snervamento minimo della riga, MPa"; "MinTensileMPa", "Rottura minima della riga, MPa"; "RawFieldsJson", "Riga stampata come JSON (colonne `<=T °C, MPa`, identità, note)"; "Fingerprint", "Hash della riga; `User` per righe create dall'utente" ]
      "Sb1", m [ "id_property", "Curva Sb1 (stessa chiave di `Material_Properties`)"; "id_size", "Deve coincidere con `Material_Properties.id_size`" ]
      "Sb2", m [ "id_property", "Curva Sb2 (stessa chiave di `Material_Properties`)"; "id_size", "Deve coincidere con `Material_Properties.id_size`" ]
      "Source_Notes", m [ "SourceTable", "Tabella ASME"; "Code", "Codice nota, es. `(a)` o `G10`"; "Kind", "`General` o `Specific`"; "NoteText", "Testo integrale" ]
      "Property_Note_Links", m [ "SourceTable", "Tabella della nota"; "Code", "Codice della nota" ]
      "Property_Notes", m [ "note", "Testo della nota"; "note_type", "`Tabella:Codice`" ]
      "Physical_Properties", m [ "PropertyCode", "`E`, `Alpha`, `AlphaInstantaneous`, `Cp`, `TC`, `TD`, `Density`, `Nu`" ]
      "Physical_Property_Values", m [ "Temperature", "°C"; "PropertyValue", "MPa (E), 1/°C (Alpha), J/(kg·K) (Cp), W/(m·K) (TC), m²/s (TD), kg/m³ (Density), adimensionale (Nu)" ]
      "Extension", m [ "ConstantPoissonRatio", "Adimensionale, −1 < Nu < 0,5"; "ConstantDensity", "kg/m³, > 0" ]
      "External_Pressure_Charts", m [ "Chart_Name", "Nome (univoco)" ]
      "EP_Chart_Curves", m [ "Temperature", "°C"; "Elastic_Modulus", "MPa; opzionale (non valorizzato dall'importazione XML)" ]
      "EP_Chart_Points", m [ "Factor_A", "Adimensionale"; "Factor_B", "MPa" ]
      "ASME_Data", m [ "P_Number", "P-No. (testo, es. `1`, `5A`)"; "Group_Number", "Group No."; "External_Pressure_Chart_No", "Riferimento al diagramma EP originale (es. `CS-2`)"; "Time_Dependent_Start_Temp", "°C"; "Max_Temp_Sect_I", "°C"; "Max_Temp_Sect_III", "°C"; "Max_Temp_Sect_VIII_1", "°C"; "Max_Temp_Sect_VIII_2", "°C"; "Max_Temp_Sect_XII", "°C" ]
      "ASME_Data_Source", m [ "SourceTable", "Tabella ASME"; "File", "File CSV"; "CsvLine", "Riga nel CSV"; "FieldName", "Campo normativo (P_Number, Group_Number, External_Pressure_Chart_No, Max_Temp_Sect_*)"; "RawValue", "Valore come stampato (anche `NP` o con qualifica)"; "NumericValue", "Valore numerico, se riducibile (°C)" ]
      "Source_Documents", m [ "SourceTable", "Tabella ASME"; "SHA256", "Hash del documento"; "Contents", "Testo del documento" ]
      "Import_Files", m [ "Path", "Percorso relativo del CSV"; "SHA256", "Hash del file"; "RowCount", "Righe lette" ]
      "Import_Omitted_Rows", m [ "SourceTable", "Tabella ASME"; "File", "File CSV"; "CsvLine", "Riga nel CSV"; "Reason", "Motivo dell'esclusione"; "RawFieldsJson", "Riga grezza" ]
      "Database_Metadata", m [ "Key", "SourceKind, SourcePath, SourceSHA256, ArchiveSHA256, Edition, Units, Scope, IdentityPolicy, SchemaVersion"; "Value", "Valore" ]
      "Stress_Strain_Curves", m [ "Temperature", "°C"; "Time_Hours", "h; obbligatorio per `Isochronous`"; "Curve_Type", "Tipo di curva" ]
      "Stress_Strain_Points", m [ "Strain", "Adimensionale"; "Stress", "MPa" ]
      "Creep_Rupture_Data", m [ "Temperature", "°C"; "Duration_Hours", "h"; "StressValue", "MPa" ]
      "Larson_Miller_Curve_Points", m [ "StressValue", "MPa" ]
      "Stress_Strain_Model_Parameters", m [ "Temperature_Limit_C", "°C"; "Temperature_Limit_F", "°F" ]
      "Cyclic_Curve_Values", m [ "Temperature", "°C" ]
      "Property_Curve_Metadata", m [ "Code", "Famiglia della curva (Sy, S1, E, …)"; "CurveId", "Id della curva nella tabella della famiglia (`Material_Properties.id` o `Physical_Properties.id`)"; "SizeAxis", "Asse dimensionale"; "UserNotes", "Note libere dell'utente"; "UserNoteItemsJson", "Copia modificata delle note ASME: array di `{SourceTable, Code, Text}`"; "S1StressLevel", "`Normal` o `High` (G5)"; "CodeLimitsJson", "Override dei limiti per Section; NULL = limiti della riga sorgente" ]
      "Property_Edit_History", m [ "MaterialId", "Materiale modificato"; "Code", "Famiglia o `MAIN`"; "CurveId", "Curva modificata"; "EditedUtc", "UTC, ISO 8601"; "Operation", "Create, Update, Delete, Identity, DeleteMaterial, DeleteEmpty"; "BeforeJson", "Immagine prima"; "AfterJson", "Immagine dopo" ]
      "Xml_Import_Documents", m [ "SHA256", "Hash dei byte del file XML"; "File", "Percorso del file"; "Contents", "XML serializzato dal parser" ]
      "Xml_Property_Associations", m [ "DocumentSHA256", "Documento XML di origine"; "PropertyCode", "Famiglia (E, Alpha, k, …, EP)"; "RecordKey", "Chiave stabile del record XML"; "TargetId", "Id della curva o del diagramma scritto"; "RecordJson", "Record compatto (chiave, identità, unità, note, diagnostica); i punti sono nelle tabelle fisiche"; "ImportedUtc", "UTC" ]
      "Xml_Import_History", m [ "ImportedUtc", "UTC"; "MaterialId", "Materiale"; "PropertyCode", "Famiglia"; "RecordKey", "Record XML"; "BeforeJson", "Stato prima"; "AfterJson", "Stato dopo" ]
      "Sql_Change_Log", m [ "ChangedUtc", "UTC, ISO 8601"; "TableName", "Tabella modificata"; "Operation", "INSERT, UPDATE, DELETE"; "RowId", "Chiave della riga"; "BeforeJson", "Immagine prima (vuota per INSERT)"; "AfterJson", "Immagine dopo (vuota per DELETE)" ] ]

let triggerText : Map<string, string> =
    Map.ofList [
      "Validate_Sb1_Insert", "BEFORE INSERT: accetta solo curve `Sb1` con fonte Table 3 e stesso materiale e intervallo della curva."
      "Validate_Sb2_Insert", "BEFORE INSERT: accetta solo curve `Sb2` con fonte Table 4 e stesso materiale e intervallo della curva."
      "Guard_MaterialProperties_Code_Insert", "rifiuta famiglie meccaniche sconosciute."
      "Guard_MaterialProperties_Size_Insert", "rifiuta una curva meccanica senza riga di `Sizes` (`id_size` NULL): per «qualsiasi dimensione» serve la riga con `SizeMin` e `SizeMax` NULL."
      "Guard_PhysicalProperties_Code_Insert", "rifiuta famiglie fisiche sconosciute."
      "Guard_PropertyValues_Insert", "rifiuta tensioni negative e temperature fuori −273,15…1200 °C."
      "Guard_PropertyValues_Update", "come sopra, in modifica."
      "Guard_PhysicalValues_Insert", "rifiuta temperature fuori −273,15…1200 °C."
      "Guard_Extension_Insert", "densità > 0 e −1 < Nu < 0,5."
      "Guard_Extension_Update", "come sopra, in modifica."
      "Guard_Sizes_Immutable", "rifiuta ogni `UPDATE` (copy-on-write)."
      "Sync_Sb_Size_From_Curve", "quando cambia `Material_Properties.id_size` di una curva Sb1/Sb2, allinea `Sb1`/`Sb2`."
      "Guard_Sb1_Update", "rifiuta un `id_size` diverso da quello della curva."
      "Guard_Sb2_Update", "rifiuta un `id_size` diverso da quello della curva."
      "Clean_Sizes_After_Curve_Delete", "elimina l'intervallo se nessuna curva né `Sb1`/`Sb2` lo usa più."
      "Clean_Sizes_After_Curve_Repoint", "come sopra, quando la curva passa a un altro intervallo."
      "Clean_EPChart_After_Link_Delete", "elimina il diagramma se nessun materiale lo usa più."
      "Clean_EPChart_After_Link_Repoint", "come sopra, quando il materiale passa a un altro diagramma."
      "Clean_Metadata_After_Mechanical_Delete", "elimina i metadati `(Code, CurveId)` della curva cancellata."
      "Clean_Metadata_After_Physical_Delete", "elimina i metadati della curva fisica cancellata." ]

let indexText = Map.ofList [ "UX_PropertyValues_Curve_Temp", "una sola temperatura per curva"; "UX_PhysicalValues_Curve_Temp", "una sola temperatura per curva" ]

let viewText : Map<string, string> =
    Map.ofList [
      "S1", "Curve S1 (Division 1) pronte da leggere: materiale, forma di prodotto, fonte, intervallo e punti. Unisce `Main`, `Material_Properties`, `Property_Source`, `Sizes`, `Property_Values`; mostra solo le curve con riga sorgente."
      "S2", "Come `S1`, per le curve S2 (Division 2)."
      "VW_Main_Mechanical_Counts", "Per materiale, il numero di curve Sy/Su/S1/S2/Sb1/Sb2."
      "VW_Main_Physical_Availability", "Per materiale, presenza booleana di E, Alpha, Cp, k, a, densità, Nu."
      "VW_Material_Strength_Availability", "Per materiale: `HasSy`, `HasSu`, `HasS1`, `HasS2`, `HasSb1`, `HasSb2` (esistenza di punti)."
      "VW_Missing_Strength", "Materiali con tensione ammissibile (S1/S2/Sb) ma senza Sy o Su: controllo di completezza."
      "VW_Property_Notes_All", "Note applicabili a una curva: quelle collegate più le note `General` della sua tabella sorgente."
      "VW_ThermalCapacity_Calculated", "Calore specifico calcolato = TC / (Density · TD) dove le tre curve fisiche esistono alle stesse temperature (J/(kg·K)). Vuota se la densità è solo una costante in `Extension`."
      "VW_Curve_Points", "Vista piatta di tutte le curve meccaniche: identità, famiglia, forma, intervallo, temperatura e valore. Include anche le curve senza riga sorgente. Solo nelle copie rafforzate."
      "VW_Integrity_Report", "Una riga per incoerenza nota (curve vuote o senza `Sizes`, temperature duplicate, `Sizes` o diagrammi inutilizzati, `Sb1`/`Sb2` non allineate, chiavi di identità incoerenti). Vuota = tutto in ordine. Solo nelle copie rafforzate." ]

// ---------------------------------------------------------------------------------------------------------------
// 4. Markdown generation
// ---------------------------------------------------------------------------------------------------------------
let isWorkOnly t = List.contains t workOnlyTables
let connFor t = if isWorkOnly t then work else reference
let tableSql t = (if isWorkOnly t then workObjs else refObjs) |> List.find (fun o -> o.Kind = "table" && o.Name = t) |> fun o -> o.Sql
let rowCount t = query reference $"SELECT COUNT(*) FROM \"{t}\"" |> List.head |> Array.head |> Convert.ToInt64
let collapse (s: string) = Regex.Replace(s.Trim(), @"\s+", " ")

let tableMarkdown (t: string) =
    let db = connFor t
    let columns = query db $"PRAGMA table_info(\"{t}\")" |> List.map (fun r -> str r[1], str r[2], Convert.ToInt32 r[3] = 1, (if Convert.IsDBNull r[4] then None else Some(str r[4])), Convert.ToInt32 r[5])
    let sql = tableSql t
    let fkRows = query db $"PRAGMA foreign_key_list(\"{t}\")" |> List.map (fun r -> Convert.ToInt32 r[0], str r[2], str r[3], str r[4], str r[6])
    let fkGroups = fkRows |> List.groupBy (fun (id, _, _, _, _) -> id) |> List.map (fun (_, rows) -> rows)
    let singleFk = fkGroups |> List.choose (function [ (_, tgt, from, to', action) ] -> Some(from, (tgt, to', action)) | _ -> None) |> Map.ofList
    let pkColumns = columns |> List.filter (fun (_, _, _, _, pk) -> pk > 0) |> List.map (fun (n, _, _, _, _) -> n)
    let autoinc = sql.ToUpperInvariant().Contains "AUTOINCREMENT"
    let purposeText, writer = purpose[t]
    let rows = if isWorkOnly t then (if t = "Sql_Change_Log" then "assente (solo se attivata)" else "assente (creata nelle copie di lavoro)") else thousands (rowCount t)
    let b = ResizeArray<string>()
    b.Add $"#### `{t}`"; b.Add ""; b.Add purposeText; b.Add ""
    b.Add $"- **Righe nel riferimento:** {rows}"; b.Add $"- **Chi la scrive:** {writer}"; b.Add ""
    b.Add "| Colonna | Tipo | Vincoli | Note |"; b.Add "|---|---|---|---|"
    for (name, typ, notNull, dflt, pk) in columns do
        let cons = ResizeArray<string>()
        if pk > 0 && pkColumns.Length = 1 then cons.Add("PK" + (if autoinc then " AUTOINCREMENT" else ""))
        elif pk > 0 then cons.Add $"PK {pk}/{pkColumns.Length}"
        if notNull && pk = 0 then cons.Add "NOT NULL"
        match Map.tryFind name singleFk with
        | Some(tgt, to', action) -> cons.Add $"""FK → `{tgt}.{to'}` ({if action = "CASCADE" then "CASCADE" elif action = "NO ACTION" || action = "" then "senza azione" else action})"""
        | None -> ()
        match dflt with Some d -> cons.Add $"default {d}" | None -> ()
        let own = notes |> Map.tryFind t |> Option.bind (Map.tryFind name)
        let note =
            match own with
            | Some n -> n
            | None -> (match name with "id" | "Id" -> "Chiave tecnica" | "id_main" -> "Materiale" | "id_property" -> "Curva" | _ -> "")
        let consText = if cons.Count = 0 then "—" else String.Join("; ", cons)
        b.Add $"""| `{name}` | {(if typ = "" then "—" else typ)} | {consText} | {(if note = "" then "—" else note)} |"""
    b.Add ""
    // Constraints and structures
    let source = if isWorkOnly t then workObjs else refObjs
    let items = ResizeArray<string>()
    if pkColumns.Length > 1 then items.Add $"""chiave primaria composta `({String.Join(", ", pkColumns)})`"""
    for g in fkGroups do
        if g.Length > 1 then
            let (_, tgt, _, _, _) = g.Head
            let from = String.Join(", ", g |> List.map (fun (_, _, f, _, _) -> f))
            let to' = String.Join(", ", g |> List.map (fun (_, _, _, x, _) -> x))
            items.Add $"chiave esterna composta `({from})` → `{tgt}({to'})` (senza azione)"
    for m in Regex.Matches(sql, @"CHECK\s*\((.*?)\)\s*(?:,|\)\s*$)", RegexOptions.Singleline ||| RegexOptions.IgnoreCase) do
        items.Add $"CHECK `{collapse m.Groups[1].Value}`"
    let uniqueColumn = Regex.Match(sql, @"(\w+)\s+TEXT\s+NOT\s+NULL\s+UNIQUE", RegexOptions.IgnoreCase)
    if uniqueColumn.Success then items.Add $"`{uniqueColumn.Groups[1].Value}` univoco"
    for o in source |> List.filter (fun o -> o.Kind = "index" && o.Table = t && o.Sql <> "" && not (isAudit o)) do
        if o.Name = "UX_Main_FourFieldIdentity" then
            items.Add "indice `UX_Main_FourFieldIdentity` (univoco) sui quattro campi di identità: `Specification`, `Grade`, `ClassConditionTemper`, `AlloyDesignation`, con NULL trattato come stringa vuota e confronto senza distinzione fra maiuscole e minuscole"
        else
            let cols = collapse (Regex.Match(o.Sql, @"\((.*)\)", RegexOptions.Singleline).Groups[1].Value)
            items.Add $"""indice `{o.Name}`{(if o.Sql.ToUpperInvariant().Contains " UNIQUE" then " (univoco)" else "")} su `{cols}`"""
    for o in source |> List.filter (fun o -> o.Kind = "trigger" && o.Table = t && not (isAudit o)) do
        items.Add $"""trigger `{o.Name}`: {Map.tryFind o.Name triggerText |> Option.defaultValue ""}"""
    if t = "Sql_Change_Log" then
        items.Add "trigger `Audit_<Tabella>_Insert/Update/Delete` su 15 tabelle (identità, curve, punti, costanti, `ASME_Data`, `Sizes`, `Sb1`/`Sb2`, diagrammi EP), installati e rimossi da `SqlAudit.enable`/`disable`"
    if items.Count > 0 then
        b.Add (if isWorkOnly t then "**Vincoli e strutture:**" else "**Vincoli e strutture nel riferimento:**"); b.Add ""
        for i in items do b.Add $"- {i}"
        b.Add ""
    // Additions by the hardening (reference tables only)
    if not (isWorkOnly t) then
        let extra = hardeningAdded |> List.filter (fun o -> o.Table = t) |> List.sortBy (fun o -> o.Kind, o.Name)
        let replaced = dropped |> List.filter (fun o -> o.Table = t)
        if not extra.IsEmpty || not replaced.IsEmpty then
            b.Add "**Aggiunte dal rafforzamento delle copie di lavoro (vedere §13.3):**"; b.Add ""
            for o in extra do
                if o.Kind = "index" then
                    let cols = collapse (Regex.Match(o.Sql, @"\((.*)\)", RegexOptions.Singleline).Groups[1].Value)
                    let unique = if o.Sql.ToUpperInvariant().Contains " UNIQUE" then " (univoco)" else ""
                    let why = match Map.tryFind o.Name indexText with Some w -> $": {w}" | None -> ""
                    b.Add $"- indice `{o.Name}`{unique} su `{cols}`{why}"
                else b.Add $"""- trigger `{o.Name}`: {Map.tryFind o.Name triggerText |> Option.defaultValue ""}"""
            for o in replaced do b.Add $"- sostituisce l'indice `{o.Name}` (non univoco)"
            b.Add ""
    String.Join("\n", b)

let out = ResizeArray<string>()
let add (s: string) = out.Add s
let allTables = groups |> List.collect snd
// Every table found in the databases must be described, and every described table must exist.
let missing = (refTables @ workOnlyTables) |> List.filter (fun t -> not (List.contains t allTables))
if not missing.IsEmpty then failwithf "Tables without a description in the generator: %A" missing
let unknown = allTables |> List.filter (fun t -> not (List.contains t refTables) && not (List.contains t workOnlyTables))
if not unknown.IsEmpty then failwithf "Described tables missing from the databases: %A" unknown

let viewNames = refViews @ workOnlyViews
let viewsMissing = viewNames |> List.filter (fun v -> not (viewText.ContainsKey v))
if not viewsMissing.IsEmpty then failwithf "Views without a description in the generator: %A" viewsMissing
let triggersMissing = (refObjs @ hardeningAdded) |> List.filter (fun o -> o.Kind = "trigger" && not (triggerText.ContainsKey o.Name)) |> List.map (fun o -> o.Name)
if not triggersMissing.IsEmpty then failwithf "Triggers without a description in the generator: %A" triggersMissing

add "## 2. Catalogo delle tabelle\n"
add """Questo capitolo descrive **tutte** le tabelle e le viste del database: scopo, colonne, tipi,
vincoli (chiavi primarie ed esterne, `UNIQUE`, `CHECK`, indici, trigger) e chi le scrive. I
capitoli seguenti spiegano come usarle insieme; qui si consulta una tabella alla volta.

> Questo capitolo è **generato** dallo schema reale con `tools/Generate-DatabaseCatalog.ps1`:
> non modificarlo a mano (le modifiche andrebbero perse alla prossima rigenerazione).

### 2.1 Come leggere il catalogo

- **Origine.** Le tabelle del *riferimento* esistono in `materials_2025.db`. Le tabelle
  delle *copie di lavoro* nascono solo quando si apre una copia con l'app (editor)
  e sono segnate come tali. Il rafforzamento (§13.3) aggiunge indici, trigger e viste alle sole copie di lavoro.
- **Colonne.** `PK` = chiave primaria (`n/m` per le chiavi composte); `FK → Tabella.colonna` = chiave
  esterna; `CASCADE` = la cancellazione della riga padre cancella le figlie; `senza azione` = la
  chiave esterna blocca la cancellazione della riga referenziata.
- **Tipi.** Dichiarati come nello schema SQLite (`INTEGER`, `REAL`, `TEXT`, `BOOLEAN`); SQLite
  non li impone rigidamente, quindi i vincoli sotto sono quelli che contano.
- **Unità fisse.** Temperature in °C, tensioni in MPa, dimensioni in mm, altre unità indicate nella
  colonna *Note* (§5).
- **Foreign keys.** Per il funzionamento a cascata ogni connessione deve eseguire `PRAGMA foreign_keys = ON`.
- **Conteggi** riferiti al riferimento 2025.

### 2.2 Panoramica

| Tabella | Gruppo | Origine | Righe nel riferimento |
|---|---|---|---|"""
for (g, ts) in groups do
    for t in ts do
        let origin, count =
            if t = "Sql_Change_Log" then "opzionale", "—"
            elif isWorkOnly t then "copia di lavoro", "—"
            else "riferimento", thousands (rowCount t)
        add $"| `{t}` | {g} | {origin} | {count} |"
add ""
add $"In totale {refTables.Length} tabelle nel riferimento, {workOnlyTables.Length - 1} tabelle create nelle copie di lavoro (più `Sql_Change_Log` se attivata) e {viewNames.Length} viste.\n"
groups |> List.iteri (fun i (g, ts) ->
    add $"### 2.{i + 3} {g}\n"
    for t in ts do add (tableMarkdown t); add "")
let nGroups = groups.Length
add $"### 2.{nGroups + 3} Viste\n"
add "Le viste non memorizzano dati; ricalcolano ciò che mostrano a ogni lettura.\n"
add "| Vista | Origine | Scopo | Colonne |"
add "|---|---|---|---|"
for v in viewNames do
    let db = if List.contains v refViews then reference else work
    let cols = query db $"PRAGMA table_info(\"{v}\")" |> List.map (fun r -> $"`{str r[1]}`") |> String.concat ", "
    let origin = if List.contains v refViews then "riferimento" else "copia di lavoro rafforzata"
    add $"| `{v}` | {origin} | {viewText[v]} | {cols} |"
add ""
add $"### 2.{nGroups + 4} Trigger e indici: riepilogo\n"
add "| Oggetto | Tipo | Tabella | Origine | Funzione |"
add "|---|---|---|---|---|"
for o in refObjs |> List.filter (fun o -> o.Kind = "trigger") |> List.sortBy (fun o -> o.Table, o.Name) do
    add $"| `{o.Name}` | trigger | `{o.Table}` | riferimento | {triggerText[o.Name]} |"
for o in hardeningAdded |> List.filter (fun o -> o.Kind = "trigger") |> List.sortBy (fun o -> o.Table, o.Name) do
    add $"| `{o.Name}` | trigger | `{o.Table}` | rafforzamento | {triggerText[o.Name]} |"
let refIndexes = refObjs |> List.filter (fun o -> o.Kind = "index") |> List.length
let addedIndexes = hardeningAdded |> List.filter (fun o -> o.Kind = "index") |> List.length
let uniqueReplacements = hardeningAdded |> List.filter (fun o -> o.Kind = "index" && o.Sql.ToUpperInvariant().Contains " UNIQUE") |> List.length
add $"\nGli indici del riferimento sono {refIndexes} (elencati con la propria tabella); il rafforzamento ne aggiunge {addedIndexes} (di cui {uniqueReplacements} univoci che sostituiscono `IDX_PropertyValues_Temp` e `IDX_PhysicalValues_Temp`).\n"

// ---------------------------------------------------------------------------------------------------------------
// 5. Splice into the documentation between the markers
// ---------------------------------------------------------------------------------------------------------------
let lf = string (char 10)
let crlf = string (char 13) + lf
let generated = Regex.Replace(String.Join(lf, out), @"\n{3,}", lf + lf).TrimEnd(char 10) + lf
let rawDoc = File.ReadAllText(docPath)
// Compare and splice with LF, then write back with the line ending the file already uses (Git on Windows may check out CRLF).
let eol = if rawDoc.Contains crlf then crlf else lf
let doc = rawDoc.Replace(crlf, lf)
let start = doc.IndexOf beginMarker
let stop = doc.IndexOf endMarker
if start < 0 || stop < 0 || stop < start then failwith "Catalog markers not found in the documentation file."
let afterBegin = doc.IndexOf("-->", start) + 3
let updated = doc.Substring(0, afterBegin) + lf + lf + generated + lf + doc.Substring stop
let changed = updated <> doc
let checkOnly = fsi.CommandLineArgs |> Array.contains "--check"
if changed && not checkOnly then File.WriteAllText(docPath, updated.Replace(lf, eol))
printfn "Catalog %s: %d tables (%d reference, %d working-copy), %d views, %d lines." (if not changed then "unchanged" elif checkOnly then "OUT OF DATE" else "UPDATED") (refTables.Length + workOnlyTables.Length) refTables.Length workOnlyTables.Length viewNames.Length (generated.Split('\n').Length)

work.Dispose()
reference.Dispose()
SqliteConnection.ClearAllPools()
try Directory.Delete(temp, true) with _ -> ()
if changed && checkOnly then exit 1
