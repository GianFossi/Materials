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
                         UNS=value "uns" node;Composition=value "composition" node;Notes=value "notes" node;IsConstant=value "constant" node="true";
                         Points=(if invalid then [||] else parsed);RequiresReview=false;
                         Diagnostic=(if scale.IsNone then "Unità non riconosciuta: scegliere famiglia/unità e verificare i valori." elif invalid then "Uno o più punti non validi: nessuna riga è stata scartata silenziosamente; correggere il record." else "XML strutturato; verificare associazione e unità.")}
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
        if not structuredRows.IsEmpty then
            let records = structuredRows |> List.mapi (fun i node -> let record=structured baseRecord node in if value "id" node="" then {record with Key=baseRecord.Key+ $"/record-{i}"} else record)
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
