namespace MaterialLibrary.Crud

open MaterialLibrary.Domain

/// <summary>Logical groups of tables stored inside a material record.</summary>
type StoredMaterialTableKind =
    | ThermalExpansion
    | ElasticModulus
    | SpecificHeat
    | Density
    | ThermalConductivity
    | AllowableStress
    | AllowableStressDataset
    | Tensile
    | Compression
    | StressStrain
    | CyclicStrain
    | ExternalPressure
    | NortonModel
    | GarofaloModel
    | KachanovOmegaModel
    | Creep
    | AverageCreepStrainRateStress
    | MinimumCreepStrainRateStress
    | StressRupture
    | AverageCreepRuptureStress
    | MinimumCreepRuptureStress
    | LarsonMiller
    | Fatigue
    | CodeCase2964AppendixIIIConstants

/// <summary>CRUD operation executed against a material or stored table.</summary>
type CrudOperation =
    | Create
    | Read
    | Update
    | Delete

/// <summary>Result metadata for one CRUD operation.</summary>
type CrudChange =
    { Operation: CrudOperation
      MaterialId: string option
      TableKind: StoredMaterialTableKind option
      Message: string }

/// <summary>Common helpers for CRUD modules.</summary>
module CrudResult =
    let changed operation materialId tableKind message =
        { Operation = operation
          MaterialId = materialId
          TableKind = tableKind
          Message = message }
        |> Ok

    let notFound message =
        Error(MaterialError.NotFound message)

    let invalid message =
        Error(MaterialError.InvalidOperation message)

