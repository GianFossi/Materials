# Material Models and Tables

## Standard Units (Fixed A Priori)

All numeric values are handled and persisted using these fixed units:

- Temperature: degC
- Stress / Strength / Allowable stress: MPa
- Time: hours
- Density: kg/m^3
- Specific heat: J/(kg*K)
- Thermal conductivity: W/(m*K)
- Thermal expansion coefficient: 1/degC
- Strain and elongation values: percent when documented as % in table descriptions

These units are also documented directly in XML comments inside the F# source files for Material properties and tables.

Stress/strain datasets also carry explicit basis metadata:

- StressStrainTable: `ReferenceDurationHours`, `StrainBasis`, `StressBasis`, `Source`
- CreepTable: `ReferenceTemperature`, `AppliedStress`, `Source`
- FatigueCurve: `StressBasis`

Isochronous tables use `StressStrainTable.ReferenceDurationHours = Some hours`.

External-pressure tables support both regimes through `ReferenceDurationHours`:

- `None` means time-independent.
- `Some hours` means time-dependent at that reference duration.

## External Pressure Table

There is one external-pressure material-table type:

- `ExternalPressureTable` always stores `Factor A -> allowable compressive stress` in MPa.
- `ExternalPressureTable.Source = MaterialDatabase` identifies database data.
- `ExternalPressureTable.Source = CodeCase2964` identifies Code Case 2964-generated data.
- `ReferenceDurationHours = None` represents a time-independent table.
- `ReferenceDurationHours = Some hours` represents an isochronous table.
- Tables are stored only in `Material.StrengthProperties.ExternalPressureTables`.
- `CompressionProperties` contains compressive strengths only and does not embed another table.

Code Case 2964 can generate either time-dependent or time-independent external-pressure tables. The time regime is a property of the input data and resulting optional duration, not a separate table type.
- `Material.CodeCase2964AppendixIIIConstants` stores reusable Appendix III `A_i` and `B_i` input rows by temperature.
- `Material.CodeCase2964AppendixIIIFactorRule` stores the reusable Appendix III material-family rule for `m2` and `ε′p`.

Stored Code Case 2964 factor evaluation:

- EN: `R = σ_y / σ_ult` is resolved from `TensileProperties` at the assessment temperature when available.
- EN: if no tensile row exists at that temperature, the library falls back to `BasicProperties` using `SMYS / SMUTS`.

Evaluation rule for Code Case 2964 charts:

- EN: because published Code Case 2964 charts use a logarithmic A-axis, prefer log-scale interpolation for `A -> Sc` lookup.
- IT: poiche i diagrammi pubblicati del Code Case 2964 usano un asse A logaritmico, per la ricerca `A -> Sc` e preferibile l'interpolazione su scala logaritmica.

Available query paths:

- `GetCodeCase2964CompressiveStress(...)` for ordinary interpolation on A.
- `GetCodeCase2964CompressiveStressLogA(...)` for interpolation on `log10(A)`.
- `GenerateExternalPressureTableFromStoredCodeCase2964Inputs(...)` to synthesize an `A -> Sc` table.
- `GenerateExternalPressureTableFromStoredCodeCase2964InputsWithCalibrationMode(...)` to synthesize a table with explicit calibration.

Stored-input chart synthesis rule:

- EN: the generated chart is computed from stored `A_i` and `B_i` polynomial coefficient rows plus evaluated factor inputs (`R`, `m2`, `ε′p`) at the requested temperature.
- EN: generated points are sampled on a log-spaced parameter band to align with the logarithmic A-axis behavior used in published Code Case 2964 charts.
- EN: when a stored Code Case 2964 chart already exists at the same `(temperature, referenceDurationHours)`, generation applies an automatic log-domain affine calibration step to improve consistency with that reference baseline.

Additional Code Case 2964 material-family initialization presets:

- `createCodeCase2964StainlessSteelOrNickelBasedAlloyFactorRulePublished()`
- `createCodeCase2964DuplexStainlessSteelFactorRulePublished()`
- `createCodeCase2964FactorRulePublishedByFamily(...)` for a single family-based entry point.

These two published-preset helpers currently return an explicit `InvalidOperation` until validated published rows are wired into this repository.

Calibration comparison workflow:

- EN: generate a chart from stored Appendix III inputs, then compare against reference Figure/Table points using `GetCodeCase2964CompressiveStressLogA(...)` on the same `A` coordinates.
- EN: track `MAPE` and `MaxAPE` as quantitative quality metrics during calibration.
- EN: define explicit acceptance thresholds for `MAPE` and `MaxAPE` in tests so calibration quality regressions fail fast.

Typical usage rule:

- Use `ExternalPressureTableBuilder.createFromDatabase` for database-read A-vs-stress data.
- Use the Code Case 2964 builder functions to generate the same `ExternalPressureTable` type.

## Stress-Strain Curve Models — CC 2964 Appendix I vs ASME VIII.2 §3-D

> **Software warning:** API 579-1/ASME FFS-1 Annex 10B.5 is not implemented.
> Do not use this library to generate isochronous stress-strain tables by that method.
> `Api579Annex10B5.ensureImplemented()` returns an explicit error until the licensed
> equations, tables, and coefficients are available and validated.

Two separate analytical models are implemented in `Domain.StressStrainModels.fs`.
They share the same underlying mathematical framework (Ramberg-Osgood / tanh blending)
but are applied in different design contexts.

### Symbol mapping

