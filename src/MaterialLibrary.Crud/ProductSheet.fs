// Author: Claude
// Last revision: 2026-11-03
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
    let private inv = CultureInfo.InvariantCulture

    /// <summary>Format a number with up to <paramref name="decimals"/> decimals, English separators (decimal point).</summary>
    let private fmt (decimals: int) (value: float) =
        let pattern = if decimals = 0 then "0" else "0." + String('#', decimals)
        value.ToString(pattern, inv)

    /// <summary>Short column label of a temperature column: Design = T.Des, Operating = T.Ope, Room unchanged.</summary>
    let private shortName (column: string) =
        match column with
        | "Design" -> "T.Des"
        | "Operating" -> "T.Ope"
        | other -> other

    /// <summary>Colour marker of a temperature column (emoji, so it survives every Markdown renderer): Room = blue, Design = red, Operating = green.</summary>
    let private marker (column: string) =
        match column with
        | "Room" -> "🔵"
        | "Design" -> "🔴"
        | "Operating" -> "🟢"
        | _ -> ""

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
        | None, None -> "any size"
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

    /// <summary>
    /// Pad every Markdown table of the text so that its columns line up in the source (rendering is unchanged).
    /// Columns whose separator ends with ':' are right-aligned. Pure function: text in, text out.
    /// </summary>
    let private alignTables (markdown: string) =
        let isRow (l: string) = l.StartsWith "|"
        // Cells of a row, without the outer pipes and with surrounding blanks removed.
        let cellsOf (l: string) = l.Trim().Trim('|').Split('|') |> Array.map (fun c -> c.Trim())
        let isSeparator (cells: string[]) = cells |> Array.forall (fun c -> c.Length >= 3 && c.TrimEnd(':') |> Seq.forall ((=) '-'))
        let formatBlock (block: string list) =
            let rows = block |> List.map cellsOf
            let columns = rows |> List.map Array.length |> List.max
            // Column width: widest cell, at least 3 so that the separator stays valid.
            let width i = rows |> List.filter (fun r -> not (isSeparator r) && i < r.Length) |> List.fold (fun w r -> max w r[i].Length) 3
            let widths = Array.init columns width
            rows |> List.map (fun r ->
                let cells =
                    Array.init columns (fun i ->
                        let c = if i < r.Length then r[i] else ""
                        let right = isSeparator r && c.EndsWith ":"
                        if isSeparator r then (if right then String('-', widths[i] - 1) + ":" else String('-', widths[i]))
                        else
                            // Right-align when the separator row of this table marks the column with ':'.
                            let rightAligned = rows |> List.exists (fun s -> isSeparator s && i < s.Length && s[i].EndsWith ":")
                            if rightAligned then c.PadLeft widths[i] else c.PadRight widths[i])
                "| " + String.Join(" | ", cells) + " |")
        // Group consecutive table lines into blocks; every other line is copied unchanged.
        let output = ResizeArray<string>()
        let mutable block = []
        let flush () =
            if not block.IsEmpty then output.AddRange(formatBlock (List.rev block)); block <- []
        for l in markdown.Split('\n') do
            if isRow l then block <- l :: block
            else flush (); output.Add l
        flush ()
        String.Join("\n", output)

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
                // Notes of the table being printed: numbered from 1 in every size-range table and cleared at its start.
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
                            for n in c.UserNoteItems do refs <- refs @ [ noteRef $"**ASME {n.SourceTable} / {n.Code}** (user-edited copy): {n.Text}" ]
                        else
                            for n in Asme2025Data.query db "SELECT SourceTable,Code,Kind,NoteText FROM VW_Property_Notes_All WHERE id_property=$id ORDER BY SourceTable,Kind,Code" [ "$id", box c.Id ] do
                                refs <- refs @ [ noteRef $"**ASME {text n[0]} / {text n[1]}** ({text n[2]}): {text n[3]}" ]
                    elif c.SourceNotes <> "" then refs <- refs @ [ noteRef $"**XML source:** {c.SourceNotes.Replace('\n', ' ')}" ]
                    if c.UserNotes <> "" then refs <- refs @ [ noteRef $"**User note:** {c.UserNotes}" ]
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
                // Material-level limits (ASME_Data): each curve carries only the limits printed in its own ASME table row, so the Section
                // columns of the other table (e.g. VIII-2 on an S1 curve, VIII-1 on an S2 curve) are blank there and are completed from here.
                let materialLimits =
                    if (Asme2025Data.query db "SELECT 1 FROM sqlite_master WHERE type='table' AND name='ASME_Data'" []).IsEmpty then Asme2025Data.emptyCodeLimits
                    else
                        // Numbers are printed without trailing zeros (371.0 -> 371); text fields are copied as they are.
                        let cell (v: obj) = if isNull v then "" else (match v with :? float as f -> f.ToString("0.###", inv) | other -> string other)
                        match Asme2025Data.query db "SELECT P_Number,Group_Number,External_Pressure_Chart_No,Max_Temp_Sect_I,Max_Temp_Sect_III,Max_Temp_Sect_VIII_1,Max_Temp_Sect_VIII_2,Max_Temp_Sect_XII FROM ASME_Data WHERE id_main=$id" [ "$id", box materialId ] with
                        | r :: _ ->
                            Asme2025Data.normalizeCodeLimits
                                { PNumber = cell r[0]; GroupNumber = cell r[1]; ExternalPressureChart = cell r[2]; MaxTempSectionI = cell r[3]; MaxTempSectionIII = cell r[4]
                                  MaxTempSectionVIII1 = cell r[5]; MaxTempSectionVIII2 = cell r[6]; MaxTempSectionXII = cell r[7] }
                        | [] -> Asme2025Data.emptyCodeLimits
                // Allowable-stress rows follow the Section column of their own curve: VIII-1 for S1/Sb1, VIII-2 for S2/Sb2 (own value first, then the material-level one).
                let allowableCell (c: Asme2025Curve) (value: float) (t: float) (decimals: int) =
                    let own, fallback =
                        match c.Code with
                        | "S1" | "Sb1" -> c.CodeLimits.MaxTempSectionVIII1, materialLimits.MaxTempSectionVIII1
                        | "S2" | "Sb2" -> c.CodeLimits.MaxTempSectionVIII2, materialLimits.MaxTempSectionVIII2
                        | _ -> "", ""
                    let raw = if own <> "" then own else fallback
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
                // Built per table (not once) because the note numbers of a row depend on the table it is printed in.
                let physicalRowsOf () =
                    physical |> List.collect (fun (family, unit, scale, decimals) ->
                        let curves = Asme2025Data.curves db materialId family
                        if not curves.IsEmpty then
                            curves |> List.map (fun c ->
                                usedCurves.Add c
                                let cell (_, t) = match evaluate c t with Some v -> fmt decimals (v * scale) | None -> "n.d."
                                { Property = family; Unit = unit; Curve = $"#{c.Id}" + refText (curveNotes c); Cells = columns |> List.map cell })
                        else
                            match (if family = "density" || family = "Nu" then constantOf family else None) with
                            | Some v -> [ { Property = family; Unit = unit; Curve = "constant"; Cells = columns |> List.map (fun _ -> fmt decimals (v * scale)) } ]
                            | None -> [ { Property = family; Unit = unit; Curve = "no data"; Cells = columns |> List.map (fun _ -> "—") } ])
                let sb = StringBuilder()
                let line (s: string) = sb.Append(s).Append('\n') |> ignore
                let ident = [ material.Specification; material.Grade; material.Condition; material.UNS ] |> List.filter ((<>) "") |> String.concat " · "
                line $"# Product data sheet — {ident}"
                line ""
                line "> Document generated from the ASME 2025 database: every value is linearly interpolated inside its own curve and **never extrapolated** (`n.d.` = outside the curve or no data). Temperatures in °C, stresses in MPa, sizes in mm."
                line ""
                line "## 1. Identification"
                line ""
                line "| Field | Value |"
                line "|---|---|"
                line $"| Specification | {material.Specification} |"
                line ("| Grade | " + orDash material.Grade + " |")
                line ("| Class / condition / temper | " + orDash material.Condition + " |")
                line ("| UNS | " + orDash material.UNS + " |")
                line ("| Nominal composition | " + orDash material.NominalComposition + " |")
                let scalar (v: obj) (unit: string) = if isNull v then "—" else fmt 1 (Convert.ToDouble v) + " " + unit
                line ("| Specified minimum yield strength (SMYS) | " + scalar main[0] "MPa" + " |")
                line ("| Specified minimum tensile strength (SMUS) | " + scalar main[1] "MPa" + " |")
                line ("| Elongation at rupture (long. / transv.) | " + scalar main[2] "%" + " / " + scalar main[3] "%" + " |")
                line ""
                let roomMark, designMark, operatingMark = marker "Room", marker "Design", marker "Operating"
                line "## 2. Reference conditions"
                line ""
                line "| Column | Temperature |"
                line "|---|---|"
                line $"| {roomMark} Room | {fmt 1 conditions.RoomTemperature} °C |"
                conditions.DesignTemperatures |> List.iteri (fun i t -> line $"| {designMark} T.Des {i + 1} | {fmt 1 t} °C |")
                conditions.OperatingTemperatures |> List.iteri (fun i t -> line $"| {operatingMark} T.Ope {i + 1} | {fmt 1 t} °C |")
                line ""
                line "T.Des = design temperature, T.Ope = operating temperature. At room temperature Sy and Su report the **specified minimum** of the ASME row (marked `(min.)`); the other properties are interpolated on the curve. The ASME allowable-stress tables start at 40 °C: below the first point the value is not invented (`n.d.`); see the general table notes on their use at lower temperatures."
                line ""
                let secs = sections mechanical
                let sectionNumber = ref 0
                line "## 3. Properties by size"
                for s in secs do
                    sectionNumber.Value <- sectionNumber.Value + 1
                    notes.Clear()
                    line ""
                    line $"### 3.{sectionNumber.Value} {title symbol s}"
                    line ""
                    // Two-line column title (short name <br> temperature) keeps the numeric columns narrow.
                    let header = columns |> List.map (fun (name, t) -> $"{marker name} {shortName name}<br>{fmt 1 t} °C")
                    line ("| Property | Curve [notes] | " + String.Join(" | ", header) + " |")
                    line ("|---|---|" + String.Join("", columns |> List.map (fun _ -> "---:|")))
                    let applicable family = mechanical |> List.filter (fun c -> c.Code = family && applies c s.Probe)
                    let mechanicalRows = mechanicalFamilies |> List.collect (fun f -> match applicable f with [] -> [ { Property = f; Unit = "MPa"; Curve = "no curve"; Cells = columns |> List.map (fun _ -> "—") } ] | cs -> cs |> List.map mechanicalRow)
                    let allRows = mechanicalRows @ physicalRowsOf ()
                    for r in allRows do
                        let cells = String.Join(" | ", r.Cells)
                        line ($"| {r.Property} | {r.Curve} | {cells} |")
                    // Units are printed once under the table instead of in a column: one entry per unit, properties in table order.
                    let unitText (unit: string) = if unit = "—" then "dimensionless" else unit
                    let units =
                        allRows |> List.groupBy (fun r -> r.Unit)
                        |> List.map (fun (unit, rs) -> String.Join(", ", rs |> List.map (fun r -> r.Property)) + ": " + unitText unit)
                    line ""
                    line ("Units: " + String.Join("; ", units) + ".")
                    // Notes referenced by this table only, under the table, numbered as in the [..] references of its rows.
                    if notes.Count > 0 then
                        line ""
                        line $"**Notes of table 3.{sectionNumber.Value}**"
                        line ""
                        notes |> Seq.iteri (fun i n -> line $"{i + 1}. {n.Replace('\n', ' ')}")
                // Code limits per curve (the external-pressure chart has its own section below)
                line ""
                line "## 4. Maximum application temperatures and code limits"
                line ""
                line "| Curve | P-No. | Group | Sect. I | Sect. III | VIII-1 | VIII-2 | XII |"
                line "|---|---|---|---|---|---|---|---|"
                let limited = usedCurves |> Seq.filter (fun c -> List.contains c.Code [ "S1"; "S2"; "Sb1"; "Sb2" ]) |> Seq.distinctBy (fun c -> c.Id) |> Seq.toList
                // A blank limit of the curve is completed with the material-level value (marked †); blank on both sides prints a dash.
                let limitCell (own: string) (fallback: string) = if own <> "" then own elif fallback <> "" then fallback + " †" else "—"
                for c in limited do
                    let l, m = c.CodeLimits, materialLimits
                    line $"| {c.Code} #{c.Id} | {limitCell l.PNumber m.PNumber} | {limitCell l.GroupNumber m.GroupNumber} | {limitCell l.MaxTempSectionI m.MaxTempSectionI} | {limitCell l.MaxTempSectionIII m.MaxTempSectionIII} | {limitCell l.MaxTempSectionVIII1 m.MaxTempSectionVIII1} | {limitCell l.MaxTempSectionVIII2 m.MaxTempSectionVIII2} | {limitCell l.MaxTempSectionXII m.MaxTempSectionXII} |"
                if limited.IsEmpty then line "| — | — | — | — | — | — | — | — |"
                line ""
                line "Limits in °C. `NP` = not permitted; a qualified value (e.g. `204 (Cl. 3 only)`) is not applied as a general limit. † = not printed in the ASME table row of the curve: taken from the material-level data (same material, other ASME table). The ⚠ symbol in the tables marks a temperature above the numeric limit of the curve's Section (VIII-1 for S1/Sb1, VIII-2 for S2/Sb2)."
                // External pressure charts: a section of their own, one sub-section per chart
                line ""
                line "## 5. External pressure charts"
                line ""
                let pressure = Asme2025Data.pressureCurves db materialId
                // Chart reference printed in the ASME table: the first non-blank value among the curves, else the material-level one.
                let chartReference =
                    (limited |> List.map (fun c -> c.CodeLimits.ExternalPressureChart)) @ [ materialLimits.ExternalPressureChart ] |> List.tryFind ((<>) "")
                match chartReference with
                | Some r -> line $"Chart reference in the ASME tables: **{r}**."; line ""
                | None -> ()
                if pressure.IsEmpty then line "No external pressure chart is linked to the material."
                else
                    line "For each temperature: pairs (A, dimensionless → B, MPa)."
                    pressure |> List.groupBy (fun (_, name, _, _) -> name) |> List.iteri (fun i (name, curvesOfChart) ->
                        line ""
                        line $"### 5.{i + 1} Chart {name}"
                        line ""
                        line "| Temperature | A → B points |"
                        line "|---|---|"
                        for (_, _, t, points) in curvesOfChart do
                            let pairs = points |> List.map (fun (a, b) -> a.ToString("0.###E+0", inv) + " → " + fmt 2 b) |> String.concat "; "
                            line ("| " + fmt 1 t + " °C | " + pairs + " |"))
                // Curve register
                line ""
                line "## 6. Curves used"
                line ""
                line "| Id | Family | Form | Size | Source | Points | T range |"
                line "|---|---|---|---|---|---|---|"
                let familyOrder = mechanicalFamilies @ [ "E"; "Alpha"; "AlphaInstantaneous"; "c"; "k"; "a"; "density"; "Nu" ]
                let orderOf (c: Asme2025Curve) = (familyOrder |> List.tryFindIndex ((=) c.Code) |> Option.defaultValue 99), c.Id
                let dash (v: string) = if v = "" then "—" else v
                for c in usedCurves |> Seq.distinctBy (fun c -> c.Id, c.Code) |> Seq.sortBy orderOf do
                    let size = title symbol { Lower = (if c.SizeMinimum.HasValue then Some c.SizeMinimum.Value else None); LowerIncluded = c.MinimumIncluded; Upper = (if c.SizeMaximum.HasValue then Some c.SizeMaximum.Value else None); UpperIncluded = c.MaximumIncluded; Probe = 0. }
                    // Physical curves live in another table: their ids must never be looked up in Property_Source.
                    let src =
                        if not (List.contains c.Code mechanicalFamilies) then (if c.SourceNotes <> "" then "imported from XML" else "entered by the user")
                        else
                            match sourceRow c.Id with
                            | Some s -> String.Join(" ", [ text s[0]; (if text s[1] = "" then "" else "p." + text s[1]); (if text s[2] = "" then "" else "l." + text s[2]) ] |> List.filter ((<>) ""))
                            | None -> if c.SourceTable = "User" then "entered by the user" else c.SourceTable
                    let range = if c.Points.Length = 0 then "—" else $"{fmt 1 c.Points[0].Temperature}…{fmt 1 c.Points[c.Points.Length - 1].Temperature} °C"
                    let sizeText = if c.SizeMinimum.HasValue || c.SizeMaximum.HasValue then size else "any"
                    line ($"| {c.Id} | {c.Code} | {dash c.ProductForm} | {sizeText} | {src} | {c.Points.Length} | {range} |")
                // General reading notes (the notes of each curve are printed under its own table in section 3)
                line ""
                line "## 7. General reading notes"
                line ""
                line "- Colour code of the temperature columns: 🔵 Room, 🔴 T.Des (design), 🟢 T.Ope (operating)."
                line "- The Curve column gives the product form, the stress level (S1), the curve id and, in square brackets, the note numbers listed under the table itself (numbering restarts in every table); `no curve` = no curve valid for that size."
                line "- Linear interpolation between the two adjacent points of the curve; no extrapolation."
                line "- Mean Alpha refers to the reference temperature of the source (conventionally 20 °C); `a` is shown in mm²/s (1 mm²/s = 10⁻⁶ m²/s)."
                line "- Several curves in the same property row indicate variants (product form, S1 stress level): «the first one» is never chosen silently."
                line "- A missing property (`no data`) is neither computed nor copied from other materials."
                Ok(alignTables (sb.ToString()))
