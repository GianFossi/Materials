namespace MaterialLibrary.Domain

open System

/// <summary>
/// Creep stress-rupture reference table: X = Temperature (degC), Y = Stress (MPa) that causes
/// rupture at the given reference duration.
/// </summary>
/// <remarks>
/// Distinct from <see cref="StressRuptureTable"/>, which holds a stress-rupture curve at one
/// temperature varying over time (X=time to rupture h, Y=stress MPa). This table instead fixes the
/// duration and varies temperature, matching how ASME Section II Part D commonly tabulates SRavg/SRmin
/// (e.g. at a reference duration of 100,000 h). The minimum and average bases are stored as separate
/// lists on <c>StrengthProperties</c> (<c>MinimumCreepRuptureStress</c> / <c>AverageCreepRuptureStress</c>)
/// rather than as a field on this type, since which list a table lives in identifies its basis.
/// </remarks>
type CreepStressRuptureTable =
    {
        /// The underlying property table (X=Temperature degC, Y=Stress MPa).
        Table: PropertyTable
        /// Reference duration for the rupture criterion (hours), e.g. 100000.
        ReferenceDurationHours: float
    }

/// <summary>Factory and accessor functions for <see cref="CreepStressRuptureTable"/>.</summary>
module CreepStressRuptureTable =
    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>Creates a CreepStressRuptureTable with required metadata.</summary>
    let create (table: PropertyTable) (referenceDurationHours: float) : CreepStressRuptureTable =
        { Table = table
          ReferenceDurationHours = referenceDurationHours }

    /// <summary>Validates creep-stress-rupture-specific metadata and table values.</summary>
    let validate (creepStressRuptureTable: CreepStressRuptureTable) : Result<CreepStressRuptureTable, MaterialError> =
        if isNull (box creepStressRuptureTable) then
            Error(MaterialError.InvalidOperation "Creep stress-rupture table cannot be null")
        elif
            not (isFinite creepStressRuptureTable.ReferenceDurationHours)
            || creepStressRuptureTable.ReferenceDurationHours <= 0.0
        then
            Error(
                MaterialError.InvalidOperation "Creep stress-rupture reference duration must be finite and > 0 hours"
            )
        else
            PropertyTable.validate creepStressRuptureTable.Table
            |> Result.map (fun _ -> creepStressRuptureTable)

    /// <summary>Gets the underlying PropertyTable.</summary>
    let table (t: CreepStressRuptureTable) : PropertyTable = t.Table

    /// <summary>Gets the reference duration (hours).</summary>
    let referenceDurationHours (t: CreepStressRuptureTable) : float = t.ReferenceDurationHours

    /// <summary>Unwraps the CreepStressRuptureTable to access the underlying PropertyTable.</summary>
    let unwrap (t: CreepStressRuptureTable) : PropertyTable = t.Table
