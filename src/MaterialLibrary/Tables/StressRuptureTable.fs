namespace MaterialLibrary.Domain

/// <summary>
/// Stress-Rupture table: X = time to rupture (hours), Y = stress (MPa).
/// Metadata: ReferenceTemperature (degC).
/// </summary>
type StressRuptureTable =
    {
        /// The underlying property table (X=time to rupture h, Y=stress MPa).
        Table: PropertyTable
        /// Reference temperature or assessment condition (degC).
        ReferenceTemperature: float
    }

/// <summary>Factory and accessor functions for <see cref="StressRuptureTable"/>.</summary>
module StressRuptureTable =
    /// <summary>Creates a StressRuptureTable with required metadata.</summary>
    let create (table: PropertyTable) (referenceTemperature: float) : StressRuptureTable =
        { Table = table
          ReferenceTemperature = referenceTemperature }

    /// <summary>Gets the underlying PropertyTable.</summary>
    let table (t: StressRuptureTable) : PropertyTable = t.Table

    /// <summary>Gets the reference temperature (degC).</summary>
    let referenceTemperature (t: StressRuptureTable) : float = t.ReferenceTemperature

    /// <summary>Unwraps the StressRuptureTable to access the underlying PropertyTable.</summary>
    let unwrap (t: StressRuptureTable) : PropertyTable = t.Table
