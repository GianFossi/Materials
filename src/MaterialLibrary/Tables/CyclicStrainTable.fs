namespace MaterialLibrary.Domain

open System

/// Cyclic stress-strain data containing both amplitude and hysteresis tables.
type CyclicStrainTable =
    {
        /// X = stress amplitude (MPa), Y = strain amplitude (dimensionless).
        Table: PropertyTable
        /// X = stress range (MPa), Y = strain range (dimensionless).
        HysteresisRangeTable: PropertyTable
        /// Explicit closed hysteresis loops with branch-identified point lists.
        HysteresisLoops: HysteresisLoop list
        /// Reference temperature (degC).
        ReferenceTemperature: float
        /// Cyclic strength coefficient (MPa).
        Kcss: float
        /// Cyclic strain-hardening exponent.
        Ncss: float
        /// Material or grade description.
        MaterialDescription: string
        /// Human-readable dataset description.
        Description: string
    }

module CyclicStrainTable =
    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    let create
        table
        hysteresisRangeTable
        hysteresisLoops
        referenceTemperature
        kcss
        ncss
        materialDescription
        description
        =
        { Table = table
          HysteresisRangeTable = hysteresisRangeTable
          HysteresisLoops = hysteresisLoops
          ReferenceTemperature = referenceTemperature
          Kcss = kcss
          Ncss = ncss
          MaterialDescription = materialDescription
          Description = description }

    let validate (table: CyclicStrainTable) : Result<CyclicStrainTable, MaterialError> =
        if isNull (box table) then
            Error(MaterialError.InvalidOperation "Cyclic strain table cannot be null")
        elif not (isFinite table.ReferenceTemperature) then
            Error(MaterialError.InvalidOperation "Cyclic strain table temperature must be finite")
        elif not (isFinite table.Kcss) || table.Kcss <= 0.0 then
            Error(MaterialError.InvalidOperation "Kcss must be finite and > 0 MPa")
        elif not (isFinite table.Ncss) || table.Ncss <= 0.0 then
            Error(MaterialError.InvalidOperation "Ncss must be finite and > 0")
        elif String.IsNullOrWhiteSpace table.MaterialDescription then
            Error(MaterialError.InvalidOperation "Material description cannot be empty")
        elif String.IsNullOrWhiteSpace table.Description then
            Error(MaterialError.InvalidOperation "Cyclic table description cannot be empty")
        else
            PropertyTable.validate table.Table
            |> Result.bind (fun _ -> PropertyTable.validate table.HysteresisRangeTable)
            |> Result.bind (fun _ ->
                if List.isEmpty table.HysteresisLoops then
                    Error(MaterialError.InvalidOperation "At least one hysteresis loop is required")
                elif
                    table.HysteresisLoops
                    |> List.exists (fun loop ->
                        List.length loop.Points < 4
                        || loop.Points |> List.exists (fun point -> not (isFinite point.Strain && isFinite point.Stress)))
                then
                    Error(MaterialError.InvalidOperation "Hysteresis loops contain insufficient or non-finite points")
                else
                    Ok table)

    let table (t: CyclicStrainTable) = t.Table
    let hysteresisRangeTable (t: CyclicStrainTable) = t.HysteresisRangeTable
    let hysteresisLoops (t: CyclicStrainTable) = t.HysteresisLoops
    let referenceTemperature (t: CyclicStrainTable) = t.ReferenceTemperature
    let unwrap (t: CyclicStrainTable) = t.Table
