namespace MaterialLibrary.Domain.Database.Lookup

open MaterialLibrary.Domain

/// String comparison used by one material-search field.
type TextCriterion =
    | Exact of string
    | Contains of string

/// Inclusive numeric interval.
type NumericRange =
    { Minimum: float option
      Maximum: float option }

/// Optional criteria are combined with AND semantics.
type MaterialSearchCriteria =
    {
        ProductForm: TextCriterion option
        Specification: TextCriterion option
        Grade: TextCriterion option
        ClassConditionTemper: TextCriterion option
        Uns: TextCriterion option
        NominalComposition: TextCriterion option
        Family: AsmeMaterialFamily option
        MinimumYieldStrength: NumericRange option
        MinimumTensileStrength: NumericRange option
    }

module MaterialSearchCriteria =
    let empty =
        { ProductForm = None
          Specification = None
          Grade = None
          ClassConditionTemper = None
          Uns = None
          NominalComposition = None
          Family = None
          MinimumYieldStrength = None
          MinimumTensileStrength = None }

    let identity productForm specification grade classConditionTemper =
        { empty with
            ProductForm = Some(Exact productForm)
            Specification = Some(Exact specification)
            Grade = Some(Exact grade)
            ClassConditionTemper = classConditionTemper |> Option.map Exact }
