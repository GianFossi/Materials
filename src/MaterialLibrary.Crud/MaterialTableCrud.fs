namespace MaterialLibrary.Crud

open MaterialLibrary.Domain

/// <summary>CRUD helpers for physical-property tables stored in <see cref="Material.PhysicalProperties"/>.</summary>
module PhysicalPropertyCrud =
    let private touch (physicalProperties: PhysicalProperties) (material: Material) =
        { material with
            PhysicalProperties = physicalProperties
            LastModified = System.DateTime.UtcNow }

    let setThermalExpansion (rows: ThermalExpansionTablePoint list) (material: Material) =
        touch { material.PhysicalProperties with ThermalExpansionTable = rows |> List.sortBy (fun row -> row.Temperature) } material

    let addOrReplaceThermalExpansion (row: ThermalExpansionTablePoint) material =
        let rows =
            material.PhysicalProperties.ThermalExpansionTable
            |> List.filter (fun existing -> existing.Temperature <> row.Temperature)
            |> fun existing -> row :: existing

        setThermalExpansion rows material

    let deleteThermalExpansion temperature material =
        material.PhysicalProperties.ThermalExpansionTable
        |> List.filter (fun row -> row.Temperature <> temperature)
        |> fun rows -> setThermalExpansion rows material

    let setElasticModulus (rows: ElasticModulusTablePoint list) (material: Material) =
        touch { material.PhysicalProperties with ElasticModulusTable = rows |> List.sortBy (fun row -> row.Temperature) } material

    let addOrReplaceElasticModulus (row: ElasticModulusTablePoint) material =
        let rows =
            material.PhysicalProperties.ElasticModulusTable
            |> List.filter (fun existing -> existing.Temperature <> row.Temperature)
            |> fun existing -> row :: existing

        setElasticModulus rows material

    let deleteElasticModulus temperature material =
        material.PhysicalProperties.ElasticModulusTable
        |> List.filter (fun row -> row.Temperature <> temperature)
        |> fun rows -> setElasticModulus rows material

    let setDensity (rows: DensityTablePoint list) (material: Material) =
        touch { material.PhysicalProperties with DensityTable = rows |> List.sortBy (fun row -> row.Temperature) } material

    let addOrReplaceDensity (row: DensityTablePoint) material =
        let rows =
            material.PhysicalProperties.DensityTable
            |> List.filter (fun existing -> existing.Temperature <> row.Temperature)
            |> fun existing -> row :: existing

        setDensity rows material

    let deleteDensity temperature material =
        material.PhysicalProperties.DensityTable
        |> List.filter (fun row -> row.Temperature <> temperature)
        |> fun rows -> setDensity rows material

    let setSpecificHeat (rows: SpecificHeatTablePoint list option) (material: Material) =
        touch { material.PhysicalProperties with SpecificHeatTable = rows |> Option.map (List.sortBy (fun row -> row.Temperature)) } material

    let setThermalConductivity (rows: (float * float) list option) (material: Material) =
        touch { material.PhysicalProperties with ThermalConductivityTable = rows |> Option.map (List.sortBy fst) } material

    /// <summary>Replaces the thermal-diffusivity table, sorted by temperature.</summary>
    /// <param name="rows">Rows as (temperature degC, diffusivity m^2/s) pairs, or None to clear.</param>
    /// <param name="material">Source material.</param>
    /// <returns>Updated material with a refreshed <c>LastModified</c> stamp.</returns>
    let setThermalDiffusivity (rows: (float * float) list option) (material: Material) =
        touch { material.PhysicalProperties with ThermalDiffusivityTable = rows |> Option.map (List.sortBy fst) } material

/// <summary>CRUD helpers for strength-property table collections stored in <see cref="Material.StrengthProperties"/>.</summary>
module StrengthPropertyCrud =
    let private touch (strengthProperties: StrengthProperties) (material: Material) =
        { material with
            StrengthProperties = strengthProperties
            LastModified = System.DateTime.UtcNow }

    let setAllowableStressDatasets rows material =
        touch { material.StrengthProperties with AllowableStressDatasets = rows } material

    /// <summary>Replaces the Sy table (yield strength vs temperature, by size range).</summary>
    /// <param name="table">New 2D PropertyTable, or None to clear.</param>
    /// <param name="material">Source material.</param>
    let setSyTable (table: PropertyTable option) (material: Material) =
        touch { material.StrengthProperties with SyTable = table } material

    /// <summary>Replaces the Su table (ultimate tensile strength vs temperature, by size range).</summary>
    /// <param name="table">New 2D PropertyTable, or None to clear.</param>
    /// <param name="material">Source material.</param>
    let setSuTable (table: PropertyTable option) (material: Material) =
        touch { material.StrengthProperties with SuTable = table } material

    let setCompressionProperties (rows: CompressionProperties list option) (material: Material) =
        touch { material.StrengthProperties with CompressionProperties = rows |> Option.map (List.sortBy (fun row -> row.Temperature)) } material

    let setStressStrainTables rows material =
        touch { material.StrengthProperties with StressStrainTables = rows } material

    let setCyclicStrainTables rows material =
        touch { material.StrengthProperties with CyclicStrainTables = rows } material

    let setExternalPressureTables rows material =
        Material.addExternalPressureTables rows material

    let addOrReplaceExternalPressureTable row material =
        Material.addOrReplaceExternalPressureTable row material

    let setNortonModels rows material =
        Material.addNortonModels rows material

    let setGarofaloModels rows material =
        Material.addGarofaloModels rows material

    let setKachanovOmegaModels rows material =
        Material.addKachanovOmegaModels rows material

    let setCreepTables rows material =
        Material.addCreepTables rows material

    let setAverageCreepStrainRateStress rows material =
        touch { material.StrengthProperties with AverageCreepStrainRateStress = rows } material

    let setMinimumCreepStrainRateStress rows material =
        touch { material.StrengthProperties with MinimumCreepStrainRateStress = rows } material

    let setStressRuptureCurves rows material =
        Material.addStressRuptureCurves rows material

    let setAverageCreepRuptureStress rows material =
        touch { material.StrengthProperties with AverageCreepRuptureStress = rows } material

    let setMinimumCreepRuptureStress rows material =
        touch { material.StrengthProperties with MinimumCreepRuptureStress = rows } material

    let setLarsonMillerCurves rows material =
        touch { material.StrengthProperties with LarsonMillerCurves = rows } material

    let setFatigueCurves rows material =
        Material.addFatigueCurves rows material

/// <summary>CRUD helpers for special ASME Code Case data stored in <see cref="Material.SpecialProperties"/>.</summary>
module SpecialPropertyCrud =
    let setCodeCase2964AppendixIIIConstants rows material =
        Material.addCodeCase2964AppendixIIIConstants rows material

    let addOrReplaceCodeCase2964AppendixIIIConstants row material =
        Material.addOrReplaceCodeCase2964AppendixIIIConstants row material

    let setCodeCase2964AppendixIIIFactorRule row material =
        Material.setCodeCase2964AppendixIIIFactorRule row material
