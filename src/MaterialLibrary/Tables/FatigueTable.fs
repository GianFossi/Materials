namespace MaterialLibrary.Domain

/// <summary>
/// Fatigue table: X = cycles (dimensionless), Y = stress amplitude Sa (MPa).
/// Metadata: ReferenceTemperature (degC), optional ReferenceDurationHours.
/// </summary>
type FatigueTable =
    {
        /// The underlying property table (X=cycles, Y=stress amplitude Sa MPa).
        Table: PropertyTable
        /// Reference temperature or assessment condition (degC).
        ReferenceTemperature: float
        /// Optional reference duration if time-dependent.
        ReferenceDurationHours: float option
    }

/// <summary>Factory and accessor functions for <see cref="FatigueTable"/>.</summary>
module FatigueTable =
    /// <summary>Creates a FatigueTable with required metadata.</summary>
    let create
        (table: PropertyTable)
        (referenceTemperature: float)
        (referenceDurationHours: float option)
        : FatigueTable =
        { Table = table
          ReferenceTemperature = referenceTemperature
          ReferenceDurationHours = referenceDurationHours }

    /// <summary>Gets the underlying PropertyTable.</summary>
    let table (t: FatigueTable) : PropertyTable = t.Table

    /// <summary>Gets the reference temperature (degC).</summary>
    let referenceTemperature (t: FatigueTable) : float = t.ReferenceTemperature

    /// <summary>Gets the optional reference duration in hours.</summary>
    let referenceDurationHours (t: FatigueTable) : float option = t.ReferenceDurationHours

    /// <summary>Unwraps the FatigueTable to access the underlying PropertyTable.</summary>
    let unwrap (t: FatigueTable) : PropertyTable = t.Table
