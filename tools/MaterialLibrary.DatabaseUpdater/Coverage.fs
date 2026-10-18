namespace MaterialLibrary.DatabaseUpdater

open System
open System.IO
open System.Text.Json
open Microsoft.Data.Sqlite
open Domain

/// <summary>Complete coverage checks and printed matrices for every unique MAIN record.</summary>
module Coverage =
    /// <summary>Export per-material curve counts, all mechanical absences and actual physical-value availability.</summary>
    let write (writeCsv: string -> string list -> seq<string list> -> unit) directory databasePath =
        use db = Database.connect databasePath SqliteOpenMode.ReadOnly
        let mechanical = Database.query db "SELECT * FROM VW_Main_Mechanical_Counts ORDER BY Specification,Grade,ClassConditionTemper,UNS,MaterialId"
        let physical = Database.query db "SELECT * FROM VW_Main_Physical_Availability ORDER BY Specification,Grade,ClassConditionTemper,UNS,MaterialId"
        let expected = Database.query db "SELECT count(*) FROM Main" |> List.head |> Array.head |> Convert.ToInt32
        if mechanical.Length <> expected || physical.Length <> expected then failwith "MAIN coverage matrices do not include every material"
        let physicalCodes = ["E";"Alpha";"c";"k";"a";"density";"Nu"]
        let headers = ["Specification";"Grade";"ClassConditionTemper";"UNS";"NominalComposition";"MaterialId";"MaterialKey"]
        let identity (row: obj array) = [string row[1];string row[2];string row[3];string row[4];string row[5];string row[0];string row[6]]
        let has (value: obj) = Convert.ToInt64(value) <> 0L
        let absent codes offset (row: obj array) = codes |> List.mapi (fun i code -> code,has row[offset+i]) |> List.choose (fun (code,present) -> if present then None else Some code)
        // Source contexts may overlap; a shared four-field identity belongs to both groups.
        let applications =
            Database.query db "SELECT id_main,SourceTable,RawFieldsJson FROM Material_Source"
            |> List.groupBy (fun row -> string row[0])
            |> List.map (fun (id,rows) ->
                let contexts = rows |> List.map (fun row ->
                    use doc = JsonDocument.Parse(string row[2])
                    let form = doc.RootElement.EnumerateObject() |> Seq.tryFind (fun p -> p.Name.Trim().Equals("Product Form",StringComparison.OrdinalIgnoreCase)) |> Option.map (fun p -> normalize (p.Value.GetString())) |> Option.defaultValue ""
                    let bolt = form = "BOLTING" || (["3";"4"] |> List.contains (string row[1]))
                    let other = not bolt && (form <> "" || (["1A";"1B";"5A";"5B";"2A";"2B"] |> List.contains (string row[1])))
                    bolt,other)
                let bolt = contexts |> List.exists fst
                let other = (contexts |> List.exists snd) || not bolt
                id,(bolt,other)) |> Map.ofList
        let applies code (row: obj array) =
            let bolt,other = applications[string row[0]]
            match code with "Sb1" | "Sb2" -> bolt | "S1" | "S2" -> other | _ -> true
        let missing row = absent mechanicalCodes 13 row |> List.filter (fun code -> applies code row)
        let group filename codes select =
            let rows = mechanical |> List.filter (fun row -> select applications[string row[0]])
            let values = rows |> List.map (fun row -> identity row @ (codes |> List.map (fun code -> string row[7 + (mechanicalCodes |> List.findIndex ((=) code))])))
            writeCsv (Path.Combine(directory,filename)) (headers @ codes) values
            rows.Length
        let boltingCount = group "main-bolting.csv" ["Sy";"Su";"Sb1";"Sb2"] fst
        let nonBoltingCount = group "main-nonbolting.csv" ["Sy";"Su";"S1";"S2"] snd
        let missingMechanical = mechanical |> List.map (fun row -> identity row @ [String.concat "," (missing row)])
        let mechanicalRows = mechanical |> List.map (fun row ->
            identity row @ [for i in 7..12 -> string row[i]] @ [for i in 13..18 -> if has row[i] then "YES" else "NO"] @ [String.concat "," (missing row)])
        writeCsv (Path.Combine(directory,"main-mechanical.csv")) (headers @ mechanicalCodes @ (mechanicalCodes |> List.map (fun code -> code+"HasData")) @ ["MissingData"]) mechanicalRows
        writeCsv (Path.Combine(directory,"missing-mechanical.csv")) (headers @ ["MissingData"]) (missingMechanical |> List.filter (fun row -> List.last row <> ""))
        let physicalRows = physical |> List.map (fun row -> identity row @ [for i in 7..13 -> if has row[i] then "YES" else "NO"])
        writeCsv (Path.Combine(directory,"main-physical.csv")) (headers @ physicalCodes) physicalRows
        let missingByCode = mechanicalCodes |> List.mapi (fun i code -> code,(mechanical |> List.filter (fun row -> applies code row && not (has row[13+i])) |> List.length)) |> dict
        let physicalWithData = physicalCodes |> List.mapi (fun i code -> code,(physical |> List.filter (fun row -> has row[7+i]) |> List.length)) |> dict
        {|
            MainRowsChecked=expected
            BoltingMaterials=boltingCount
            NonBoltingMaterials=nonBoltingCount
            MissingMechanicalByProperty=missingByCode
            MaterialsWithMissingMechanicalData=missingMechanical |> List.filter (fun row -> List.last row <> "") |> List.length
            PhysicalMaterialsWithData=physicalWithData
        |}
