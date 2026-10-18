namespace MaterialLibrary.DatabaseUpdater

open Domain

/// <summary>Conservative projection of regulatory source fields; temperatures are in degrees Celsius.</summary>
module AsmeData =
    /// <summary>Map destination columns to exact export headers and distinguish Celsius limits from text identifiers.</summary>
    let fields = [
        "P_Number", "P-No.", false
        "Group_Number", "Group No.", false
        "External_Pressure_Chart_No", "External Pressure Chart No.", false
        "Max_Temp_Sect_I", "I—Applic. and Max. Temp. Limit (°C)", true
        "Max_Temp_Sect_III", "III—Applic. and Max. Temp. Limit (°C)", true
        "Max_Temp_Sect_VIII_1", "VIII-1—Applic. and Max. Temp. Limit (°C)", true
        "Max_Temp_Sect_VIII_2", "VIII-2—Applic. and Max. Temp. Limit (°C)", true
        "Max_Temp_Sect_XII", "XII—Applic. and Max. Temp. Limit (°C)", true
    ]

    /// <summary>Retrieve a regulatory value while tolerating equivalent typographic dashes in column headers.</summary>
    let sourceValue header (row: SourceRow) =
        row.Fields |> Map.toSeq |> Seq.tryFind (fun (name,_) -> normalize name = normalize header)
        |> Option.map snd |> Option.defaultValue ""

    /// <summary>Keep all distinct nonmissing values; qualifiers and NP remain meaningful evidence.</summary>
    let values header rows =
        rows |> List.map (sourceValue header) |> List.filter (missing >> not)
        |> List.distinctBy normalize |> List.sortBy normalize

    /// <summary>Populate a scalar only when every supplied value agrees, without discarding applicability qualifiers.</summary>
    let scalar header isTemperature rows : obj =
        let supplied = values header rows
        if isTemperature then
            let parsed = supplied |> List.choose number
            match List.distinct parsed with
            | [value] when parsed.Length = supplied.Length -> box value
            | _ -> box System.DBNull.Value
        else
            match supplied with
            | [value] -> box value
            | _ -> box System.DBNull.Value
