namespace MaterialLibrary.Domain

open System
open System.IO
open MaterialLibrary
open Microsoft.Data.Sqlite

/// <summary>
/// Typical ASME material-family parameter groups used to build representative stress-strain,
/// isochronous, external-pressure, and creep datasets.
/// </summary>
type AsmeRepresentativeFamily =
    | CarbonSteel
    | LowAlloySteel
    | StainlessSteel
    | NickelAlloy

/// <summary>
/// Helper functions for constructing representative ASME-style datasets from common family groups.
/// </summary>
/// <remarks>
/// Shared parameter resolution: reads from the configured SQLite database when available,
/// falls back to the hardcoded family representative values below.
/// </remarks>
module private AsmeFamilyParameters =

    let isFinite (value: float) : bool =
        not (Double.IsNaN value || Double.IsInfinity value)

    let validateTemperature (temperature: float) : Result<float, MaterialError> =
        if not (isFinite temperature) then
            Error(MaterialError.InvalidOperation "Temperature must be finite")
        else
            Ok temperature

    let fallback (family: AsmeRepresentativeFamily) =
        match family with
        | CarbonSteel ->
            {| YieldStress = 250.0
               UltimateStress = 460.0
               ElasticModulus = 200000.0
               CreepStrengthCoefficient = 1.2e-12
               CreepStressExponent = 3.5
               CreepTimeExponent = 0.35 |}
        | LowAlloySteel ->
            {| YieldStress = 320.0
               UltimateStress = 540.0
               ElasticModulus = 205000.0
               CreepStrengthCoefficient = 8.0e-13
               CreepStressExponent = 3.2
               CreepTimeExponent = 0.32 |}
        | StainlessSteel ->
            {| YieldStress = 240.0
               UltimateStress = 520.0
               ElasticModulus = 190000.0
               CreepStrengthCoefficient = 5.0e-13
               CreepStressExponent = 3.6
               CreepTimeExponent = 0.30 |}
        | NickelAlloy ->
            {| YieldStress = 300.0
               UltimateStress = 620.0
               ElasticModulus = 190000.0
               CreepStrengthCoefficient = 2.5e-13
               CreepStressExponent = 3.4
               CreepTimeExponent = 0.28 |}

    let get (family: AsmeRepresentativeFamily) =
        let configPath =
            Path.Combine(AppContext.BaseDirectory, "MaterialLibrary.config.xml")

        let configOption =
            if File.Exists configPath then
                match Configuration.load configPath with
                | Ok loaded -> Some loaded
                | Error _ -> None
            else
                None

        let dbPath =
            match configOption with
            | Some cfg -> Configuration.getAsmeDatabasePath cfg
            | None -> Path.Combine(AppContext.BaseDirectory, "ASME_Material_DB.sqlite")

        if File.Exists dbPath then
            try
                use connection = new SqliteConnection(sprintf "Data Source=%s" dbPath)
                connection.Open()

                let familyName =
                    match family with
                    | CarbonSteel -> "CarbonSteel"
                    | LowAlloySteel -> "LowAlloySteel"
                    | StainlessSteel -> "StainlessSteel"
                    | NickelAlloy -> "NickelAlloy"

                use command = connection.CreateCommand()

                command.CommandText <-
                    "SELECT YieldStress, UltimateStress, ElasticModulus, CreepStrengthCoefficient, CreepStressExponent, CreepTimeExponent FROM MaterialFamilies WHERE FamilyName = @family"

                command.Parameters.AddWithValue("@family", familyName) |> ignore

                use reader = command.ExecuteReader()

                if reader.Read() then
                    {| YieldStress = reader.GetDouble(0)
                       UltimateStress = reader.GetDouble(1)
                       ElasticModulus = reader.GetDouble(2)
                       CreepStrengthCoefficient = reader.GetDouble(3)
                       CreepStressExponent = reader.GetDouble(4)
                       CreepTimeExponent = reader.GetDouble(5) |}
                else
                    fallback family
            with _ ->
                fallback family
        else
            fallback family

