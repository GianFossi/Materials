// Author: Copilot
// Last revision: 2026-11-01
// Purpose: Read physical-property XML with explicit units, provenance and review diagnostics.
namespace MaterialLibrary.Crud

open System
open System.IO
open System.Globalization
open System.Security.Cryptography
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Read structured XML and explicitly reviewable PDF-token staging files without treating source text as instructions.</summary>
module XmlPropertyReader =
    /// <summary>Parse a finite invariant scalar; blank/missing markers never become zero.</summary>
    let number (text: string) =
        match Double.TryParse(text.Trim().Replace('−','-'), NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true,value when Double.IsFinite value -> Some value
        | _ -> None
    /// <summary>Normalize text for explicit field matching only.</summary>
    let normalize (text: string) = Regex.Replace(text.Trim().Replace('–','-').Replace('—','-'),"\\s+"," ").ToUpperInvariant()
    /// <summary>Read an attribute or direct child, ignoring XML namespace and letter case.</summary>
    let value name (node: XElement) =
        node.Attributes() |> Seq.tryFind (fun a -> a.Name.LocalName.Equals(name,StringComparison.OrdinalIgnoreCase)) |> Option.map (fun a -> a.Value)
        |> Option.orElseWith (fun () -> node.Elements() |> Seq.tryFind (fun e -> e.Name.LocalName.Equals(name,StringComparison.OrdinalIgnoreCase)) |> Option.map (fun e -> e.Value)) |> Option.defaultValue ""
    /// <summary>Convert only declared, recognized units to the editor's fixed units.</summary>
    let unitScale code unit =
        match code,normalize unit with
        | "E","GPA" -> Some 1000.
        | "Alpha","10^-6/K" | "AlphaInstantaneous","10^-6/K" | "Alpha","10-6/K" | "AlphaInstantaneous","10-6/K" -> Some 1e-6
        | "a","MM2/S" | "a","MM²/S" -> Some 1e-6
        | "E","MPA" | "Alpha","1/K" | "Alpha","1/DEGC" | "AlphaInstantaneous","1/K" | "AlphaInstantaneous","1/DEGC"
        | "c","J/(KG*K)" | "k","W/(M*K)" | "a","M2/S" | "a","M²/S" | "density","KG/M3" | "density","KG/M³"
        | "Nu","DIMENSIONLESS" | "EP","MPA" -> Some 1.
        | _ -> None
    /// <summary>Canonical physical aliases keep mean and instantaneous expansion distinct.</summary>
    let code (text: string) =
        match normalize text with
        | "ALPHAMEAN" | "ALPHA_MEAN" | "ALPHA-MEAN" | "MEAN" -> "Alpha"
        | "ALPHAINSTANTANEOUS" | "ALPHA_INSTANTANEOUS" | "ALPHA-INSTANTANEOUS" | "INSTANTANEOUS" -> "AlphaInstantaneous"
        | "SPECIFICHEAT" -> "c" | "ELASTICYOUNGMODULUS" -> "E" | "THERMALCONDUCTIVITY" -> "k" | "THERMALDIFFUSIVITY" -> "a" | "POISSONRATIO" -> "Nu"
        | "DENS" -> "density" | "EXTERNALPRESSURE" | "EXTERNAL PRESSURE" | "EP" -> "EP"
        | _ -> Asme2025Data.canonicalCode text
    /// <summary>Create a record from structured points, preserving original XML for provenance.</summary>
    let private structured (baseRecord: XmlPropertyRecord) (node: XElement) =
        let c = code (value "property" node)
        let unit = value "unit" node
        let scale = unitScale c unit
        let points = node.Descendants() |> Seq.filter (fun e -> e.Name.LocalName.Equals("Point",StringComparison.OrdinalIgnoreCase)) |> Seq.toArray
        let mutable invalid = false
        let parsed = points |> Array.choose (fun p ->
            let temperature = number (value "temperature" p) |> Option.orElseWith (fun () -> if value "constant" node="true" then Some 20. else None)
            let x = if c="EP" then number (value "A" p) else temperature
            let y = number (value (if c="EP" then "B" else "value") p)
            match temperature,x,y,scale with
            | Some t,Some x,Some y,Some s -> Some {Temperature=t;X=x;Y=y*s}
            | _ -> invalid <- true; None)
        {baseRecord with Key=baseRecord.Key+"/"+value "id" node;Description=value "description" node;PropertyCode=c;Unit=(if c="EP" then "MPa" else Asme2025Data.families |> Array.tryFind (fun (code,_) -> code=c) |> Option.map snd |> Option.defaultValue unit);
                         ChartName=value "chart" node;Specification=value "specification" node;Grade=value "grade" node;Condition=value "condition" node;
                         UNS=value "uns" node;Composition=(if value "nominalChemicalComposition" node<>"" then value "nominalChemicalComposition" node else value "composition" node);Notes=value "notes" node;IsConstant=value "constant" node="true";
                         Points=(if invalid then [||] else parsed);RequiresReview=false;
                         Diagnostic=(if scale.IsNone then "Unità non riconosciuta: scegliere famiglia/unità e verificare i valori." elif invalid then "Uno o più punti non validi: nessuna riga è stata scartata silenziosamente; correggere il record." else "XML strutturato; verificare associazione e unità.")}
    /// <summary>Expand compact material constants into density (kg/m3) and Nu (dimensionless) records with shared identity.</summary>
    let private materialConstants (baseRecord: XmlPropertyRecord) (root: XElement) =
        let materials = root.Elements() |> Seq.filter (fun n -> n.Name.LocalName="Materials") |> Seq.collect (fun n -> n.Elements()) |> Seq.toList
        if materials.IsEmpty then raise (FormatException("XML material-constants-v1: nessun materiale presente."))
        materials |> List.collect (fun material ->
            let id, name = value "id" material, value "name" material
            if material.Name.LocalName<>"Material" || String.IsNullOrWhiteSpace id || String.IsNullOrWhiteSpace name then
                raise (FormatException("XML material-constants-v1: ogni voce richiede Material, id e name."))
            let uns, composition = value "uns" material, value "nominalChemicalComposition" material
            let manual = String.IsNullOrWhiteSpace uns && String.IsNullOrWhiteSpace composition
            let sourceMaterial, sourcePage = value "sourceMaterial" material, value "sourcePdfPage" material
            let notes = String.concat " " [baseRecord.Notes; $"ASME Table PRD: {sourceMaterial}; source PDF page {sourcePage}."]
            [ "density", "density", "densityUnit"; "poisson", "Nu", "poissonRatioUnit" ]
            |> List.map (fun (suffix, propertyCode, unitAttribute) ->
                let scalarAttribute = if propertyCode="Nu" then "poissonRatio" else "density"
                let scalar, unit = value scalarAttribute material, value unitAttribute root
                match number scalar, unitScale propertyCode unit with
                | Some n, Some 1. when n>0. && (propertyCode<>"Nu" || n<0.5) -> ()
                | _ -> raise (FormatException($"PRD {name}: {scalarAttribute} o unità non validi."))
                let node = XElement(XName.Get "Record",
                    XAttribute(XName.Get "id", $"prd-{suffix}-{id}"),
                    XAttribute(XName.Get "property", propertyCode),
                    XAttribute(XName.Get "unit", unit),
                    XAttribute(XName.Get "constant", "true"),
                    XAttribute(XName.Get "description", name),
                    XAttribute(XName.Get "uns", uns),
                    XAttribute(XName.Get "nominalChemicalComposition", composition),
                    XElement(XName.Get "notes", notes),
                    XElement(XName.Get "Point", XAttribute(XName.Get "value", scalar)))
                let record = structured baseRecord node
                {record with RequiresReview=manual;
                             Diagnostic=(if manual then "PRD: famiglia senza UNS/composizione esatta; associazione manuale e verifica richieste." else "PRD: chiave UNS/composizione esplicita; verificare applicabilità e associazione.")}))
    /// <summary>Expand shared SI curves and constants (E MPa, k W/(m*K), a m2/s, expansion 1/degC, density kg/m3, Nu dimensionless) with declared limits.</summary>
    let private materialCurves (baseRecord: XmlPropertyRecord) (root: XElement) =
        let curves = root.Elements(XName.Get "Curves") |> Seq.collect (fun n -> n.Elements(XName.Get "Curve")) |> Seq.toList
        if curves.IsEmpty then raise (FormatException("XML material-curves-v1: nessuna curva presente."))
        let curveIds = curves |> List.map (value "id")
        if curveIds |> List.exists String.IsNullOrWhiteSpace || (curveIds |> List.distinct |> List.length)<>curves.Length then
            raise (FormatException("XML material-curves-v1: ID curva mancanti o duplicati."))
        curves |> List.collect (fun curve ->
            let id = value "id" curve
            let propertyCode = value "property" curve
            let expectedUnit =
                match propertyCode with
                | "E" -> "MPa" | "k" -> "W/(m*K)" | "a" -> "m2/s"
                | "density" -> "kg/m3" | "Nu" -> "dimensionless"
                | _ -> "1/degC"
            if not (["E"; "k"; "a"; "Alpha"; "AlphaInstantaneous"; "density"; "Nu"] |> List.contains propertyCode) || value "unit" curve<>expectedUnit || value "temperatureUnit" curve<>"degC" then
                raise (FormatException($"Curva {id}: proprietà/unità non supportate; temperatura richiesta in degC."))
            let constantText = value "constant" curve
            let constant = constantText="true"
            if (constantText<>"" && constantText<>"true" && constantText<>"false") || (constant && propertyCode<>"density" && propertyCode<>"Nu") then
                raise (FormatException($"Curva {id}: constant è consentito solo per densità e Poisson."))
            let expansion = propertyCode="Alpha" || propertyCode="AlphaInstantaneous"
            let referenceText = value "referenceTemperature" curve
            if expansion && (number referenceText |> Option.exists (fun t -> t>= -273.15) |> not) then
                raise (FormatException($"Curva {id}: temperatura di riferimento mancante o non valida."))
            let points = curve.Elements(XName.Get "Points") |> Seq.collect (fun n -> n.Elements()) |> Seq.toArray
            if points.Length=0 then raise (FormatException($"Curva {id}: nessun punto presente."))
            if constant && (points.Length<>1 || value "temperature" points[0]<>"") then
                raise (FormatException($"Curva {id}: una costante richiede un unico valore senza temperatura."))
            let temperatures = points |> Array.map (fun point ->
                let temperature = if constant then Some 20. else number (value "temperature" point)
                match temperature, number (value "value" point) with
                | Some t, Some e when point.Name.LocalName="Point" && t>= -273.15 && e>0. && (propertyCode<>"Nu" || e<0.5) -> t
                | _ -> raise (FormatException($"Curva {id}: temperatura o valore della proprietà non validi.")))
            if temperatures |> Array.pairwise |> Array.exists (fun (a,b) -> b<=a) then
                raise (FormatException($"Curva {id}: temperature duplicate o non crescenti."))
            let materials = curve.Elements(XName.Get "Materials") |> Seq.collect (fun n -> n.Elements()) |> Seq.toList
            if materials.IsEmpty then raise (FormatException($"Curva {id}: nessuna identità materiale presente."))
            materials |> List.map (fun material ->
                let materialId, name = value "id" material, value "name" material
                if material.Name.LocalName<>"Material" || String.IsNullOrWhiteSpace materialId || String.IsNullOrWhiteSpace name then
                    raise (FormatException($"Curva {id}: ogni materiale richiede id e name."))
                let sourceMaterial, sourcePage = value "sourceMaterial" material, value "sourcePdfPage" material
                let applicability, grade = value "applicability" material, value "sourceGrade" material
                let maximumText, minimumText = value "maxTemperature" material, value "minTemperature" material
                let limit text =
                    if text="" then None
                    else
                        match number text with
                        | Some t when t>= -273.15 -> Some t
                        | _ -> raise (FormatException($"Curva {id}, materiale {name}: limite di temperatura non valido."))
                let maximum, minimum = limit maximumText, limit minimumText
                let inverted = match minimum,maximum with Some a,Some b -> a>b | _ -> false
                if inverted then
                    raise (FormatException($"Curva {id}, materiale {name}: intervallo di temperatura invertito."))
                let applicablePoints = points |> Array.filter (fun point ->
                    if constant then true
                    else
                        let temperature = number (value "temperature" point) |> Option.get
                        (maximum |> Option.forall (fun bound -> temperature<=bound)) &&
                        (minimum |> Option.forall (fun bound -> temperature>=bound)))
                if applicablePoints.Length=0 then raise (FormatException($"Curva {id}, materiale {name}: nessun punto entro il limite dichiarato."))
                let notes = String.concat " " [baseRecord.Notes; value "SourceNotes" root; value "notes" curve; value "notes" material;
                                               $"Source material: {sourceMaterial}; PDF page {sourcePage}; grade label: {grade}; applicability: {applicability}; minTemperature (degC): {minimumText}; maxTemperature (degC): {maximumText}; referenceTemperature (degC): {referenceText}."; if constant then "Constant: no source temperature; internal 20 degC is a reader convention, not a source datum." else ""]
                let node = XElement(XName.Get "Record",
                    XAttribute(XName.Get "id", id+"/"+materialId),
                    XAttribute(XName.Get "property", propertyCode),
                    XAttribute(XName.Get "unit", expectedUnit),
                    XAttribute(XName.Get "constant", if constant then "true" else "false"),
                    XAttribute(XName.Get "description", name),
                    XElement(XName.Get "notes", notes))
                for field in ["uns"; "nominalChemicalComposition"; "specification"; "grade"; "condition"] do
                    node.SetAttributeValue(XName.Get field, value field material)
                for point in applicablePoints do node.Add(XElement(point))
                let record = structured baseRecord node
                let manual = String.IsNullOrWhiteSpace record.UNS && String.IsNullOrWhiteSpace record.Composition
                let review = manual || value "requiresReview" material="true" || value "codeCase" root<>""
                {record with RequiresReview=review;
                             Diagnostic=(if manual then $"Curva {propertyCode}: famiglia senza UNS/composizione esatta; associazione manuale richiesta."
                                         elif review then $"Curva {propertyCode}: chiavi esplicite, ma verificare la condizione di applicabilità dichiarata."
                                         else $"Curva {propertyCode}: identità UNS/composizione esplicita; verificare applicabilità e associazione.")}))
    /// <summary>Split PDF text into source pages; numeric blocks never cross page or prose boundaries.</summary>
    let private pages (raw: string) = Regex.Split(raw,@"--- SourcePage \d+ ---") |> Array.filter (String.IsNullOrWhiteSpace >> not)
    /// <summary>Extract fully rectangular A/B/C or TC/TD blocks; retain source descriptions and force review.</summary>
    let private columnBlocks (record: XmlPropertyRecord) (pageIndex: int) (page: string) =
        let lines = page.Split('\n') |> Array.map (fun s -> s.Trim())
        let groupSize,first = if record.PropertyCode="Alpha" then 3,"A" else 2,"TC"
        let mutable start = Array.tryFindIndex ((=) first) lines |> Option.defaultValue -1
        let output = ResizeArray<XmlPropertyRecord>()
        if start>=0 then
            let mutable finish = start
            while finish<lines.Length && ((if groupSize=3 then ["A";"B";"C"] else ["TC";"TD"]) |> List.contains lines[finish]) do finish <- finish+1
            let columns = finish-start
            if columns>0 && columns%groupSize=0 then
                let count = columns/groupSize
                let points = Array.init columns (fun _ -> ResizeArray<XmlPropertyPoint>())
                let mutable cursor = finish
                let mutable last = Double.NegativeInfinity
                let mutable running = true
                while running && cursor+columns<lines.Length do
                    match number lines[cursor] with
                    | Some t when t>last && t>= -273.15 && t<=1500. && (lines[cursor+1..cursor+columns] |> Array.forall (fun v -> (number v).IsSome || (["...";"…"] |> List.contains v))) ->
                        for i in 0..columns-1 do number lines[cursor+1+i] |> Option.iter (fun v -> points[i].Add {Temperature=t;X=t;Y=v*(if groupSize=3 || i%2=1 then 1e-6 else 1.)})
                        cursor <- cursor+columns+1;last <- t
                    | _ -> running <- false
                if points |> Array.exists (fun p -> p.Count>0) then
                    let heading = String.concat " " lines[0..start-1]
                    let descriptions = Regex.Split(heading,@"(?i)Coefficients for") |> Array.skip 1
                    for g in 0..count-1 do
                        for offset,c,u in (if groupSize=3 then [0,"AlphaInstantaneous","1/degC";1,"Alpha","1/degC"] else [0,"k","W/(m*K)";1,"a","m2/s"]) do
                            if points[g*groupSize+offset].Count>0 then
                                output.Add {record with Key=record.Key+ $"/page-{pageIndex}/group-{g+1}/{c}";Description=(if descriptions.Length=count then descriptions[g] else $"{record.Description} / page {pageIndex}, gruppo {g+1}");PropertyCode=c;Unit=u;Points=points[g*groupSize+offset].ToArray();RawText=page;
                                                        Diagnostic="Blocco numerico rettangolare estratto. Verificare gruppo, note e conversione unità prima di confermare."}
        List.ofSeq output
    /// <summary>Extract named rows with a fixed temperature header from the raw modulus tables.</summary>
    let private modulus (record: XmlPropertyRecord) pageIndex (page: string) =
        let lines = page.Split('\n') |> Array.map (fun s -> s.Trim())
        let first = lines |> Array.tryFindIndex (fun s -> number s=Some -200. || number s=Some 25.)
        let output = ResizeArray<XmlPropertyRecord>()
        match first with
        | None -> ()
        | Some start ->
            let temps = lines[start..] |> Array.takeWhile (number >> Option.isSome) |> Array.choose number
            if temps.Length>=3 then
                let mutable cursor = start+temps.Length
                while cursor<lines.Length do
                    let labelStart = cursor
                    while cursor<lines.Length && (number lines[cursor]).IsNone && not (["...";"…"] |> List.contains lines[cursor]) do cursor <- cursor+1
                    let label = String.concat " " lines[labelStart..cursor-1]
                    if cursor+temps.Length<=lines.Length && label<>"" && (lines[cursor..cursor+temps.Length-1] |> Array.forall (fun v -> (number v).IsSome || (["...";"…"] |> List.contains v))) then
                        let points = temps |> Array.mapi (fun i t -> number lines[cursor+i] |> Option.map (fun v -> {Temperature=t;X=t;Y=v*1000.})) |> Array.choose id
                        if points.Length>0 then output.Add {record with Key=record.Key+ $"/page-{pageIndex}/row-{labelStart}";Description=label;PropertyCode="E";Unit="MPa";Points=points;RawText=page;Diagnostic="E estratto da riga PDF (×1000 MPa). Verificare riga, intestazione e note."}
                        cursor <- cursor+temps.Length
                    else cursor <- max (cursor+1) (labelStart+1)
        List.ofSeq output
    /// <summary>Extract PRD density/Poisson rows only after the PRD heading; preserve wrapped material labels.</summary>
    let private prd (record: XmlPropertyRecord) (raw: string) =
        let index = raw.IndexOf("Table PRD",StringComparison.Ordinal)
        if index<0 then []
        else
            let lines = raw.Substring(index).Split('\n') |> Array.map (fun s -> s.Trim())
            let output = ResizeArray<XmlPropertyRecord>()
            let mutable label = ResizeArray<string>()
            for i in 0..lines.Length-2 do
                match number lines[i] with
                | Some nu when nu>0. && nu<0.5 && Regex.IsMatch(lines[i+1],@"^\d{1,2} \d{3}$") ->
                    let description = String.concat " " label
                    let v = if record.PropertyCode="Nu" then nu else Double.Parse(lines[i+1].Replace(" ",""),CultureInfo.InvariantCulture)
                    output.Add {record with Key=record.Key+ $"/row-{i}";Description=description;Unit=(if record.PropertyCode="Nu" then "dimensionless" else "kg/m3");IsConstant=true;Points=[|{Temperature=20.;X=20.;Y=v}|];Diagnostic="PRD: verificare nome del materiale e riga originale prima della conferma."}
                    label.Clear()
                | _ -> if lines[i]<>"" && (number lines[i]).IsNone && not (Regex.IsMatch(lines[i],@"^\d{1,2} \d{3}$")) then label.Add lines[i]
            List.ofSeq output
    /// <summary>Read one XML with DTDs disabled; raw pressure charts stay explicit manual review records because implicit exponents are ambiguous.</summary>
    let readFile path =
        let bytes = File.ReadAllBytes path
        let settings = XmlReaderSettings(DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20000000L)
        use stream = new MemoryStream(bytes)
        use reader = XmlReader.Create(stream,settings)
        let document = XDocument.Load reader
        let root = document.Root
        let raw = value "RawText" root
        let source = value "sourceTable" root
        let quantity = value "targetQuantity" root
        let c = if root.Name.LocalName="ExternalPressureTableImport" then "EP" elif source.StartsWith("TE-") then "Alpha" elif source.StartsWith("TM-") then "E" elif quantity="Density" then "density" elif quantity.Contains("Poisson") then "Nu" elif quantity.Contains("Diffusivity") then "a" elif quantity.Contains("Conductivity") then "k" else code quantity
        let baseRecord = {Key=Path.GetFullPath path;File=Path.GetFullPath path;SHA256=Convert.ToHexString(SHA256.HashData bytes);Xml=document.ToString();Description=value "name" root;PropertyCode=c;Unit="";ChartName=value "figure" root;Specification="";Grade="";Condition="";UNS="";Composition="";Notes=value "Notes" root;RawText=raw;RequiresReview=true;Diagnostic="Testo XML grezzo: selezionare/ricostruire una riga numerica verificata.";IsConstant=false;Points=[||]}
        let structuredRows = root.DescendantsAndSelf() |> Seq.filter (fun n -> value "property" n<>"" && (n.Elements() |> Seq.exists (fun p -> p.Name.LocalName.Equals("Point",StringComparison.OrdinalIgnoreCase)))) |> Seq.toList
        if (["material-constants-v1"; "material-curves-v1"] |> List.contains (value "schema" root)) || not structuredRows.IsEmpty then
            let records =
                if value "schema" root="material-constants-v1" then materialConstants baseRecord root
                elif value "schema" root="material-curves-v1" then materialCurves baseRecord root
                else structuredRows |> List.mapi (fun i node -> let record=structured baseRecord node in if value "id" node="" then {record with Key=baseRecord.Key+ $"/record-{i}"} else record)
            if records |> List.countBy (fun r -> r.Key) |> List.exists (fun (_,n) -> n>1) then failwith "ID record XML duplicato."
            records
        elif raw<>"" then
            let records =
                if c="Alpha" || c="k" || c="a" then pages raw |> Array.mapi (fun i p -> columnBlocks baseRecord (i+1) p) |> Array.toList |> List.concat |> List.filter (fun r -> if c="k" then r.PropertyCode="k" elif c="a" then r.PropertyCode="a" else true)
                elif c="E" then pages raw |> Array.mapi (fun i p -> modulus baseRecord (i+1) p) |> Array.toList |> List.concat
                elif c="density" || c="Nu" then prd baseRecord raw
                else []
            if records.IsEmpty then [baseRecord] else records
        elif root.Name.LocalName.Contains("Index") || root.Name.LocalName.EndsWith("Index",StringComparison.OrdinalIgnoreCase) then []
        else [baseRecord]
    /// <summary>Scan every XML below a selected folder and return file-specific failures without hiding malformed files.</summary>
    let readFolder folder =
        let records,errors = ResizeArray<XmlPropertyRecord>(),ResizeArray<string>()
        for path in Directory.EnumerateFiles(folder,"*.xml",SearchOption.AllDirectories) |> Seq.sort do
            try records.AddRange(readFile path) with ex -> errors.Add($"{path}: {ex.Message}")
        records.ToArray(),errors.ToArray()
