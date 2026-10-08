/// Reference values the database must reproduce exactly. Every constant is traceable to the Code
/// (ASME BPVC II-D Metric): the minimum strengths and Table 1A allowable stress of each grade, and
/// Table TE-1 Group 1 (carbon and low alloy steels), whose raw text is staged in
/// src/MaterialLibrary/data/physical-properties-xml/ThermalExpansion/TE-1.xml.
module MaterialLibrary.DataValidation.References

/// Scalar strengths and the Division 1 allowable stress at 40 degC of a grade (MPa).
type StrengthReference =
    { Specification: string
      /// Empty for specifications without a grade.
      Grade: string
      Smys: float
      Smts: float
      /// Allowable stress at 40 degC, Division 1.
      Division1At40: float }

let strength: StrengthReference list =
    [ { Specification = "SA-516"; Grade = "60"; Smys = 220.0; Smts = 415.0; Division1At40 = 118.0 }
      { Specification = "SA-516"; Grade = "70"; Smys = 260.0; Smts = 485.0; Division1At40 = 138.0 }
      { Specification = "SA-106"; Grade = "B"; Smys = 240.0; Smts = 415.0; Division1At40 = 118.0 }
      { Specification = "SA-106"; Grade = "C"; Smys = 275.0; Smts = 485.0; Division1At40 = 138.0 }
      { Specification = "SA-105"; Grade = ""; Smys = 250.0; Smts = 485.0; Division1At40 = 138.0 }
      { Specification = "SA-333"; Grade = "6"; Smys = 240.0; Smts = 415.0; Division1At40 = 118.0 }
      { Specification = "SA-240"; Grade = "304"; Smys = 205.0; Smts = 515.0; Division1At40 = 138.0 }
      { Specification = "SA-240"; Grade = "304L"; Smys = 170.0; Smts = 485.0; Division1At40 = 115.0 }
      { Specification = "SA-240"; Grade = "316"; Smys = 205.0; Smts = 515.0; Division1At40 = 138.0 }
      { Specification = "SA-240"; Grade = "316L"; Smys = 170.0; Smts = 485.0; Division1At40 = 115.0 }
      { Specification = "SA-240"; Grade = "321"; Smys = 205.0; Smts = 515.0; Division1At40 = 138.0 }
      { Specification = "SA-240"; Grade = "347"; Smys = 205.0; Smts = 515.0; Division1At40 = 138.0 }
      { Specification = "SA-213"; Grade = "TP304"; Smys = 205.0; Smts = 515.0; Division1At40 = 138.0 }
      { Specification = "SA-213"; Grade = "TP316"; Smys = 205.0; Smts = 515.0; Division1At40 = 138.0 }
      { Specification = "SA-213"; Grade = "TP304L"; Smys = 170.0; Smts = 485.0; Division1At40 = 115.0 }
      { Specification = "SA-213"; Grade = "TP316L"; Smys = 170.0; Smts = 485.0; Division1At40 = 115.0 } ]

/// Carbon steels that use Table TE-1 Group 1 for thermal expansion and the carbon-steel
/// elastic modulus group (specification, grade; empty grade = every grade).
let carbonSteels: (string * string) list =
    [ "SA-516", "60"
      "SA-516", "70"
      "SA-106", "B"
      "SA-106", "C"
      "SA-105", ""
      "SA-179", "" ]

/// Table TE-1, Group 1, column A: instantaneous coefficient (1e-6/degC) by temperature (degC).
let te1Group1Instantaneous: (float * float) list =
    [ 20.0, 11.5
      50.0, 12.0
      75.0, 12.3
      100.0, 12.7
      125.0, 12.9
      150.0, 13.2
      200.0, 13.8
      250.0, 14.3
      300.0, 14.9
      400.0, 15.9
      500.0, 16.7
      600.0, 17.0 ]

/// Table TE-1, Group 1, column B: mean coefficient from 20 degC (1e-6/degC).
let te1Group1Mean: (float * float) list =
    [ 20.0, 11.5
      50.0, 11.8
      75.0, 11.9
      100.0, 12.1
      125.0, 12.3
      150.0, 12.4
      200.0, 12.7
      250.0, 13.0
      300.0, 13.3
      400.0, 13.8
      500.0, 14.4
      600.0, 14.8 ]

/// Elastic modulus of carbon steel at 25 degC (GPa), Table TM-1.
let carbonSteelElasticModulusAt25 = 202.0

/// Density (kg/m3) and Poisson ratio of carbon steel, Table PRD.
let carbonSteelDensity = 7750.0
let carbonSteelPoisson = 0.30