// ─────────────────────────────────────────────────────────────────────────────
// Section 1 — Stress-Strain Curve representative input data by material family
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Representative ASME stress-strain curve datasets keyed by material family.
/// </summary>
module AsmeFamilyStressStrainDatabase =

    /// <summary>
    /// Generates a representative time-independent stress-strain table for the given material family.
    /// </summary>
    /// <param name="family">ASME material family.</param>
    /// <param name="temperature">Assessment temperature (degC).</param>
    /// <returns>Representative <see cref="StressStrainTable"/> or a validation error.</returns>
    let generate (family: AsmeRepresentativeFamily) (temperature: float) : Result<StressStrainTable, MaterialError> =
        match AsmeFamilyParameters.validateTemperature temperature with
        | Error err -> Error err
        | Ok temp ->
            let p = AsmeFamilyParameters.get family

            let points: StressStrainPoint list =
                [ { Strain = 0.0; Stress = 0.0 }
                  { Strain = 0.2
                    Stress = p.YieldStress * 0.5 }
                  { Strain = 0.5
                    Stress = p.YieldStress * 0.8 }
                  { Strain = 1.0; Stress = p.YieldStress }
                  { Strain = 2.0
                    Stress = p.YieldStress * 1.1 }
                  { Strain = 4.0
                    Stress = p.UltimateStress * 0.85 }
                  { Strain = 8.0
                    Stress = p.UltimateStress } ]

            StressStrainTableBuilder.createTimeIndependent
                temp
                Engineering
                Engineering
                (sprintf "%A representative stress-strain table at %.1f degC" family temp)
                points
                (Some p.YieldStress)
                (Some p.UltimateStress)

// ─────────────────────────────────────────────────────────────────────────────
// Section 2 — Isochronous stress-strain representative input data by material family
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Representative ASME isochronous stress-strain datasets keyed by material family.
/// </summary>
module AsmeFamilyIsochronousStressStrainDatabase =

    /// <summary>
    /// Generates a representative isochronous stress-strain dataset.
    /// </summary>
    /// <param name="family">ASME material family.</param>
    /// <param name="temperature">Assessment temperature (degC).</param>
    /// <param name="timeHours">Reference exposure time (hours).</param>
    /// <returns>A duration-bearing <see cref="StressStrainTable"/> or a validation error.</returns>
    let generate
        (family: AsmeRepresentativeFamily)
        (temperature: float)
        (timeHours: float)
        : Result<StressStrainTable, MaterialError> =
        match AsmeFamilyParameters.validateTemperature temperature with
        | Error err -> Error err
        | Ok temp ->
            if not (AsmeFamilyParameters.isFinite timeHours) || timeHours <= 0.0 then
                Error(MaterialError.InvalidOperation "Isochronous duration must be > 0 hours")
            else
                let p = AsmeFamilyParameters.get family

                let points: StressStrainPoint list =
                    [ { Strain = 0.1
                        Stress = p.YieldStress * 0.55 }
                      { Strain = 0.3
                        Stress = p.YieldStress * 0.75 }
                      { Strain = 0.6
                        Stress = p.YieldStress * 0.95 }
                      { Strain = 1.2
                        Stress = p.UltimateStress * 0.70 }
                      { Strain = 2.5
                        Stress = p.UltimateStress * 0.80 } ]

                StressStrainTableBuilder.createIsochronous
                    temp
                    timeHours
                    Engineering
                    Engineering
                    (sprintf
                        "%A representative isochronous stress-strain data at %.1f degC for %.1f h"
                        family
                        temp
                        timeHours)
                    points
                    None
                    None

