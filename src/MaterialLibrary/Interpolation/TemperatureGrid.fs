namespace MaterialLibrary.Interpolation

open System
open MaterialLibrary.Domain

// The shared temperature grid used to align tables before combining them.

/// Default temperature column presets matching ASME Section II Part D tables.
type TemperatureGrid =
    /// Allowable-stress temperature columns from Table 1A (carbon/low-alloy) and 1B (high-alloy) — °F values.
    | ASME_Table1A_1B
    /// Physical-property temperature columns from Table 5A / 5B — °C (SI) values.
    | ASME_Table5A_5B
    /// Yield-strength (Table Y-1) and tensile-strength (Table U) temperature columns — °C (SI) values.
    | SyAndSu
    /// Uniform grid from T0 to T1 (inclusive) with step deltaT; must satisfy deltaT > 0 and T1 >= T0.
    | CustomRange of T0: float * T1: float * deltaT: float
    /// Caller-supplied explicit temperature list; sorted ascending and de-duplicated on use.
    | ExplicitTemperatures of float list

module TemperatureGrid =
    /// Returns the temperature list for the given preset or custom grid, sorted ascending.
    let toList (grid: TemperatureGrid) : float list =
        match grid with
        | ASME_Table1A_1B ->
            [ 40.0
              65.0
              100.0
              125.0
              150.0
              200.0
              250.0
              300.0
              325.0
              350.0
              375.0
              400.0
              425.0
              450.0
              475.0
              500.0
              525.0
              550.0
              575.0
              600.0
              625.0
              650.0
              675.0
              700.0
              725.0
              750.0
              775.0
              800.0
              825.0
              850.0
              875.0
              900.0 ]
        | ASME_Table5A_5B ->
            [ 20.0
              50.0
              100.0
              150.0
              200.0
              250.0
              300.0
              350.0
              400.0
              450.0
              500.0
              550.0
              600.0
              650.0
              700.0 ]
        | SyAndSu ->
            [ 20.0
              50.0
              100.0
              150.0
              200.0
              250.0
              300.0
              350.0
              400.0
              450.0
              500.0
              550.0
              600.0 ]
        | CustomRange(t0: float, t1: float, dt: float) ->
            let values = [ t0; t1; dt ]

            if
                values |> List.exists (fun value -> Double.IsNaN value || Double.IsInfinity value)
                || dt <= 0.0
                || t1 < t0
            then
                []
            else
                let count = Math.Ceiling((t1 - t0) / dt)

                if count > 1_000_000.0 then
                    []
                else
                    let n = int count
                    [ for i in 0..n -> min t1 (t0 + float i * dt) ] |> List.distinctBy id
        | ExplicitTemperatures temps -> temps |> List.sort |> List.distinct


// ========== PROPERTY TABLE TYPES ==========
