namespace MaterialLibrary.DatabaseUpdater

open System
open System.IO
open System.Text
open System.Text.Json

/// <summary>Render the same complete, readable report for the terminal and a UTF-8 text file.</summary>
module TextReport =
    /// <summary>Format summary counts and detailed findings from the verified report artifacts.</summary>
    let render directory mode databasePath detailedReportsPath =
        use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"summary.json")))
        let summary = document.RootElement
        let text = StringBuilder()
        let line (value: string) = text.AppendLine(value) |> ignore
        let number (name: string) = summary.GetProperty(name).GetInt32()
        let counts title (property: string) =
            line title
            for item in summary.GetProperty(property).EnumerateObject() |> Seq.sortBy (fun item -> item.Name) do
                line ("  " + item.Name + ": " + string (item.Value.GetInt32()))
        line ("ASME - edizione " + summary.GetProperty("Edition").GetString())
        line ("Modalità: " + mode)
        line ("Sorgente: " + summary.GetProperty("SourcePath").GetString())
        line ("Database di confronto: " + summary.GetProperty("Baseline").GetString())
        databasePath |> Option.iter (fun path -> line ("Nuovo database: " + path))
        detailedReportsPath |> Option.iter (fun path -> line ("Report CSV/JSON: " + path))
        line ""
        line (sprintf "Materiali: %d; curve: %d; valori: %d; note: %d" (number "MaterialCount") (number "SourceRowCount") (number "StressValueCount") (number "NoteDefinitionCount"))
        line (sprintf "Dati normativi ASME: %d materiali; %d campi sorgente; %d conflitti scalari." (number "AsmeDataMaterialCount") (number "AsmeDataSourceFieldCount") (number "AsmeDataConflictCount"))
        line "S1 = 1A + 1B; S2 = 5A + 5B; Sb1 = Table 3; Sb2 = Table 4. Tensioni MPa, temperature °C, dimensioni mm."
        counts "Differenze tra materiali:" "MaterialChanges"
        counts "Differenze nelle note:" "NoteChanges"
        line (sprintf "Con S1/S2/Sb1/Sb2 ma senza Sy: %d; senza Su: %d; senza entrambe: %d." (number "MissingSyOnly") (number "MissingSuOnly") (number "MissingBoth"))
        line (sprintf "Possibili curve sotto identità diverse: %d; senza candidati: %d." (number "MissingWithStrengthCandidates") (number "MissingWithoutStrengthCandidates"))
        line (sprintf "Fasce dimensionali non interamente coperte: %d; righe vuote con revisione: %d." (number "SizeCoverageFindings") (number "BlankRevisionRows"))
        line ("Righe identità da tutte le tabelle: " + string (number "IdentitySourceRowCount"))
        let coverage = summary.GetProperty("MainCoverage")
        line "Materiali senza dati per famiglia applicabile:"
        for item in coverage.GetProperty("MissingMechanicalByProperty").EnumerateObject() do line ("  " + item.Name + ": " + string (item.Value.GetInt32()))
        line "Materiali con proprietà fisiche effettivamente associate:"
        for item in coverage.GetProperty("PhysicalMaterialsWithData").EnumerateObject() do line ("  " + item.Name + ": " + string (item.Value.GetInt32()))
        counts "Diagnostica:" "Diagnostics"
        line ""
        line "LIMITI DEL CONFRONTO"
        for item in summary.GetProperty("Limitations").EnumerateArray() do line ("- " + item.GetString())
        line ""
        line "VERIFICHE"
        for item in summary.GetProperty("Checks").EnumerateArray() do line ("- " + item.GetString())
        let matrix title filename columns =
            let rows = File.ReadAllBytes(Path.Combine(directory,filename)) |> Input.decode |> Input.parseCsv |> List.map snd
            let header = List.head rows
            let entries = List.tail rows |> List.map (fun row -> List.zip header row |> Map.ofList)
            line ""
            line (sprintf "%s (%d)" title entries.Length)
            line (String.concat " | " columns)
            for row in entries do line (columns |> List.map (fun column -> row[column]) |> String.concat " | ")
        line ""
        line "I prospetti meccanici separano BOLTING dagli altri product form; le identità condivise compaiono in entrambi. MAIN contiene materiali univoci per Specification/Grade/ClassConditionTemper/UNS; 6A-6D escluse. I conteggi sono curve/righe sorgente, non punti di temperatura."
        line "HasData richiede almeno un valore: una curva vuota non conta come dato disponibile."
        line "Una proprietà assente non implica che sia normativamente applicabile a quel materiale."
        let identityColumns = ["Specification";"Grade";"ClassConditionTemper";"UNS";"NominalComposition";"MaterialId"]
        matrix "MAIN - BOLTING: CONTEGGI Sy, Su, Sb1, Sb2" "main-bolting.csv" (identityColumns @ ["Sy";"Su";"Sb1";"Sb2"])
        matrix "MAIN - ALTRI PRODUCT FORM: CONTEGGI Sy, Su, S1, S2" "main-nonbolting.csv" (identityColumns @ ["Sy";"Su";"S1";"S2"])
        matrix "MAIN - PRESENZA E, Alpha, c, k, a, density, Nu" "main-physical.csv" (identityColumns @ ["E";"Alpha";"c";"k";"a";"density";"Nu"])
        text.ToString()
