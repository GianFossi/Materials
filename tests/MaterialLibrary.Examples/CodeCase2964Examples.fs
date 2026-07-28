module MaterialLibrary.Tests.Examples

open MaterialLibrary.Domain

/// <summary>
/// Example datasets used by test scenarios for Code Case 2964 workflows.
/// </summary>
module CodeCase2964Examples =
    let private psiToMpa (psi: float) : float = psi * 0.006894757293168361

    /// <summary>
    /// Creates the Figure 1M / Table 1 example chart for 2 1/4Cr-1Mo annealed at 538 degC and 100000 h.
    /// </summary>
    let createFigure1M_2_25Cr_1MoAnnealed_538C_100000h () : Result<ExternalPressureTable, MaterialError> =

        let points: ExternalPressureTablePoint list =
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
            "Code Case 2964 Figure 1M / Table 1 example - 2 1/4Cr-1Mo annealed @ 538degC, 100000 h"
            points

    /// <summary>
    /// Creates the Appendix III constants example row for 2 1/4Cr-1Mo annealed near 538 degC.
    /// </summary>
    let createAppendixIII_2_25Cr_1MoAnnealed_538C () : Result<CodeCase2964AppendixIIIConstants, MaterialError> =
        CodeCase2964Database.create_2_25Cr_1MoAnnealed_AppendixIII_538C ()