// ─────────────────────────────────────────────────────────────────────────────
// Section 3 — External Pressure Chart representative input data by material family
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Representative ASME external-pressure chart datasets keyed by material family.
/// </summary>
module AsmeFamilyExternalPressureDatabase =

    /// <summary>
    /// Generates a representative database-backed external-pressure material table.
    /// </summary>
    /// <param name="family">ASME material family.</param>
    /// <param name="temperature">Assessment temperature (degC).</param>
    /// <param name="referenceDurationHours">Reference duration (hours).</param>
    /// <returns>Representative <see cref="ExternalPressureTable"/> or a validation error.</returns>
    let generate
        (family: AsmeRepresentativeFamily)
        (temperature: float)
        (referenceDurationHours: float)
        : Result<ExternalPressureTable, MaterialError> =
        match AsmeFamilyParameters.validateTemperature temperature with
        | Error err -> Error err
        | Ok temp ->
            if
                not (AsmeFamilyParameters.isFinite referenceDurationHours)
                || referenceDurationHours <= 0.0
            then
                Error(MaterialError.InvalidOperation "Reference duration must be > 0 hours")
            else
                let p = AsmeFamilyParameters.get family

                let points: ExternalPressureTablePoint list =
                    [ 1.0e-5, p.YieldStress * 0.25
                      1.0e-4, p.YieldStress * 0.50
                      1.0e-3, p.YieldStress * 0.75
                      1.0e-2, p.YieldStress ]
                    |> List.map (fun (factorA, stress) ->
                        { FactorA = factorA
                          CompressiveStress = stress
                          TangentModulus = stress / factorA })

                ExternalPressureTableBuilder.createFromDatabase
                    temp
                    (Some referenceDurationHours)
                    (sprintf "%A representative external-pressure table at %.1f degC" family temp)
                    points

// ─────────────────────────────────────────────────────────────────────────────
// Section 4 — Creep Curve representative input data by material family
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Representative ASME creep curve datasets keyed by material family.
/// </summary>
module AsmeFamilyCreepDatabase =

    /// <summary>
    /// Generates a representative creep table for the given material family,
    /// temperature and applied stress using a scaled Norton-like shape.
    /// </summary>
    /// <param name="family">ASME material family.</param>
    /// <param name="temperature">Assessment temperature (degC).</param>
    /// <param name="appliedStress">Applied stress (MPa).</param>
    /// <returns>Representative <see cref="CreepTable"/> or a validation error.</returns>
    let generate
        (family: AsmeRepresentativeFamily)
        (temperature: float)
        (appliedStress: float)
        : Result<CreepTable, MaterialError> =
        match AsmeFamilyParameters.validateTemperature temperature with
        | Error err -> Error err
        | Ok temp ->
            if not (AsmeFamilyParameters.isFinite appliedStress) || appliedStress <= 0.0 then
                Error(MaterialError.InvalidOperation "Applied stress must be > 0 MPa")
            else
                let p = AsmeFamilyParameters.get family
                let scale = appliedStress / max p.YieldStress 1.0

                let points: CreepPoint list =
                    [ { Time = 1.0; Strain = 0.05 * scale }
                      { Time = 10.0; Strain = 0.12 * scale }
                      { Time = 100.0; Strain = 0.28 * scale }
                      { Time = 1000.0; Strain = 0.50 * scale }
                      { Time = 10000.0
                        Strain = 0.80 * scale } ]

                CreepTableBuilder.create
                    temp
                    appliedStress
                    (sprintf "%A representative creep table at %.1f degC, %.1f MPa" family temp appliedStress)
                    points

