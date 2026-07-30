namespace MaterialLibrary.Domain

open System

/// <summary>
/// Creep strain-rate reference table: X = Temperature (degC), Y = Stress (MPa) that produces the
/// given reference creep-rate criterion.
/// </summary>
/// <remarks>
/// ASME Section II Part D commonly tabulates SC as "the average stress to produce a creep rate of
/// 0.01% in 1000 hours"; <see cref="ReferenceCreepRatePercentPer1000Hours"/> stores that 0.01 value.
/// The minimum and average bases are stored as separate lists on <c>StrengthProperties</c>
/// (<c>MinimumCreepStrainRateStress</c> / <c>AverageCreepStrainRateStress</c>) rather than as a field
/// on this type, since which list a table lives in identifies its basis.
/// </remarks>
type CreepStrainRateTable =
    {
        /// The underlying property table (X=Temperature degC, Y=Stress MPa).
        Table: PropertyTable
        /// Reference creep-rate criterion (% strain per 1000 hours), e.g. 0.01.
        ReferenceCreepRatePercentPer1000Hours: float
    }

/// <summary>Factory and accessor functions for <see cref="CreepStrainRateTable"/>.</summary>
module CreepStrainRateTable =
    let private isFinite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>Creates a CreepStrainRateTable with required metadata.</summary>
    let create (table: PropertyTable) (referenceCreepRatePercentPer1000Hours: float) : CreepStrainRateTable =
        { Table = table
          ReferenceCreepRatePercentPer1000Hours = referenceCreepRatePercentPer1000Hours }

    /// <summary>Validates creep-strain-rate-specific metadata and table values.</summary>
    let validate (creepStrainRateTable: CreepStrainRateTable) : Result<CreepStrainRateTable, MaterialError> =
        if isNull (box creepStrainRateTable) then
            Error(MaterialError.InvalidOperation "Creep strain-rate table cannot be null")
        elif
            not (isFinite creepStrainRateTable.ReferenceCreepRatePercentPer1000Hours)
            || creepStrainRateTable.ReferenceCreepRatePercentPer1000Hours <= 0.0
        then
            Error(
                MaterialError.InvalidOperation "Creep strain-rate reference rate must be finite and > 0 %/1000h"
            )
        else
            PropertyTable.validate creepStrainRateTable.Table
            |> Result.map (fun _ -> creepStrainRateTable)

    /// <summary>Gets the underlying PropertyTable.</summary>
    let table (t: CreepStrainRateTable) : PropertyTable = t.Table

    /// <summary>Gets the reference creep-rate criterion (%/1000h).</summary>
    let referenceCreepRatePercentPer1000Hours (t: CreepStrainRateTable) : float =
        t.ReferenceCreepRatePercentPer1000Hours

    /// <summary>Unwraps the CreepStrainRateTable to access the underlying PropertyTable.</summary>
    let unwrap (t: CreepStrainRateTable) : PropertyTable = t.Table
