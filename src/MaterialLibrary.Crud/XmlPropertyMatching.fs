namespace MaterialLibrary.Crud

open System
open System.Text.RegularExpressions
open Microsoft.Data.Sqlite
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Explainable candidate associations; matching never authorizes a database update.</summary>
module XmlPropertyMatching =
    /// <summary>Canonical nominal-composition tokens; reorder equivalent elements without fuzzy alloy substitution.</summary>
    let compositionKey text =
        let normalized = XmlPropertyReader.normalize text
        let tokens = normalized.Split('-')
        if tokens.Length>1 && (tokens |> Array.forall (fun t -> Regex.IsMatch(t,@"^(?:[0-9¼½¾.]+)?[A-Z]{1,2}$"))) then tokens |> Array.sort |> String.concat "-"
        else normalized
    /// <summary>List candidates with exact identity, UNS, composition or existing chart-reference evidence.</summary>
    let propose (db: SqliteConnection) (records: XmlPropertyRecord array) =
        let materials = Asme2025Data.materials db
        let references = Asme2025Data.query db "SELECT id_main,RawValue FROM ASME_Data_Source WHERE FieldName='External_Pressure_Chart_No'" [] |> List.groupBy (fun r -> Convert.ToInt64 r[0]) |> List.map (fun (id,rows) -> id,rows |> List.map (fun r -> XmlPropertyReader.normalize (string r[1])) |> Set.ofList) |> Map.ofList
        let proposals = [|
            for record in records do
                let uns = Regex.Matches(record.UNS+" "+record.Description,@"\b[A-Z][0-9]{5}\b",RegexOptions.IgnoreCase) |> Seq.map (fun m -> m.Value.ToUpperInvariant()) |> Set.ofSeq
                for material in materials do
                    let same expected actual = expected<>"" && XmlPropertyReader.normalize expected=XmlPropertyReader.normalize actual
                    let identity = record.Specification<>"" && record.Grade<>"" && same record.Specification material.Specification && same record.Grade material.Grade
                                   && (record.Condition="" || same record.Condition material.Condition) && (record.UNS="" || same record.UNS material.UNS)
                    let chart = record.PropertyCode="EP" && record.ChartName<>"" && (Map.tryFind material.Id references |> Option.exists (Set.contains (XmlPropertyReader.normalize record.ChartName)))
                    let chemistry = record.Composition<>"" && material.NominalComposition<>"" && compositionKey record.Composition=compositionKey material.NominalComposition
                    let exactUns = material.UNS<>"" && uns.Contains(XmlPropertyReader.normalize material.UNS)
                    let score,reason = if chart then 100,"Riferimento External Pressure Chart del record ASME" elif identity then 100,"Specification + Grade e qualificatori XML coincidenti" elif exactUns then 95,"UNS esplicitamente presente nel record XML" elif chemistry then 85,"Composizione nominale esatta (ordine elementi normalizzato)" else 0,""
                    let qualifiers = (record.Specification="" || same record.Specification material.Specification) && (record.Grade="" || same record.Grade material.Grade) && (record.Condition="" || same record.Condition material.Condition) && (record.UNS="" || exactUns)
                    if score>0 && qualifiers then yield {MaterialId=material.Id;Specification=material.Specification;Grade=material.Grade;Condition=material.Condition;UNS=material.UNS;Composition=material.NominalComposition;RecordKey=record.Key;PropertyCode=record.PropertyCode;Score=score;Reason=reason;Selected=false}
        |]
        // Different XML rows with equal evidence are ambiguous, even if their numerical values happen to agree.
        let counts = proposals |> Array.groupBy (fun p -> p.MaterialId,p.PropertyCode) |> Array.map (fun (key,rows) -> key,rows.Length) |> Map.ofArray
        proposals |> Array.map (fun p -> if counts[p.MaterialId,p.PropertyCode]>1 then {p with Reason=p.Reason+"; più record candidati: scelta manuale richiesta"} else p)
