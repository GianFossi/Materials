namespace MaterialLibrary.Domain

open System

/// <summary>
/// Pure helpers for validating and calculating external-pressure chart datasets.
/// </summary>
module ExternalPressureTableModels =

    let isFinite (value: float) : bool =
        not (Double.IsNaN value || Double.IsInfinity value)

    let psiToMpa (psi: float) : float = psi * 0.006894757293168361

    let validateCodeCase2964Description (description: string) : Result<unit, MaterialError> =
        if String.IsNullOrWhiteSpace description then
            Error(MaterialError.InvalidOperation "Code Case 2964 chart description cannot be empty")
        else
            Ok()

    let normalizeCodeCase2964Points
        (points: ExternalPressureTablePoint list)
        : Result<ExternalPressureTablePoint list, MaterialError> =

        if List.length points < 2 then
            Error(MaterialError.InvalidOperation "Code Case 2964 chart requires at least two points")
        elif
            points
            |> List.exists (fun p ->
                not (isFinite p.FactorA)
                || not (isFinite p.CompressiveStress)
                || not (isFinite p.TangentModulus)
                || p.FactorA <= 0.0
                || p.CompressiveStress <= 0.0
                || p.TangentModulus <= 0.0)
        then
            Error(MaterialError.InvalidOperation "Code Case 2964 chart contains invalid values")
        else
            let sorted = points |> List.sortBy (fun p -> p.FactorA)

            let hasDuplicateA =
                sorted |> List.pairwise |> List.exists (fun (a, b) -> a.FactorA = b.FactorA)

            if hasDuplicateA then
                Error(MaterialError.InvalidOperation "Code Case 2964 chart contains duplicate A values")
            else
                Ok sorted

    let buildCodeCase2964Points
        (minimumCurveFactor: float)
        (points: StressStrainPoint list)
        : Result<ExternalPressureTablePoint list, MaterialError> =

        if
            not (isFinite minimumCurveFactor)
            || minimumCurveFactor <= 0.0
            || minimumCurveFactor > 1.0
        then
            Error(MaterialError.InvalidOperation "Code Case 2964 minimum-curve factor must be in (0, 1]")
        else
            let sorted = points |> List.sortBy (fun p -> p.Strain)

            if List.length sorted < 2 then
                Error(MaterialError.InvalidOperation "Code Case 2964 requires at least two stress-strain points")
            elif
                sorted
                |> List.exists (fun p ->
                    not (isFinite p.Strain)
                    || not (isFinite p.Stress)
                    || p.Strain < 0.0
                    || p.Stress <= 0.0)
            then
                Error(MaterialError.InvalidOperation "Stress-strain points contain invalid values for Code Case 2964")
            else
                let chartPoints =
                    sorted
                    |> List.pairwise
                    |> List.choose (fun (p1, p2) ->
                        let deltaStrain = p2.Strain - p1.Strain
                        let reducedStress1 = minimumCurveFactor * p1.Stress
                        let reducedStress2 = minimumCurveFactor * p2.Stress
                        let deltaStress = reducedStress2 - reducedStress1

                        if deltaStrain <= 0.0 || deltaStress <= 0.0 then
                            None
                        else
                            let tangentModulus = deltaStress / deltaStrain
                            let compressiveStress = 0.5 * (reducedStress1 + reducedStress2)
                            let factorA = compressiveStress / tangentModulus

                            if
                                isFinite tangentModulus
                                && isFinite compressiveStress
                                && isFinite factorA
                                && tangentModulus > 0.0
                                && compressiveStress > 0.0
                                && factorA > 0.0
                            then
                                Some
                                    { FactorA = factorA
                                      CompressiveStress = compressiveStress
                                      TangentModulus = tangentModulus }
                            else
                                None)
                    |> List.sortBy (fun p -> p.FactorA)

                if List.length chartPoints < 2 then
                    Error(
                        MaterialError.InvalidOperation
                            "Code Case 2964 chart generation produced fewer than two valid A-Sc points"
                    )
                else
                    Ok chartPoints