// ─────────────────────────────────────────────────────────────────────────────
// Section 5 — Code Case 2964 published presets
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Published Code Case 2964 external-pressure chart and Appendix III presets.
/// </summary>
module CodeCase2964Database =

    let private psiToMpa (psi: float) : float = psi * 0.006894757293168361

    let private isFinite (v: float) =
        not (System.Double.IsNaN v || System.Double.IsInfinity v)

    // ── Figure 1M / Table 1 published chart ───────────────────────────────────

    /// <summary>
    /// Published Figure 1M / Table 1 Code Case 2964 chart for 2 1/4Cr-1Mo steel (annealed)
    /// at 538 °C and 100 000 h. Factor A vs factor B (psi); Sc stored in MPa; Et = Sc / A.
    /// </summary>
    let createFigure1M_2_25Cr_1MoAnnealed_538C_100000h () : Result<ExternalPressureTable, MaterialError> =
        let pts: ExternalPressureTablePoint list =
            [ { FactorA = 1.00e-05
                CompressiveStress = psiToMpa 124.0
                TangentModulus = psiToMpa 124.0 / 1.00e-05 }
              { FactorA = 8.10e-05
                CompressiveStress = psiToMpa 1000.0
                TangentModulus = psiToMpa 1000.0 / 8.10e-05 }
              { FactorA = 1.17e-04
                CompressiveStress = psiToMpa 1450.0
                TangentModulus = psiToMpa 1450.0 / 1.17e-04 }
              { FactorA = 6.47e-04
                CompressiveStress = psiToMpa 1750.0
                TangentModulus = psiToMpa 1750.0 / 6.47e-04 }
              { FactorA = 1.72e-03
                CompressiveStress = psiToMpa 1950.0
                TangentModulus = psiToMpa 1950.0 / 1.72e-03 }
              { FactorA = 4.76e-03
                CompressiveStress = psiToMpa 2200.0
                TangentModulus = psiToMpa 2200.0 / 4.76e-03 }
              { FactorA = 1.04e-02
                CompressiveStress = psiToMpa 2400.0
                TangentModulus = psiToMpa 2400.0 / 1.04e-02 }
              { FactorA = 1.00e-01
                CompressiveStress = psiToMpa 2400.0
                TangentModulus = psiToMpa 2400.0 / 1.00e-01 } ]

        ExternalPressureTableBuilder.createCodeCase2964FromTabulatedValues
            538.0
            (Some 100000.0)
            "Code Case 2964 Figure 1M / Table 1 — 2 1/4Cr-1Mo annealed @ 538 degC, 100000 h"
            pts

    // ── Appendix III constants ────────────────────────────────────────────────

    /// <summary>Creates one Appendix III constants row after validating all numeric inputs.</summary>
    let createAppendixIIIConstants
        (temperature: float)
        (a0: float)
        (a1: float)
        (a2: float)
        (a3: float)
        (a4: float)
        (b0: float)
        (b1: float)
        (b2: float)
        (b3: float)
        (b4: float)
        (notes: string option)
        : Result<CodeCase2964AppendixIIIConstants, MaterialError> =
        if
            [ temperature; a0; a1; a2; a3; a4; b0; b1; b2; b3; b4 ]
            |> List.exists (fun v -> not (isFinite v))
        then
            Error(MaterialError.InvalidOperation "Code Case 2964 Appendix III constants must be finite")
        else
            Ok
                { Temperature = temperature
                  A0 = a0
                  A1 = a1
                  A2 = a2
                  A3 = a3
                  A4 = a4
                  B0 = b0
                  B1 = b1
                  B2 = b2
                  B3 = b3
                  B4 = b4
                  Notes = notes }

    /// <summary>Published Appendix III constants for 2 1/4Cr-1Mo annealed near 538 °C.</summary>
    let create_2_25Cr_1MoAnnealed_AppendixIII_538C () : Result<CodeCase2964AppendixIIIConstants, MaterialError> =
        createAppendixIIIConstants
            538.0
            -21.860
            51635.000
            -7330.000
            -2577.000
            0.0
            -1.850
            7205.000
            -2436.000
            0.0
            0.0
            (Some "Published Appendix III table entry for 2 1/4Cr-1Mo annealed")

    // ── Appendix III factor rules ─────────────────────────────────────────────

    /// <summary>Creates one Appendix III factor rule after validating numeric inputs.</summary>
    let createAppendixIIIFactorRule
        (materialFamily: CodeCase2964MaterialFamily)
        (temperatureLimitF: float)
        (m2Coefficient: float)
        (epsPrimeP: float)
        (notes: string option)
        : Result<CodeCase2964AppendixIIIFactorRule, MaterialError> =
        if not (isFinite temperatureLimitF) || temperatureLimitF <= 0.0 then
            Error(MaterialError.InvalidOperation "Code Case 2964 factor-rule temperature limit must be > 0 degF")
        elif not (isFinite m2Coefficient) || m2Coefficient <= 0.0 then
            Error(MaterialError.InvalidOperation "Code Case 2964 m2 coefficient must be finite and > 0")
        elif not (isFinite epsPrimeP) || epsPrimeP <= 0.0 then
            Error(MaterialError.InvalidOperation "Code Case 2964 epsPrimeP must be finite and > 0")
        else
            Ok
                { MaterialFamily = materialFamily
                  TemperatureLimitF = temperatureLimitF
                  M2Coefficient = m2Coefficient
                  EpsPrimeP = epsPrimeP
                  Notes = notes }

    /// <summary>Published Appendix III factor rule for ferrous steel.</summary>
    let createFerrousSteelFactorRule () : Result<CodeCase2964AppendixIIIFactorRule, MaterialError> =
        createAppendixIIIFactorRule
            FerrousSteel
            900.0
            0.60
            2.0e-5
            (Some "Published Appendix III Table III-2 rule: m2 = 0.60 * (1 - R)")

    /// <summary>Stainless steel / nickel-based alloy rule — not yet validated in this repository.</summary>
    let createStainlessSteelOrNickelBasedAlloyFactorRule () : Result<CodeCase2964AppendixIIIFactorRule, MaterialError> =
        Error(
            MaterialError.InvalidOperation
                "Published Code Case 2964 stainless/nickel factor-rule preset is not yet available in this repository"
        )

    /// <summary>Duplex stainless steel rule — not yet validated in this repository.</summary>
    let createDuplexStainlessSteelFactorRule () : Result<CodeCase2964AppendixIIIFactorRule, MaterialError> =
        Error(
            MaterialError.InvalidOperation
                "Published Code Case 2964 duplex factor-rule preset is not yet available in this repository"
        )

    /// <summary>
    /// Returns the published Appendix III factor rule for the requested material family.
    /// Ferrous steel is currently available; stainless/nickel and duplex return
    /// <c>InvalidOperation</c> until validated published rows are added.
    /// </summary>
    let createFactorRulePublishedByFamily
        (materialFamily: CodeCase2964MaterialFamily)
        : Result<CodeCase2964AppendixIIIFactorRule, MaterialError> =
        match materialFamily with
        | FerrousSteel -> createFerrousSteelFactorRule ()
        | StainlessSteelOrNickelBasedAlloy -> createStainlessSteelOrNickelBasedAlloyFactorRule ()
        | DuplexStainlessSteel -> createDuplexStainlessSteelFactorRule ()

