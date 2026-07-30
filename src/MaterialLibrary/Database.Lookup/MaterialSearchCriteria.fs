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
        Specification: TextCriterion option
        Grade: TextCriterion option
        ClassConditionTemper: TextCriterion option
        Uns: TextCriterion option
        NominalComposition: TextCriterion option
        ProductForm: TextCriterion option
        Family: AsmeMaterialFamily option
    }

module MaterialSearchCriteria =
    let empty =
        { Specification = None
          Grade = None
          ClassConditionTemper = None
          Uns = None
          NominalComposition = None
          ProductForm = None
          Family = None }

    let identity productForm specification grade classConditionTemper =
        { empty with
            ProductForm = Some(Exact productForm)
            Specification = Some(Exact specification)
            Grade = Some(Exact grade)
            ClassConditionTemper = classConditionTemper |> Option.map Exact }
