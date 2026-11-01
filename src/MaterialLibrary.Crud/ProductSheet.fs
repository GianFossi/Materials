// Author: Claude
// Last revision: 2026-11-01
// Purpose: Build a complete product data sheet (Markdown) of one native 2025 material: every property, every size range, room/design/operating temperatures, all notes.
namespace MaterialLibrary.Crud

open System
open System.Globalization
open System.Text
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Temperatures of the sheet columns, all in degC. At most three design and three operating temperatures are accepted.</summary>
[<CLIMutable>]
type SheetConditions = {
    /// Room temperature column, degC (typically 20).
    RoomTemperature: float
    /// Design temperatures, degC, 0 to 3 values.
    DesignTemperatures: float list
    /// Operating temperatures, degC, 0 to 3 values.
    OperatingTemperatures: float list
}

/// <summary>
/// Read-only report of a material: for each size range, a table with Room, Design (max 3) and Operating (max 3) temperature columns
/// for Sy, Su, S1, S2, Sb1, Sb2, E, Alpha, AlphaInstantaneous, c, k, a, density and Nu, followed by external-pressure data,
/// the code limits of each curve and every note (ASME, edited copies, user, XML provenance).
/// Values are interpolated linearly inside a curve and never extrapolated; a missing value is printed as "n.d.".
/// </summary>
module ProductSheet =
    let private it = CultureInfo("it-IT")

    /// <summary>Format a number with up to <paramref name="decimals"/> decimals, Italian separators.</summary>
    let private fmt (decimals: int) (value: float) =
        let pattern = if decimals = 0 then "0" else "0." + String('#', decimals)
        value.ToString(pattern, it)

    /// <summary>Em dash for an empty text.</summary>
    let private orDash (s: string) = if s = "" then "—" else s

    /// <summary>Text of a nullable database cell.</summary>
    let private text (v: obj) = if isNull v || Convert.IsDBNull v then "" else string v

    /// <summary>True when the curve's size interval contains the size (explicit open/closed boundaries, as in the lookup).</summary>
    let private applies (c: Asme2025Curve) (s: float) =
        (not c.SizeMinimum.HasValue || (if c.MinimumIncluded then s >= c.SizeMinimum.Value else s > c.SizeMinimum.Value))
        && (not c.SizeMaximum.HasValue || (if c.MaximumIncluded then s <= c.SizeMaximum.Value else s < c.SizeMaximum.Value))

    /// <summary>One size range of the sheet, with a probe size used to choose the applicable curves.</summary>
    type private Section = { Lower: float option; LowerIncluded: bool; Upper: float option; UpperIncluded: bool; Probe: float }

    /// <summary>Which side of a boundary value its own size belongs to.</summary>
    type private Side = LeftSide | RightSide | Neither

    /// <summary>
    /// Partition the size axis at every curve boundary. A boundary value gets a one-point section only when the curves valid exactly at that
    /// size differ from both neighbouring ranges (curves that disagree on which side includes the boundary).
    /// </summary>
    let private sections (curves: Asme2025Curve list) =
        let bounds = curves |> List.collect (fun c -> [ c.SizeMinimum; c.SizeMaximum ] |> List.filter (fun v -> v.HasValue) |> List.map (fun v -> v.Value)) |> List.distinct |> List.sort
        let setAt s = curves |> List.filter (fun c -> applies c s) |> List.map (fun c -> c.Id) |> Set.ofList
        if bounds.IsEmpty then [ { Lower = None; LowerIncluded = false; Upper = None; UpperIncluded = false; Probe = 1. } ]
        else
            let arr = List.toArray bounds
            let n = arr.Length
            // Probe strictly inside each segment so that boundary inclusion never influences it.
            let probe i = if i = 0 then (if arr[0] > 0. then arr[0] / 2. else arr[0] - 1.) elif i = n then arr[n - 1] + 1. else (arr[i - 1] + arr[i]) / 2.
            let side k =
                let atSet, left, right = setAt arr[k], setAt (probe k), setAt (probe (k + 1))
                if atSet = left then LeftSide elif atSet = right then RightSide else Neither
            let sides = Array.init n side
            [ for i in 0 .. n do
                let lower = if i = 0 then None else Some arr[i - 1]
                let upper = if i = n then None else Some arr[i]
                let section = { Lower = lower; LowerIncluded = (i > 0 && sides[i - 1] = RightSide); Upper = upper; UpperIncluded = (i < n && sides[i] = LeftSide); Probe = probe i }
                if not (setAt section.Probe).IsEmpty then yield section
                if i < n && sides[i] = Neither && not (setAt arr[i]).IsEmpty then
                    yield { Lower = Some arr[i]; LowerIncluded = true; Upper = Some arr[i]; UpperIncluded = true; Probe = arr[i] } ]

    /// <summary>Section title, with the symbol t (thickness) or d (diameter/dimension).</summary>
    let private title (symbol: string) (s: Section) =
        let upperOp = if s.UpperIncluded then "≤" else "<"
        let lowerOp = if s.LowerIncluded then "≤" else "<"
        let lowerOpOpen = if s.LowerIncluded then "≥" else ">"
        match s.Lower, s.Upper with
        | None, None -> "qualsiasi dimensione"
        | Some l, Some u when l = u -> $"{symbol} = {fmt 3 l} mm"
        | None, Some u -> $"{symbol} {upperOp} {fmt 3 u} mm"
        | Some l, None -> $"{symbol} {lowerOpOpen} {fmt 3 l} mm"
        | Some l, Some u -> $"{fmt 3 l} {lowerOp} {symbol} {upperOp} {fmt 3 u} mm"

    /// <summary>Interpolated value of one curve at a temperature, or None outside the curve (no extrapolation).</summary>
    let private evaluate (curve: Asme2025Curve) (temperature: float) =
        match Asme2025Data.lookup [ curve ] temperature None None (Some curve.Id) with
        | Ok v -> Some v
        | Error _ -> None

    /// <summary>One printed table row.</summary>
    type private Row = { Property: string; Unit: string; Curve: string; Cells: string list }

    /// <summary>Build the sheet for a material.</summary>
    /// <param name="db">Open connection to a native 2025 database (reference or working copy).</param>
    /// <param name="materialId">Main.id of the material.</param>
    /// <param name="conditions">Room, design (max 3) and operating (max 3) temperatures in degC.</param>
    /// <returns>Markdown text, or an error when the material is missing or the conditions are invalid.</returns>
    let build (db: SqliteConnection) (materialId: int64) (conditions: SheetConditions) : Result<string, string> =
        let finite = Double.IsFinite
        let allTemps = conditions.RoomTemperature :: conditions.DesignTemperatures @ conditions.OperatingTemperatures
        if conditions.DesignTemperatures.Length > 3 || conditions.OperatingTemperatures.Length > 3 then Error "At most three design and three operating temperatures are allowed."
        elif allTemps |> List.exists (fun t -> not (finite t) || t < -273.15 || t > 1200.) then Error "Temperatures must be finite and within -273.15..1200 degC."
        else
            match Asme2025Data.materials db |> List.tryFind (fun m -> m.Id = materialId) with
            | None -> Error $"Material {materialId} not found."
            | Some material ->
                let main = Asme2025Data.query db "SELECT SMYS,SMUS,ElongationAtRuptureLong,ElongationAtRuptureTransv FROM Main WHERE id=$id" [ "$id", box materialId ] |> List.head
                let mechanicalFamilies = [ "Sy"; "Su"; "S1"; "S2"; "Sb1"; "Sb2" ]
                let mechanical = mechanicalFamilies |> List.collect (fun f -> Asme2025Data.curves db materialId f)
                let symbol = if mechanical |> List.exists (fun c -> c.SizeAxis = "diameter" || c.SizeAxis = "OD" || c.SizeAxis = "DN") && not (mechanical |> List.exists (fun c -> c.SizeAxis = "thickness-or-unspecified" && (c.SizeMinimum.HasValue || c.SizeMaximum.HasValue))) then "d" else "t"
                let columns = [ ("Room", conditions.RoomTemperature) ] @ (conditions.DesignTemperatures |> List.map (fun t -> "Design", t)) @ (conditions.OperatingTemperatures |> List.map (fun t -> "Operating", t))
                let notes = ResizeArray<string>()
                let noteRef (note: string) =
                    let i = notes.IndexOf note
                    if i >= 0 then i + 1 else notes.Add note; notes.Count
                let sourceRow id = Asme2025Data.query db "SELECT SourceTable,Page,SourceLine,MinYieldMPa,MinTensileMPa FROM Property_Source WHERE id_property=$id" [ "$id", box id ] |> List.tryHead
                let curveNotes (c: Asme2025Curve) =
                    let mutable refs = []
                    let mechanicalCurve = List.contains c.Code mechanicalFamilies
                    if mechanicalCurve then
                        if c.UserNoteItems.Length > 0 then
                            for n in c.UserNoteItems do refs <- refs @ [ noteRef $"**ASME {n.SourceTable} / {n.Code}** (copia modificata dall'utente): {n.Text}" ]
                        else
                            for n in Asme2025Data.query db "SELECT SourceTable,Code,Kind,NoteText FROM VW_Property_Notes_All WHERE id_property=$id ORDER BY SourceTable,Kind,Code" [ "$id", box c.Id ] do
                                refs <- refs @ [ noteRef $"**ASME {text n[0]} / {text n[1]}** ({text n[2]}): {text n[3]}" ]
                    elif c.SourceNotes <> "" then refs <- refs @ [ noteRef $"**Fonte XML:** {c.SourceNotes.Replace('\n', ' ')}" ]
                    if c.UserNotes <> "" then refs <- refs @ [ noteRef $"**Nota utente:** {c.UserNotes}" ]
                    refs |> List.distinct
                // Consecutive note numbers are printed as ranges (1–7) to keep the table readable.
                let refText (refs: int list) =
                    if refs.IsEmpty then ""
                    else
                        let sorted = refs |> List.sort
                        let groups = sorted |> List.fold (fun acc n -> match acc with (lo, hi) :: rest when n = hi + 1 -> (lo, n) :: rest | _ -> (n, n) :: acc) [] |> List.rev
                        let parts = groups |> List.map (fun (lo, hi) -> if lo = hi then string lo elif hi = lo + 1 then $"{lo}, {hi}" else $"{lo}–{hi}")
                        " [" + String.Join(", ", parts) + "]"
                let usedCurves = ResizeArray<Asme2025Curve>()
                // Allowable-stress rows follow the Section column of their own curve: VIII-1 for S1/Sb1, VIII-2 for S2/Sb2.
                let allowableCell (c: Asme2025Curve) (value: float) (t: float) (decimals: int) =
                    let raw = match c.Code with "S1" | "Sb1" -> c.CodeLimits.MaxTempSectionVIII1 | "S2" | "Sb2" -> c.CodeLimits.MaxTempSectionVIII2 | _ -> ""
                    if raw.Trim().Equals("NP", StringComparison.OrdinalIgnoreCase) then "NP"
                    else
                        let limit = Asme2025Data.temperatureLimitC raw
                        if limit.HasValue && t > limit.Value then fmt decimals value + " ⚠" else fmt decimals value
                let mechanicalRow (c: Asme2025Curve) =
                    usedCurves.Add c
                    let label =
                        [ (if c.ProductForm <> "" then c.ProductForm else ""); (if c.Code = "S1" then c.S1StressLevel else "") ] |> List.filter ((<>) "") |> String.concat ", "
                    let source = sourceRow c.Id
                    let cell (name, t) =
                        match c.Code, source with
                        | "Sy", Some s when name = "Room" && not (isNull s[3]) -> fmt 1 (Convert.ToDouble s[3]) + " (min.)"
                        | "Su", Some s when name = "Room" && not (isNull s[4]) -> fmt 1 (Convert.ToDouble s[4]) + " (min.)"
                        | _ -> match evaluate c t with Some v -> allowableCell c v t 1 | None -> "n.d."
                    { Property = c.Code; Unit = "MPa"; Curve = (if label = "" then "" else label + " ") + $"#{c.Id}" + refText (curveNotes c); Cells = columns |> List.map cell }
                // Physical properties: name, unit, scale to display unit, decimals
                let physical =
                    [ "E", "MPa", 1., 0; "Alpha", "10⁻⁶/°C", 1e6, 2; "AlphaInstantaneous", "10⁻⁶/°C", 1e6, 2; "c", "J/(kg·K)", 1., 0
                      "k", "W/(m·K)", 1., 1; "a", "mm²/s", 1e6, 2; "density", "kg/m³", 1., 0; "Nu", "—", 1., 3 ]
                let constantOf family =
                    let column = if family = "density" then "ConstantDensity" else "ConstantPoissonRatio"
                    match Asme2025Data.query db $"SELECT {column} FROM Extension WHERE id_main=$id" [ "$id", box materialId ] with
                    | [ row ] when not (isNull row[0]) -> Some(Convert.ToDouble row[0])
                    | _ -> None
                let physicalRows =
                    physical |> List.collect (fun (family, unit, scale, decimals) ->
                        let curves = Asme2025Data.curves db materialId family
                        if not curves.IsEmpty then
                            curves |> List.map (fun c ->
                                usedCurves.Add c
                                let cell (_, t) = match evaluate c t with Some v -> fmt decimals (v * scale) | None -> "n.d."
                                { Property = family; Unit = unit; Curve = $"#{c.Id}" + refText (curveNotes c); Cells = columns |> List.map cell })
                        else
                            match (if family = "density" || family = "Nu" then constantOf family else None) with
                            | Some v -> [ { Property = family; Unit = unit; Curve = "costante"; Cells = columns |> List.map (fun _ -> fmt decimals (v * scale)) } ]
                            | None -> [ { Property = family; Unit = unit; Curve = "dato assente"; Cells = columns |> List.map (fun _ -> "—") } ])
                let sb = StringBuilder()
                let line (s: string) = sb.Append(s).Append('\n') |> ignore
                let ident = [ material.Specification; material.Grade; material.Condition; material.UNS ] |> List.filter ((<>) "") |> String.concat " · "
                line $"# Scheda prodotto — {ident}"
                line ""
                line "> Documento generato dal database ASME 2025: tutti i valori sono interpolati linearmente all'interno della propria curva e **non estrapolati** (`n.d.` = fuori campo o dato assente). Temperature in °C, tensioni in MPa, dimensioni in mm."
                line ""
                line "## 1. Identificazione"
                line ""
                line "| Campo | Valore |"
                line "|---|---|"
                line $"| Specifica | {material.Specification} |"
                line ("| Grado | " + orDash material.Grade + " |")
                line ("| Classe / condizione / tempra | " + orDash material.Condition + " |")
                line ("| UNS | " + orDash material.UNS + " |")
                line ("| Composizione nominale | " + orDash material.NominalComposition + " |")
                let scalar (v: obj) (unit: string) = if isNull v then "—" else fmt 1 (Convert.ToDouble v) + " " + unit
                line ("| Snervamento minimo specificato (SMYS) | " + scalar main[0] "MPa" + " |")
                line ("| Resistenza minima specificata (SMUS) | " + scalar main[1] "MPa" + " |")
                line ("| Allungamento a rottura (long. / trasv.) | " + scalar main[2] "%" + " / " + scalar main[3] "%" + " |")
                line ""
                line "## 2. Condizioni di riferimento"
                line ""
                line "| Colonna | Temperatura |"
                line "|---|---|"
                line $"| Room | {fmt 1 conditions.RoomTemperature} °C |"
                conditions.DesignTemperatures |> List.iteri (fun i t -> line $"| Design {i + 1} | {fmt 1 t} °C |")
                conditions.OperatingTemperatures |> List.iteri (fun i t -> line $"| Operating {i + 1} | {fmt 1 t} °C |")
                line ""
                line "A temperatura ambiente Sy e Su riportano il **minimo specificato** della riga ASME (marcato `(min.)`); le altre proprietà sono interpolate sulla curva. Le tabelle ASME delle tensioni ammissibili iniziano a 40 °C: sotto il primo punto il valore non è inventato (`n.d.`); vedere le note generali di tabella sul loro uso a temperature più basse."
                line ""
                let secs = sections mechanical
                let sectionNumber = ref 0
                line "## 3. Proprietà per dimensione"
                for s in secs do
                    sectionNumber.Value <- sectionNumber.Value + 1
                    line ""
                    line $"### 3.{sectionNumber.Value} {title symbol s}"
                    line ""
                    let header = columns |> List.mapi (fun i (name, t) -> $"{name} {fmt 1 t} °C")
                    line ("| Proprietà | Unità | Curva (forma, livello, id) [note] | " + String.Join(" | ", header) + " |")
                    line ("|---|---|---|" + String.Join("", columns |> List.map (fun _ -> "---:|")))
                    let applicable family = mechanical |> List.filter (fun c -> c.Code = family && applies c s.Probe)
                    let mechanicalRows = mechanicalFamilies |> List.collect (fun f -> match applicable f with [] -> [ { Property = f; Unit = "MPa"; Curve = "nessuna curva per questa dimensione"; Cells = columns |> List.map (fun _ -> "—") } ] | cs -> cs |> List.map mechanicalRow)
                    for r in mechanicalRows @ physicalRows do
                        let cells = String.Join(" | ", r.Cells)
                        line ($"| {r.Property} | {r.Unit} | {r.Curve} | {cells} |")
                // External pressure
                line ""
                line "## 4. Pressione esterna"
                line ""
                let pressure = Asme2025Data.pressureCurves db materialId
                if pressure.IsEmpty then line "Nessun diagramma di pressione esterna associato al materiale."
                else
                    let chart = pressure |> List.map (fun (_, name, _, _) -> name) |> List.distinct |> String.concat ", "
                    line $"Diagramma: **{chart}**. Per ogni temperatura: coppie (A adimensionale → B in MPa)."
                    line ""
                    line "| Temperatura | Punti A → B |"
                    line "|---|---|"
                    for (_, _, t, points) in pressure do
                        let pairs = points |> List.map (fun (a, b) -> a.ToString("0.###E+0", CultureInfo.InvariantCulture) + " → " + fmt 2 b) |> String.concat "; "
                        line ("| " + fmt 1 t + " °C | " + pairs + " |")
                // Code limits per curve
                line ""
                line "## 5. Limiti di applicabilità per curva"
                line ""
                line "| Curva | P-No. | Group | Diagramma EP | Sez. I | Sez. III | VIII-1 | VIII-2 | XII |"
                line "|---|---|---|---|---|---|---|---|---|"
                let limited = usedCurves |> Seq.filter (fun c -> List.contains c.Code [ "S1"; "S2"; "Sb1"; "Sb2" ]) |> Seq.distinctBy (fun c -> c.Id) |> Seq.toList
                let dash (v: string) = if v = "" then "—" else v
                for c in limited do
                    let l = c.CodeLimits
                    line $"| {c.Code} #{c.Id} | {dash l.PNumber} | {dash l.GroupNumber} | {dash l.ExternalPressureChart} | {dash l.MaxTempSectionI} | {dash l.MaxTempSectionIII} | {dash l.MaxTempSectionVIII1} | {dash l.MaxTempSectionVIII2} | {dash l.MaxTempSectionXII} |"
                if limited.IsEmpty then line "| — | — | — | — | — | — | — | — | — |"
                line ""
                line "Limiti in °C. `NP` = non permesso; un valore con qualifica (es. `204 (Cl. 3 only)`) non è applicato come limite generale. Il simbolo ⚠ nelle tabelle indica una temperatura oltre il limite numerico della Sezione della curva (VIII-1 per S1/Sb1, VIII-2 per S2/Sb2)."
                // Curve register
                line ""
                line "## 6. Curve utilizzate"
                line ""
                line "| Id | Famiglia | Forma | Dimensione | Fonte | Punti | Intervallo T |"
                line "|---|---|---|---|---|---|---|"
                let familyOrder = mechanicalFamilies @ [ "E"; "Alpha"; "AlphaInstantaneous"; "c"; "k"; "a"; "density"; "Nu" ]
                let orderOf (c: Asme2025Curve) = (familyOrder |> List.tryFindIndex ((=) c.Code) |> Option.defaultValue 99), c.Id
                for c in usedCurves |> Seq.distinctBy (fun c -> c.Id, c.Code) |> Seq.sortBy orderOf do
                    let size = title symbol { Lower = (if c.SizeMinimum.HasValue then Some c.SizeMinimum.Value else None); LowerIncluded = c.MinimumIncluded; Upper = (if c.SizeMaximum.HasValue then Some c.SizeMaximum.Value else None); UpperIncluded = c.MaximumIncluded; Probe = 0. }
                    // Physical curves live in another table: their ids must never be looked up in Property_Source.
                    let src =
                        if not (List.contains c.Code mechanicalFamilies) then (if c.SourceNotes <> "" then "importata da XML" else "inserita dall'utente")
                        else
                            match sourceRow c.Id with
                            | Some s -> String.Join(" ", [ text s[0]; (if text s[1] = "" then "" else "p." + text s[1]); (if text s[2] = "" then "" else "r." + text s[2]) ] |> List.filter ((<>) ""))
                            | None -> if c.SourceTable = "User" then "inserita dall'utente" else c.SourceTable
                    let range = if c.Points.Length = 0 then "—" else $"{fmt 1 c.Points[0].Temperature}…{fmt 1 c.Points[c.Points.Length - 1].Temperature} °C"
                    let sizeText = if c.SizeMinimum.HasValue || c.SizeMaximum.HasValue then size else "qualsiasi"
                    line ($"| {c.Id} | {c.Code} | {dash c.ProductForm} | {sizeText} | {src} | {c.Points.Length} | {range} |")
                // Notes
                line ""
                line "## 7. Note"
                line ""
                if notes.Count = 0 then line "Nessuna nota associata alle curve utilizzate."
                else
                    notes |> Seq.iteri (fun i n -> line $"{i + 1}. {n.Replace('\n', ' ')}")
                line ""
                line "**Note generali di lettura**"
                line ""
                line "- Interpolazione lineare tra i due punti adiacenti della curva; nessuna estrapolazione."
                line "- Alpha medio è riferito alla temperatura di riferimento della fonte (convenzionalmente 20 °C); `a` è mostrata in mm²/s (1 mm²/s = 10⁻⁶ m²/s)."
                line "- Più curve nella stessa riga di proprietà indicano varianti (forma di prodotto, livello di tensione S1): non si sceglie «la prima»."
                line "- Una proprietà assente (`dato assente`) non viene calcolata né copiata da altri materiali."
                Ok(sb.ToString())