// ─────────────────────────────────────────────────────────────────────────────
// Section 6 — ASME BPVC VIII.2-2025 Table 3-D.2M — Cyclic Stress-Strain Curve Parameters
// K_css in MPa, n_css dimensionless, temperature in degC.
// ─────────────────────────────────────────────────────────────────────────────
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Published ASME BPVC VIII.2-2025 Table 3-D.2M (SI units) cyclic stress-strain curve parameters.
/// Each function returns a validated <see cref="CyclicStrainModelInput"/> record
/// carrying K_css and n_css for the specified material/temperature row.
/// </summary>
module AsmeCyclicParameters =

    let private isFinite (v: float) =
        not (System.Double.IsNaN v || System.Double.IsInfinity v)

    let private makeInput kcss ncss : Result<CyclicStrainModelInput, MaterialError> =
        if kcss <= 0.0 then
            Error(MaterialError.InvalidOperation "Kcss must be > 0 MPa")
        elif ncss <= 0.0 then
            Error(MaterialError.InvalidOperation "Ncss must be > 0")
        else
            Ok
                { Kcss = kcss
                  Ncss = ncss
                  ElasticModulus = 0.0 }
    // Note: ElasticModulus must be set by the caller from temperature-dependent E tables.
    // This module stores only the K_css / n_css rows from Table 3-D.2M.

    // ── Carbon Steel ──────────────────────────────────────────────────────────

    /// <summary>Carbon Steel 20 mm base metal at 20 °C.  K_css = 757 MPa, n_css = 0.128.</summary>
    let carbonSteel20mm_BaseMetal_20C () = makeInput 757.0 0.128
    /// <summary>Carbon Steel 20 mm base metal at 200 °C.  K_css = 728 MPa, n_css = 0.134.</summary>
    let carbonSteel20mm_BaseMetal_200C () = makeInput 728.0 0.134
    /// <summary>Carbon Steel 20 mm base metal at 300 °C.  K_css = 741 MPa, n_css = 0.093.</summary>
    let carbonSteel20mm_BaseMetal_300C () = makeInput 741.0 0.093
    /// <summary>Carbon Steel 20 mm base metal at 400 °C.  K_css = 666 MPa, n_css = 0.109.</summary>
    let carbonSteel20mm_BaseMetal_400C () = makeInput 666.0 0.109

    /// <summary>Carbon Steel 20 mm weld metal at 20 °C.  K_css = 695 MPa, n_css = 0.110.</summary>
    let carbonSteel20mm_WeldMetal_20C () = makeInput 695.0 0.110
    /// <summary>Carbon Steel 20 mm weld metal at 200 °C.  K_css = 687 MPa, n_css = 0.118.</summary>
    let carbonSteel20mm_WeldMetal_200C () = makeInput 687.0 0.118
    /// <summary>Carbon Steel 20 mm weld metal at 300 °C.  K_css = 695 MPa, n_css = 0.066.</summary>
    let carbonSteel20mm_WeldMetal_300C () = makeInput 695.0 0.066
    /// <summary>Carbon Steel 20 mm weld metal at 400 °C.  K_css = 549 MPa, n_css = 0.067.</summary>
    let carbonSteel20mm_WeldMetal_400C () = makeInput 549.0 0.067

    /// <summary>Carbon Steel 50 mm base metal at 20 °C.  K_css = 693 MPa, n_css = 0.126.</summary>
    let carbonSteel50mm_BaseMetal_20C () = makeInput 693.0 0.126
    /// <summary>Carbon Steel 50 mm base metal at 200 °C.  K_css = 636 MPa, n_css = 0.113.</summary>
    let carbonSteel50mm_BaseMetal_200C () = makeInput 636.0 0.113
    /// <summary>Carbon Steel 50 mm base metal at 300 °C.  K_css = 741 MPa, n_css = 0.082.</summary>
    let carbonSteel50mm_BaseMetal_300C () = makeInput 741.0 0.082
    /// <summary>Carbon Steel 50 mm base metal at 400 °C.  K_css = 643 MPa, n_css = 0.101.</summary>
    let carbonSteel50mm_BaseMetal_400C () = makeInput 643.0 0.101

    /// <summary>Carbon Steel 100 mm base metal at 20 °C.  K_css = 765 MPa, n_css = 0.137.</summary>
    let carbonSteel100mm_BaseMetal_20C () = makeInput 765.0 0.137
    /// <summary>Carbon Steel 100 mm base metal at 200 °C.  K_css = 798 MPa, n_css = 0.156.</summary>
    let carbonSteel100mm_BaseMetal_200C () = makeInput 798.0 0.156
    /// <summary>Carbon Steel 100 mm base metal at 300 °C.  K_css = 748 MPa, n_css = 0.100.</summary>
    let carbonSteel100mm_BaseMetal_300C () = makeInput 748.0 0.100
    /// <summary>Carbon Steel 100 mm base metal at 400 °C.  K_css = 668 MPa, n_css = 0.112.</summary>
    let carbonSteel100mm_BaseMetal_400C () = makeInput 668.0 0.112

    // ── 1Cr-½Mo ───────────────────────────────────────────────────────────────

    /// <summary>1Cr-½Mo 20 mm base metal at 20 °C.  K_css = 660 MPa, n_css = 0.116.</summary>
    let oneCrHalfMo20mm_BaseMetal_20C () = makeInput 660.0 0.116
    /// <summary>1Cr-½Mo 20 mm base metal at 200 °C.  K_css = 656 MPa, n_css = 0.126.</summary>
    let oneCrHalfMo20mm_BaseMetal_200C () = makeInput 656.0 0.126
    /// <summary>1Cr-½Mo 20 mm base metal at 300 °C.  K_css = 623 MPa, n_css = 0.094.</summary>
    let oneCrHalfMo20mm_BaseMetal_300C () = makeInput 623.0 0.094
    /// <summary>1Cr-½Mo 20 mm base metal at 400 °C.  K_css = 626 MPa, n_css = 0.087.</summary>
    let oneCrHalfMo20mm_BaseMetal_400C () = makeInput 626.0 0.087

    /// <summary>1Cr-½Mo 20 mm weld metal at 20 °C.  K_css = 668 MPa, n_css = 0.088.</summary>
    let oneCrHalfMo20mm_WeldMetal_20C () = makeInput 668.0 0.088
    /// <summary>1Cr-½Mo 20 mm weld metal at 200 °C.  K_css = 708 MPa, n_css = 0.114.</summary>
    let oneCrHalfMo20mm_WeldMetal_200C () = makeInput 708.0 0.114
    /// <summary>1Cr-½Mo 20 mm weld metal at 300 °C.  K_css = 683 MPa, n_css = 0.085.</summary>
    let oneCrHalfMo20mm_WeldMetal_300C () = makeInput 683.0 0.085
    /// <summary>1Cr-½Mo 20 mm weld metal at 400 °C.  K_css = 599 MPa, n_css = 0.076.</summary>
    let oneCrHalfMo20mm_WeldMetal_400C () = makeInput 599.0 0.076

    /// <summary>1Cr-½Mo 50 mm base metal at 20 °C.  K_css = 638 MPa, n_css = 0.105.</summary>
    let oneCrHalfMo50mm_BaseMetal_20C () = makeInput 638.0 0.105
    /// <summary>1Cr-½Mo 50 mm base metal at 200 °C.  K_css = 684 MPa, n_css = 0.133.</summary>
    let oneCrHalfMo50mm_BaseMetal_200C () = makeInput 684.0 0.133
    /// <summary>1Cr-½Mo 50 mm base metal at 300 °C.  K_css = 607 MPa, n_css = 0.086.</summary>
    let oneCrHalfMo50mm_BaseMetal_300C () = makeInput 607.0 0.086
    /// <summary>1Cr-½Mo 50 mm base metal at 400 °C.  K_css = 577 MPa, n_css = 0.079.</summary>
    let oneCrHalfMo50mm_BaseMetal_400C () = makeInput 577.0 0.079

    // ── 1Cr-1Mo-¼V ────────────────────────────────────────────────────────────

    /// <summary>1Cr-1Mo-¼V at 20 °C.  K_css = 1082 MPa, n_css = 0.128.</summary>
    let oneCrOneMoQuarterV_20C () = makeInput 1082.0 0.128
    /// <summary>1Cr-1Mo-¼V at 400 °C.  K_css = 912 MPa, n_css = 0.128.</summary>
    let oneCrOneMoQuarterV_400C () = makeInput 912.0 0.128
    /// <summary>1Cr-1Mo-¼V at 500 °C.  K_css = 815 MPa, n_css = 0.143.</summary>
    let oneCrOneMoQuarterV_500C () = makeInput 815.0 0.143
    /// <summary>1Cr-1Mo-¼V at 550 °C.  K_css = 693 MPa, n_css = 0.133.</summary>
    let oneCrOneMoQuarterV_550C () = makeInput 693.0 0.133
    /// <summary>1Cr-1Mo-¼V at 600 °C.  K_css = 556 MPa, n_css = 0.153.</summary>
    let oneCrOneMoQuarterV_600C () = makeInput 556.0 0.153

    // ── 2¼Cr-1Mo ─────────────────────────────────────────────────────────────

    /// <summary>2¼Cr-1Mo at 20 °C.  K_css = 796 MPa, n_css = 0.100.</summary>
    let twoQuarterCrOneMo_20C () = makeInput 796.0 0.100
    /// <summary>2¼Cr-1Mo at 300 °C.  K_css = 741 MPa, n_css = 0.109.</summary>
    let twoQuarterCrOneMo_300C () = makeInput 741.0 0.109
    /// <summary>2¼Cr-1Mo at 400 °C.  K_css = 730 MPa, n_css = 0.096.</summary>
    let twoQuarterCrOneMo_400C () = makeInput 730.0 0.096
    /// <summary>2¼Cr-1Mo at 500 °C.  K_css = 652 MPa, n_css = 0.105.</summary>
    let twoQuarterCrOneMo_500C () = makeInput 652.0 0.105
    /// <summary>2¼Cr-1Mo at 600 °C.  K_css = 428 MPa, n_css = 0.082.</summary>
    let twoQuarterCrOneMo_600C () = makeInput 428.0 0.082

    // ── 9Cr-1Mo ──────────────────────────────────────────────────────────────

    /// <summary>9Cr-1Mo at 20 °C.  K_css = 975 MPa, n_css = 0.177.</summary>
    let nineCrOneMo_20C () = makeInput 975.0 0.117 // n_css corrected to 0.117 (table value)
    /// <summary>9Cr-1Mo at 500 °C.  K_css = 693 MPa, n_css = 0.132.</summary>
    let nineCrOneMo_500C () = makeInput 693.0 0.132
    /// <summary>9Cr-1Mo at 550 °C.  K_css = 609 MPa, n_css = 0.142.</summary>
    let nineCrOneMo_550C () = makeInput 609.0 0.142
    /// <summary>9Cr-1Mo at 600 °C.  K_css = 443 MPa, n_css = 0.121.</summary>
    let nineCrOneMo_600C () = makeInput 443.0 0.121
    /// <summary>9Cr-1Mo at 650 °C.  K_css = 343 MPa, n_css = 0.125.</summary>
    let nineCrOneMo_650C () = makeInput 343.0 0.125

    // ── Type 304 Stainless Steel ──────────────────────────────────────────────

    /// <summary>Type 304 at 20 °C.  K_css = 1227 MPa, n_css = 0.171.</summary>
    let type304_20C () = makeInput 1227.0 0.171
    /// <summary>Type 304 at 400 °C.  K_css = 590 MPa, n_css = 0.095.</summary>
    let type304_400C () = makeInput 590.0 0.095
    /// <summary>Type 304 at 500 °C.  K_css = 550 MPa, n_css = 0.085.</summary>
    let type304_500C () = makeInput 550.0 0.085
    /// <summary>Type 304 at 600 °C.  K_css = 450 MPa, n_css = 0.090.</summary>
    let type304_600C () = makeInput 450.0 0.090
    /// <summary>Type 304 at 700 °C.  K_css = 306 MPa, n_css = 0.094.</summary>
    let type304_700C () = makeInput 306.0 0.094

    /// <summary>Type 304 annealed at 20 °C.  K_css = 2275 MPa, n_css = 0.334.</summary>
    let type304Annealed_20C () = makeInput 2275.0 0.334

    // ── 800H ─────────────────────────────────────────────────────────────────

    /// <summary>800H at 20 °C.  K_css = 631 MPa, n_css = 0.070.</summary>
    let alloy800H_20C () = makeInput 631.0 0.070
    /// <summary>800H at 500 °C.  K_css = 762 MPa, n_css = 0.085.</summary>
    let alloy800H_500C () = makeInput 762.0 0.085
    /// <summary>800H at 600 °C.  K_css = 729 MPa, n_css = 0.088.</summary>
    let alloy800H_600C () = makeInput 729.0 0.088
    /// <summary>800H at 700 °C.  K_css = 553 MPa, n_css = 0.092.</summary>
    let alloy800H_700C () = makeInput 553.0 0.092
    /// <summary>800H at 800 °C.  K_css = 315 MPa, n_css = 0.080.</summary>
    let alloy800H_800C () = makeInput 315.0 0.080

    // ── Aluminum alloys ───────────────────────────────────────────────────────

    /// <summary>Aluminum Al-4.5Zn-0.6Mn at 20 °C.  K_css = 453 MPa, n_css = 0.058.</summary>
    let aluminumAl45Zn06Mn_20C () = makeInput 453.0 0.058
    /// <summary>Aluminum Al-4.5Zn-1.5Mg at 20 °C.  K_css = 511 MPa, n_css = 0.047.</summary>
    let aluminumAl45Zn15Mg_20C () = makeInput 511.0 0.047
    /// <summary>Aluminum 1100-T6 at 20 °C.  K_css = 154 MPa, n_css = 0.144.</summary>
    let aluminum1100T6_20C () = makeInput 154.0 0.144
    /// <summary>Aluminum 2014-T6 at 20 °C.  K_css = 963 MPa, n_css = 0.132.</summary>
    let aluminum2014T6_20C () = makeInput 963.0 0.132
    /// <summary>Aluminum 5086 at 20 °C.  K_css = 662 MPa, n_css = 0.139.</summary>
    let aluminum5086_20C () = makeInput 662.0 0.139
    /// <summary>Aluminum 6009-T4 at 20 °C.  K_css = 577 MPa, n_css = 0.124.</summary>
    let aluminum6009T4_20C () = makeInput 577.0 0.124
    /// <summary>Aluminum 6009-T6 at 20 °C.  K_css = 633 MPa, n_css = 0.128.</summary>
    let aluminum6009T6_20C () = makeInput 633.0 0.128

    // ── Copper ────────────────────────────────────────────────────────────────

    /// <summary>Copper at 20 °C.  K_css = 683 MPa, n_css = 0.263.</summary>
    let copper_20C () = makeInput 683.0 0.263
