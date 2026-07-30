namespace MaterialLibrary.Domain.Database.Lookup

open MaterialLibrary.Domain

module RequestedMaterialLibrary =
    let private requestedCriteria =
        // "SA-5116" is a known typo for "SA-516"; MaterialFiltering.normalizeSpecification
        // corrects it on both sides of the comparison so this criterion still matches the
        // correctly-spelled material in the database.
        [ MaterialSearchCriteria.identity "Plate" "SA-5116" "70" None
          MaterialSearchCriteria.identity "Plate" "SA-387" "11" (Some "2")
          MaterialSearchCriteria.identity "Smls. tube" "SA-213" "TP304" None
          MaterialSearchCriteria.identity "Smls. tube" "SA-213" "T11" None
          MaterialSearchCriteria.identity "Bolting" "SA-193" "B7" None ]

    /// Loads the five requested ASME materials and all matching allowable-stress datasets.
    /// Uses findUniqueMany so the Materials table is read once and shared across all five
    /// lookups instead of being rescanned per material.
    let loadMaterials databasePath =
        AsmeMaterialRepository.findUniqueMany databasePath requestedCriteria
        |> Result.map (fun materials ->
            let selectDatasets
                (level: MaterialAllowableStressLevel)
                (material: Material)
                : Material =
                let datasets =
                    material.StrengthProperties.AllowableStressDatasets
                    |> List.filter (fun dataset ->
                        match level, dataset.Source with
                        | StandardAllowableStress, Division1AllowableStress -> true
                        | HighAllowableStress, Division1HighAllowableStress -> true
                        | _, Division2AllowableStress
                        | _, BoltingAllowableStress -> dataset.Case = StandardStrengthAllowableStress
                        | _ -> false)

                { material with
                    Id =
                        match level with
                        | StandardAllowableStress -> $"{material.Id}-STANDARD"
                        | HighAllowableStress -> $"{material.Id}-HIGH"
                    AllowableStressLevel = level
                    StrengthProperties =
                        { material.StrengthProperties with
                            AllowableStressDatasets = datasets } }

            (materials: Material list)
            |> List.collect (fun (material: Material) ->
                if material.Specification = "SA-213" && material.Grade = "TP304" then
                    [ selectDatasets StandardAllowableStress material
                      selectDatasets HighAllowableStress material ]
                else
                    [ selectDatasets StandardAllowableStress material ]))

    /// Creates an in-memory MaterialLibrary containing the five requested database materials.
    let create databasePath =
        loadMaterials databasePath
        |> Result.bind global.MaterialLibrary.MaterialLibrary.create
