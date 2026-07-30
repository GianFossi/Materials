namespace MaterialLibrary.Domain.Database.Lookup

open System
open MaterialLibrary.Domain

module MaterialFiltering =
    let private normalize (value: string) =
        if String.IsNullOrWhiteSpace value then
            ""
        else
            value.Trim().Replace("ASME ", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant()

    // "SA-5116" is a known typo carried by one entry in RequestedMaterialLibrary's
    // requested-material list (see MaterialSearchCriteria.identity "Plate" "SA-5116" "70" None).
    // Normalizing it here means both the criterion and any correctly-spelled "SA-516" material
    // in the database compare equal, so the historical typo does not have to be fixed everywhere.
    let private normalizeSpecification value =
        match normalize value with
        | "SA-5116" -> "SA-516"
        | normalized -> normalized

    let private matchesText (normalizer: string -> string) (criterion: TextCriterion) (actual: string) =
        let expected =
            match criterion with
            | Exact value
            | Contains value -> normalizer value

        let candidate = normalizer actual

        match criterion with
        | Exact _ -> candidate = expected
        | Contains _ -> candidate.Contains(expected, StringComparison.Ordinal)

    let private matchesOptional
        (normalizer: string -> string)
        (criterion: TextCriterion option)
        (actual: string)
        =
        criterion |> Option.forall (fun expected -> matchesText normalizer expected actual)

    let private matchesRange (range: NumericRange) value =
        range.Minimum |> Option.forall (fun minimum -> value >= minimum)
        && range.Maximum |> Option.forall (fun maximum -> value <= maximum)

    let matches (criteria: MaterialSearchCriteria) (material: Material) =
        matchesOptional normalize criteria.ProductForm material.ProductForm
        && matchesOptional normalizeSpecification criteria.Specification material.Specification
        && matchesOptional normalize criteria.Grade material.Grade
        && matchesOptional normalize criteria.ClassConditionTemper material.Class_Condition_Tempering
        && matchesOptional normalize criteria.Uns material.AlloyIdentification_UNS
        && matchesOptional normalize criteria.NominalComposition material.NominalComposition
        && (criteria.Family |> Option.forall (fun family -> material.Family = Some family))
        && criteria.MinimumYieldStrength
           |> Option.forall (fun range -> matchesRange range material.BasicProperties.SpecifiedMinimumYieldStrength)
        && criteria.MinimumTensileStrength
           |> Option.forall (fun range -> matchesRange range material.BasicProperties.SpecifiedMinimumUltimateStrength)

    let findMany (criteria: MaterialSearchCriteria) (materials: Material list) =
        materials
        |> List.filter (matches criteria)
        |> List.sortBy (fun material -> material.Id)

    let findUnique (criteria: MaterialSearchCriteria) (materials: Material list) =
        match findMany criteria materials with
        | [ material ] -> Ok material
        | [] -> Error(MaterialError.NotFound "No material matches all search criteria")
        | matches ->
            Error(
                MaterialError.InvalidOperation(
                    $"Material criteria are ambiguous; {matches.Length} rows match: "
                    + (matches |> List.map (fun material -> material.Id) |> String.concat ", ")
                )
            )