| CC 2964 Appendix I | ASME VIII.2-2025 §3-D | Formula |
|---|---|---|
| α₁ = R = σ_ys/σ_ult | R = σ_ys/σ_uts — Eq. 3-D.11 | identical |
| α₂ = m₁ — Eq. I-15 | m₁ — Eq. 3-D.7 | identical |
| α₃ = A₁ — Eq. I-16 | A₁ — Eq. 3-D.6 | identical |
| α₄ = K — Eq. I-17 | K — Eq. 3-D.13 | identical |
| α₅ = σ_ys + K(σ_ult−σ_ys) | reference in H numerator — Eq. 3-D.10 | identical |
| α₆ = K(σ_ult−σ_ys) | K(σ_uts−σ_ys) denominator in H — Eq. 3-D.10 | identical |
| α₇ = m₂ — Eq. I-20 | m₂ — Table 3-D.1 | identical |
| α₈ = A₂ — Eq. I-21 | A₂ = σ_uts exp(m₂)/m₂^m₂ — Eq. 3-D.9 | identical |
| σ_t = (1+ε_es)σ_es — Eq. I-22 | σ_t = (1+ε_es)σ_es — §3-D nomenclature | identical |
| tanh argument (2α₅−2σ_t)/α₆ | H = 2(σ_t−reference)/(K(σ_uts−σ_ys)) — Eq. 3-D.10 | sign-equivalent |

### Differences

| Aspect | CC 2964 Appendix I | ASME VIII.2 §3-D |
|---|---|---|
| **Design context** | Time-dependent (creep regime); minimum isochronous curves (0.8× average) | Time-independent tensile stress-strain curve |
| **Strain curve** | Not given — inputs come from isochronous data directly | ε_t = σ_t/E_y + Y₁ + Y₂ given explicitly (Eqs. 3-D.1 to 3-D.4) |
| **Tangent-modulus form** | E_t = 1/(H₁ + H₂ + H₃ + H₄) — Eq. I-10 | E_t = (1/E_y + D₁ + D₂ + D₃ + D₄)⁻¹ — Eq. 3-D.17 |
| **Component count** | Three H terms (elastic + two plastic branches combined) | Four D terms (micro and macro strain branches separated) |
| **ε_p reference** | Table III-2 limits; caller sets EpsilonPrimePlastic = 0 when exceeded | Table 3-D.1 limits; same zeroing rule applies |

### F# types and entry points

| Model | Input type | Result type | Entry point |
|---|---|---|---|
| CC 2964 Appendix I (tangent modulus only) | `CodeCase2964TangentModulusInput` | `CodeCase2964TangentModulusResult` | `CodeCase2964TangentModulusModel.compute` |
| ASME VIII.2 §3-D (strain curve) | `Asme3dStressStrainInput` | `Asme3dStressStrainResult` | `Asme3dStressStrainModel.computeStrain` |
| ASME VIII.2 §3-D.5.1 (tangent modulus) | `Asme3dStressStrainInput` | `Asme3dTangentModulusResult` | `Asme3dStressStrainModel.computeTangentModulus` |

### Table 3-D.1 — m₂ and ε_p parameters by material family

| Material | Temperature limit | m₂ | ε_p |
|---|---|---|---|
| Ferritic steel (incl. carbon, low-alloy, alloy, ferritic, martensitic, iron-based age-hardening SS) | 480 °C (900 °F) | 0.60 (1.00 − R) | 2.0E-5 |
| Stainless steel and nickel-base alloys | 480 °C (900 °F) | 0.75 (1.00 − R) | 2.0E-5 |
| Duplex stainless steel | 480 °C (900 °F) | 0.70 (0.95 − R) | 2.0E-5 |
| Precipitation-hardening, nickel-based austenitic alloys | 540 °C (1,000 °F) | 1.09 (0.93 − R) | 2.0E-5 |
| Aluminum | 120 °C (250 °F) | 0.52 (0.98 − R) | 5.0E-6 |
| Copper | 65 °C (150 °F) | 0.50 (1.00 − R) | 5.0E-6 |
| Titanium and zirconium | 260 °C (500 °F) | 0.50 (0.98 − R) | 2.0E-5 |

## Unified Stress-Strain Tables

There is one stored stress-strain type:

- `ReferenceDurationHours = None`: time-independent material behavior.
- `ReferenceDurationHours = Some hours`: isochronous stress-strain behavior.
- `Source = StressStrainDatabase`: data read from the material database.
- `Source = GeneratedAsmeVIII2Annex3D`: generated using ASME VIII-2 Annex 3-D.
- API 579-1/ASME FFS-1 Annex 10B.5 remains guarded as not implemented.

Both regimes are stored in `Material.StrengthProperties.StressStrainTables`.
Use `GetStressFromStrain` for time-independent data and
`GetStressFromStrainAtDuration` for isochronous data.

## Creep Table Model Selection

`CreepTable` is the only stored time-versus-creep-strain table. Its temperature,
applied stress, `Source`, and `ApplicabilityWarning` identify the conditions and
the user-selected origin:

- `CreepDatabase`: values read from the material database; source phase coverage must be verified.
- `GeneratedNortonPowerLaw`: empirical power-time behavior; not a complete three-stage model.
- `GeneratedGarofalo`: empirical stress/power-time behavior; not a complete three-stage model.
- `GeneratedKachanovOmega`: secondary creep followed by damage-driven tertiary creep; primary creep is neglected.

The library does not automatically select a model. Callers must explicitly use
`generateWithNorton`, `generateWithGarofalo`, or `generateWithKachanovOmega`
after verifying applicability for the material, temperature, stress, and creep phase.

Garofalo table generation applies the Arrhenius term `exp(-Q/(R*T))` using
temperature in degC and activation energy in J/mol. The lower-level
`GarofaloModel.creepStrain` overload is only for an effective `A` coefficient
already calibrated at the assessment temperature.
- Repeated lookups on an unchanged `PropertyTable` reuse cached validation.

