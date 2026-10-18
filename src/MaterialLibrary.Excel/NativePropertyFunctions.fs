namespace MaterialLibrary.Excel

open System
open ExcelDna.Integration
open MaterialLibrary.Domain.Database.Lookup

/// <summary>Native 2025 functions preserving curve IDs, forms, dimensional boundaries and complete note text.</summary>
module NativePropertyFunctions =
    /// <summary>Read one family on an existing native database; never falls back to the archived edition.</summary>
    let readCurves (materialId: string) code =
        try
            use db = Asme2025Data.openRead (LibraryCache.databasePath ())
            match Int64.TryParse materialId with
            | true,id when Asme2025Data.isNormalized db -> Ok(Asme2025Data.curves db id code)
            | _ -> Error "Use an integer MAIN material ID from the ASME 2025 database."
        with ex -> Error ex.Message

    /// <summary>Native lookup with explicit optional selection; returns diagnostic text on ambiguity.</summary>
    let value materialId code temperature size productForm curveId : obj =
        try
            use db = Asme2025Data.openRead (LibraryCache.databasePath ())
            match Int64.TryParse(materialId: string) with
            | true,id -> Asme2025Data.lookupValue db id code temperature size productForm curveId |> function Ok v -> box v | Error e -> box ("ERROR: " + e)
            | _ -> box "ERROR: Use an integer MAIN material ID."
        with ex -> box ("ERROR: " + ex.Message)

    [<ExcelFunction(Category="MaterialLibrary.ASME2025",Description="ASME 2025 property value: Sy,Su,S1,S2,Sb1,Sb2,E,Alpha,c,k,a,density,Nu. Celsius, MPa and fixed SI units; explicit variant selection and no extrapolation.")>]
    let MatPropertyValue (materialId: string) (propertyCode: string) (temperatureC: float) (sizeMm: obj) (productForm: obj) (curveId: obj) : obj =
        let selectedId = Args.optionalNumberOption curveId
        match selectedId with
        | Some id when not (Double.IsFinite id) || id<=0. || id<>Math.Truncate id -> box "ERROR: CurveId must be a positive integer."
        | _ -> value materialId propertyCode temperatureC (Args.optionalNumberOption sizeMm) (Args.optionalTextOption productForm) (selectedId |> Option.map int64)

    [<ExcelFunction(Category="MaterialLibrary.ASME2025",Description="Every source variant and temperature point for a 2025 property, including curve ID, size bounds, product form, units and full notes.")>]
    let MatPropertyTable (materialId: string) (propertyCode: string) : obj[,] =
        match readCurves materialId propertyCode with
        | Error e -> array2D [[box ("ERROR: " + e)]]
        | Ok curves ->
            let code = Asme2025Data.canonicalCode propertyCode
            let unit = Asme2025Data.families |> Array.tryFind (fun (c,_) -> c=code) |> Option.map snd |> Option.defaultValue ""
            let optional (v: Nullable<float>) = if v.HasValue then box v.Value else box ""
            let rows = curves |> List.collect (fun curve -> (if curve.Points.Length=0 then [None] else curve.Points |> Array.map Some |> Array.toList) |> List.map (fun p ->
                [box curve.Id;box code;box curve.ProductForm;optional curve.SizeMinimum;box curve.MinimumIncluded;optional curve.SizeMaximum;box curve.MaximumIncluded;box curve.SizeAxis;(p |> Option.map (fun p -> box p.Temperature) |> Option.defaultValue (box ""));(p |> Option.map (fun p -> box p.Value) |> Option.defaultValue (box ""));box unit;box curve.SourceTable;box curve.SourceNotes;box curve.UserNotes]))
            ExcelHelpers.gridOfRows ["CurveId";"Property";"ProductForm";"SizeMin_mm";"MinIncluded";"SizeMax_mm";"MaxIncluded";"SizeAxis";"Temperature_C";"Value";"Unit";"SourceTable";"ASME_Notes";"UserNotes"] rows

    [<ExcelFunction(Category="MaterialLibrary.ASME2025",Description="List every property family and its fixed unit for the ASME 2025 editor.")>]
    let MatPropertyFamilies () = Asme2025Data.families |> Array.map (fun (c,u) -> [box c;box u]) |> Array.toList |> ExcelHelpers.gridOfRows ["Property";"Unit"]
