# AI History Log

Purpose: keep persistent technical memory across sessions so AI assistants do not lose project context.

## Update Rule

Add one entry for each meaningful change (architecture, API, behavior, bugfix, performance, docs impacting usage).

## Entry Template

- Date: YYYY-MM-DD
- Area: (module/file/feature)
- Change: short description of what changed
- Why: reason and expected benefit
- Impact: API/behavior/performance/testing impact
- Files: list of touched files
- Follow-up: optional next steps or open risks

## History

- Date: 2026-07-28
- Area: Project governance
- Change: Added instruction files and persistent AI history process in this repository.
- Why: Keep coding standards consistent and preserve context across sessions.
- Impact: Better onboarding, repeatability, and reduced context loss.
- Files: .claude/CLAUDE.md, .codex/INSTRUCTIONS.md, .github/copilot-instructions.md, CONTRIBUTING.md, AI_HISTORY.md
- Follow-up: Append one entry after each meaningful change.

- Date: 2026-07-28
- Area: Material domain model
- Change: Added explicit stress/strain basis metadata (Engineering/True), time-dependence metadata for stress-strain curves, compression support with external pressure charts, and updated docs for fixed units.
- Why: Clarify interpretation of curve data and support ASME-oriented compression/time-dependent material datasets.
- Impact: New required fields on curve records used by callers and tests; richer XML documentation for serialization/deserialization contexts.
- Files: src/MaterialLibrary/Domain.fs, tests/MaterialLibrary.Tests/Tests.fs, README.md, src/MaterialLibrary/README-NUGET.md, AI_HISTORY.md
- Follow-up: Add dedicated tests for True-vs-Engineering dataset handling and representative isochrone durations (10000/100000/200000 h).

- Date: 2026-07-28
- Area: Curve/Chart builder modules
- Change: Added four dedicated modules to build stress-strain, isochrone, creep, and external pressure chart datasets, including time-dependent external pressure support for creep regimes.
- Why: Provide modular, validated construction workflows per material context and reduce record-initialization errors.
- Impact: New public builder APIs and new `ExternalPressureChart` metadata fields (`TimeDependence`, `ReferenceDurationHours`); tests extended with module-level integration scenario.
- Files: src/MaterialLibrary/StressStrainCurveBuilder.fs, src/MaterialLibrary/IsochroneCurveBuilder.fs, src/MaterialLibrary/CreepCurveBuilder.fs, src/MaterialLibrary/ExternalPressureChartBuilder.fs, src/MaterialLibrary/Domain.fs, src/MaterialLibrary/MaterialLibrary.fsproj, tests/MaterialLibrary.Tests/Tests.fs, README.md, src/MaterialLibrary/README-NUGET.md, AI_HISTORY.md
- Follow-up: Add interpolation helpers for allowable external pressure vs D/t and optional temperature/time cross-interpolation.

- Date: 2026-07-28
- Area: Project structure
- Change: Moved all builder modules into `src/MaterialLibrary/builders` and updated compile/documentation paths.
- Why: Keep source tree organized with a dedicated builders section.
- Impact: No API changes; project file include paths changed.
- Files: src/MaterialLibrary/builders/StressStrainCurveBuilder.fs, src/MaterialLibrary/builders/IsochroneCurveBuilder.fs, src/MaterialLibrary/builders/CreepCurveBuilder.fs, src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, src/MaterialLibrary/MaterialLibrary.fsproj, README.md, AI_HISTORY.md
- Follow-up: Keep future builder modules under `src/MaterialLibrary/builders`.

- Date: 2026-07-28
- Area: Markdown documentation examples
- Change: Removed unit-of-measure annotations from F# example literals in Markdown docs.
- Why: Make README examples simpler for quick copy/paste and aligned with the requested docs style.
- Impact: Documentation-only change; no runtime or API behavior changes.
- Files: README.md, src/MaterialLibrary/README-NUGET.md, AI_HISTORY.md
- Follow-up: Keep future F# snippets in Markdown free of unit-of-measure literal suffixes unless explicitly required.

- Date: 2026-07-28
- Area: Configuration model
- Change: Added Fatigue interpolation section to interpolation options and default configuration creation.
- Why: Support dedicated interpolation settings for fatigue-related workflows.
- Impact: Configuration schema now includes Interpolation.Fatigue; default config initializes it to Linear with degree 3 and flat extrapolation disabled.
- Files: src/MaterialLibrary/Configuration.fs, AI_HISTORY.md
- Follow-up: If external XML configuration files are already in use, regenerate or update them to include the new Interpolation.Fatigue element.

- Date: 2026-07-28
- Area: Domain units of measure
- Change: Removed unused measure type declarations from the Domain units block (K, sec, hours, MPa, ksi, psi, bar, W, J) while keeping units still referenced by conversion code.
- Why: Reduce dead code and keep only measure types that are actually consumed in the current codebase.
- Impact: Internal build/tests unchanged; potential source-compatibility impact only for external consumers explicitly using removed measure types.
- Files: src/MaterialLibrary/Domain.fs, AI_HISTORY.md
- Follow-up: Reintroduce specific measures only if/when new typed unit APIs require them.

- Date: 2026-07-28
- Area: BasicProperties data model
- Change: Moved density and Poisson ratio from scalar fields to a table field (`DensityPoissonTable`) and kept constructor defaults by creating one entry from input values.
- Why: Support variable-size tabular entries while preserving a simple default path.
- Impact: `GetDensity` and `GetPoissonRatio` now read from the first table row and return `InvalidOperation` when the table is empty.
- Files: src/MaterialLibrary/Domain.fs, src/MaterialLibrary/Library.fs, AI_HISTORY.md
- Follow-up: Add dedicated APIs for selecting density/Poisson values by explicit criteria when multi-row datasets are introduced.

- Date: 2026-07-28
- Area: Elastic modulus model
- Change: Removed the dedicated density/Poisson table, moved Poisson ratio into each `ElasticModulusTablePoint` as an optional value, and added computed shear modulus `G` to each row.
- Why: Keep Poisson ratio tied to elastic modulus rows and provide direct access to shear modulus using `G = E / (2 * (1 + ν))` with default ν = 0.30.
- Impact: `BasicProperties` now stores scalar density again; `GetPoissonRatio` reads the first elastic-modulus row and falls back to 0.30 when unspecified/empty; elastic-modulus rows are normalized by `PhysicalPropertiesTable.create`.
- Files: src/MaterialLibrary/Domain.fs, src/MaterialLibrary/Library.fs, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: Consider adding a temperature-specific Poisson/shear lookup API for multi-temperature queries.

- Date: 2026-07-28
- Area: Thermal expansion table
- Change: Added `ReferenceTemperature` to `ThermalExpansionTablePoint` with normalization default to 20.0 °C in `PhysicalPropertiesTable.create`, and clarified docs to use ASME average thermal-expansion table (not instantaneous).
- Why: Preserve reference-temperature context for mean expansion coefficients and align data-source semantics with ASME database structure.
- Impact: Thermal expansion entries now carry explicit reference temperature metadata; existing rows can set `None` and are normalized to `Some 20.0`.
- Files: src/MaterialLibrary/Domain.fs, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: Ensure importers from `asme_material.db` map to average-expansion rows only.

- Date: 2026-07-28
- Area: Thermal expansion memory optimization
- Change: Moved thermal-expansion reference temperature from per-row storage to a single `PhysicalPropertiesTable.ThermalExpansionReferenceTemperature` field.
- Why: Avoid duplicate per-point storage and keep one authoritative reference temperature for the whole average expansion table.
- Impact: `ThermalExpansionTablePoint` no longer stores `ReferenceTemperature`; `PhysicalPropertiesTable.create` now accepts optional table-level reference temperature and defaults to 20.0 °C.
- Files: src/MaterialLibrary/Domain.fs, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: Set `ThermalExpansionReferenceTemperature` explicitly when importing ASME average tables with non-20 °C references.

- Date: 2026-07-28
- Area: Density data model and builders
- Change: Refactored density from scalar `BasicProperties.Density` to temperature-dependent `PhysicalPropertiesTable.DensityTable`; added `DensityInterpolation` and new `DensityBuilder` for estimating metal density vs temperature.
- Why: Model density as a physical property varying with temperature and provide a reusable builder workflow for estimation.
- Impact: `BasicProperties.create` now takes 4 args (no density); `PhysicalPropertiesTable.create` now requires a density table; `GetDensity` now interpolates `ρ(T)` and returns a `PropertyLookup<float>`.
- Files: src/MaterialLibrary/Domain.fs, src/MaterialLibrary/Library.fs, src/MaterialLibrary/Interpolation.fs, src/MaterialLibrary/builders/DensityBuilder.fs, src/MaterialLibrary/MaterialLibrary.fsproj, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: Add explicit import pipeline from ASME DB average thermal expansion + reference density to generated `DensityTable`.

- Date: 2026-07-28
- Area: Culture-invariant persistence policy
- Change: Added explicit culture-invariant numeric format policy statements across all core modules/builders and in README for XML/JSON persistence.
- Why: Prevent locale-dependent parsing/serialization differences when saving and loading numeric values across environments.
- Impact: Documentation/policy hardening only; no runtime/API behavior changes.
- Files: src/MaterialLibrary/Domain.fs, src/MaterialLibrary/Interpolation.fs, src/MaterialLibrary/Library.fs, src/MaterialLibrary/UnitConversions.fs, src/MaterialLibrary/Configuration.fs, src/MaterialLibrary/builders/StressStrainCurveBuilder.fs, src/MaterialLibrary/builders/IsochroneCurveBuilder.fs, src/MaterialLibrary/builders/CreepCurveBuilder.fs, src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, src/MaterialLibrary/builders/DensityBuilder.fs, README.md, AI_HISTORY.md
- Follow-up: When implementing concrete XML/JSON numeric converters, enforce `CultureInfo.InvariantCulture` in all parse/format call sites.

- Date: 2026-07-28
- Area: Domain units cleanup
- Change: Safely removed unit-of-measure declarations and related culture-rule header from Domain, and removed/deleted UnitConversions module from project compile inputs.
- Why: Align with requested simplification and avoid maintaining unused unit type declarations.
- Impact: No runtime behavior changes; build and tests pass after cleanup.
- Files: src/MaterialLibrary/Domain.fs, src/MaterialLibrary/MaterialLibrary.fsproj, src/MaterialLibrary/UnitConversions.fs, AI_HISTORY.md
- Follow-up: If typed unit APIs are needed again, reintroduce only the specific measure types required by active code paths.

- Date: 2026-07-28
- Area: Domain API documentation and helper
- Change: Clarified elongation XML documentation to specify governing direction (longitudinal/transverse), and added Material helper to compute SMYS/SMUTS ratio.
- Why: Make tensile-property direction semantics explicit and provide a direct API for the requested strength ratio.
- Impact: Non-breaking API enhancement; no behavior changes in existing flows.
- Files: src/MaterialLibrary/Domain.fs, AI_HISTORY.md
- Follow-up: If needed, expose the ratio directly in higher-level lookup APIs.

- Date: 2026-07-28
- Area: Elastic modulus memory optimization
- Change: Removed stored `ShearModulus` from `ElasticModulusTablePoint`; added static `ComputeShearModulus(E, nu)` and instance `TryGetShearModulus()` to compute G on demand when ν is known.
- Why: Avoid redundant storage and derive G from the physical formula only when required.
- Impact: Data model is leaner; no runtime regressions in current tests.
- Files: src/MaterialLibrary/Domain.fs, AI_HISTORY.md
- Follow-up: Add optional API lookup methods that return E, ν, and computed G together at query temperature.

- Date: 2026-07-28
- Area: Documentation semantics
- Change: Added a dedicated README section explicitly distinguishing `IsochroneCurve` from time-dependent `StressStrainCurve`, including overlap and recommended modeling usage.
- Why: Prevent conceptual confusion and make domain intent explicit for users and contributors.
- Impact: Documentation-only clarification; no API/runtime behavior changes.
- Files: README.md, AI_HISTORY.md
- Follow-up: Optionally add conversion helper examples in docs to map between the two curve representations.

- Date: 2026-07-28
- Area: Creep model architecture
- Change: Moved `NortonPowerLaw`, `GarofaloModel`, and `KachanovOmega` computation modules from Domain to dedicated file `CreepModels.fs` and updated project compile order.
- Why: Keep Domain focused on data schema and isolate constitutive model calculations in a dedicated computation module.
- Impact: Public behavior preserved; build/tests pass with no regressions.
- Files: src/MaterialLibrary/Domain.fs, src/MaterialLibrary/CreepModels.fs, src/MaterialLibrary/MaterialLibrary.fsproj, AI_HISTORY.md
- Follow-up: Optionally add model-to-generated-curve helper APIs that materialize predicted strain-vs-time snapshots on demand.

- Date: 2026-07-28
- Area: Material external-pressure API
- Change: Added Material-level helpers to create validated external pressure charts and attach/upsert them into compression properties by temperature.
- Why: Provide direct workflow in `Material` module for creating external pressure chart data inside a material record.
- Impact: Non-breaking API addition; existing builder-based workflows still work.
- Files: src/MaterialLibrary/Domain.fs, AI_HISTORY.md
- Follow-up: Add a short README snippet demonstrating `Material.createExternalPressureChartInMaterial` usage.

- Date: 2026-07-28
- Area: Domain file modularization
- Change: Split monolithic `Domain.fs` into focused files: basic/physical types + related helpers, curve/mechanics types, and material/error types + material operations.
- Why: Improve maintainability, readability, inspectability, and debugging by reducing file size and separating concerns.
- Impact: No behavioral/API regressions; compile order updated and all tests pass.
- Files: src/MaterialLibrary/Domain.BasicAndPhysicalTypes.fs, src/MaterialLibrary/Domain.CurveAndMechanicsTypes.fs, src/MaterialLibrary/Domain.MaterialTypes.fs, src/MaterialLibrary/CreepModels.fs, src/MaterialLibrary/MaterialLibrary.fsproj, AI_HISTORY.md
- Follow-up: Optionally add a short architecture section in README describing file responsibilities.

- Date: 2026-07-28
- Area: External pressure builder
- Change: Added dedicated Code Case 2964 external pressure chart types and builder functions that derive A-vs-Sc chart points numerically from isochrone or time-dependent stress-strain datasets.
- Why: The existing generic external-pressure chart type models D/t-vs-pressure data, while Code Case 2964 requires a distinct A-vs-Sc representation for the time-dependent region.
- Impact: Non-breaking API addition; existing external pressure chart workflows remain intact, and tests now exercise Code Case 2964 chart generation.
- Files: src/MaterialLibrary/Domain.CurveAndMechanicsTypes.fs, src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: If exact Appendix III analytical material-family equations are required, add a second builder path driven by the Code Case 2964 constants tables.

- Date: 2026-07-28
- Area: Code Case 2964 chart storage
- Change: Added dedicated Material storage for Code Case 2964 charts plus a builder path from tabulated A-vs-Sc values, and validated storage with a Figure 1M / Table 1 example inside Material.
- Why: Let real Code Case 2964 external pressure charts be stored directly in Material without overloading the generic D/t-vs-pressure chart type.
- Impact: Material schema extended with `CodeCase2964ExternalPressureCharts`; tests now verify a stored example at 538 °C and 100000 h.
- Files: src/MaterialLibrary/Domain.MaterialTypes.fs, src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: Add README documentation that explains the difference between generic external pressure charts and Code Case 2964 A-vs-Sc charts.

- Date: 2026-07-28
- Area: Code Case 2964 query API
- Change: Added interpolation and MaterialLibrary lookup APIs for stored Code Case 2964 A-vs-Sc charts, and documented the distinction from generic external pressure charts in README.
- Why: Make stored Code Case 2964 charts directly queryable and reduce modeling confusion between the two chart families.
- Impact: New query surface for A-based Sc lookups; build and tests pass cleanly with no warnings.
- Files: src/MaterialLibrary/Interpolation.fs, src/MaterialLibrary/Library.fs, README.md, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: If needed, add tangent-modulus lookup and logarithmic interpolation options for A-axis chart evaluation.

- Date: 2026-07-28
- Area: Code Case 2964 log-scale lookup
- Change: Added dedicated log10(A)-based interpolation and MaterialLibrary lookup for Code Case 2964 charts, plus a test showing the difference from ordinary linear-A interpolation on stored tabulated data.
- Why: Published Code Case 2964 charts use a logarithmic A-axis, so a dedicated log-scale evaluation path is more faithful to the source representation.
- Impact: New `GetCodeCase2964CompressiveStressLogA` query path; tests demonstrate `A=3.00E-05` gives different results for linear vs log-A interpolation.
- Files: src/MaterialLibrary/Interpolation.fs, src/MaterialLibrary/Library.fs, README.md, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: If required, add semilog/loglog interpolation strategy selection as part of a richer external-pressure query API.

- Date: 2026-07-28
- Area: Code Case 2964 published presets
- Change: Added a reusable builder preset for the published Figure 1M / Table 1 chart (`2 1/4Cr-1Mo` annealed at `538 °C`, `100000 h`) and switched tests to consume the public preset instead of inlined tabular data.
- Why: Promote a verified published example into the API surface and reduce duplicated tabular setup in tests.
- Impact: Non-breaking API addition; build and tests remain green.
- Files: src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: Add more published Figure/Table presets or a material/time keyed preset registry if additional Code Case examples are needed.

- Date: 2026-07-28
- Area: Material database preset relocation
- Change: Moved the published Code Case 2964 Appendix III constants and Figure 1M example material inputs out of the external-pressure builder and into the material-database module so the builder stays data-agnostic.
- Why: Keep material-specific published presets in the dedicated database layer rather than in the construction logic, matching the requested architecture.
- Impact: The builder surface remains the same while preset values now originate from the database-oriented module; build verification still succeeds.
- Files: src/MaterialLibrary/Domain.MaterialDatabases.fs, src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, src/MaterialLibrary/MaterialLibrary.fsproj, AI_HISTORY.md
- Follow-up: Continue moving any remaining hard-coded material preset values from other builders into the same database module.

- Date: 2026-07-28
- Area: Material identity and code-design metadata
- Change: Extended `Material` with new fields (`ProductForm`, `NominalComposition`, `Specification`, `Class_Condition_Tempering`, `AlloyIdentification_UNS`, `MaximumAllowableTemperature`, `TimeDepenedingStartTemperature`, `WeldingInfo`) and added Material-module setters to manage them.
- Why: Support richer ASME-oriented identification and design metadata directly at material level, including welding and code-temperature boundaries.
- Impact: `MaterialName` is now composed in canonical order (`Specification + Grade + Class_Condition_Tempering + AlloyIdentification_UNS`, skipping empty parts); JSON serialization/deserialization supports the new fields; tests remain green.
- Files: src/MaterialLibrary/Domain.MaterialTypes.fs, src/MaterialLibrary/MaterialSerialization.fs, src/MaterialLibrary/Library.fs, tests/MaterialLibrary.Tests/Tests.fs, README.md, AI_HISTORY.md
- Follow-up: Optionally add strict validation rules for `UNS`, `PNumber`, and `GNumber` formats if your downstream workflows require conformance checks.

- Date: 2026-07-28
- Area: Curve model separation
- Change: Extracted the pure stress-strain, isochrone, and external-pressure chart model/validation logic into dedicated domain-model modules so the builder layer only handles construction and Material attachment.
- Why: Keep the model equations and point-generation rules separate from the builder-oriented validation and persistence workflow.
- Impact: The builder modules now delegate to reusable domain-model helpers; build verification remains green.
- Files: src/MaterialLibrary/Domain.StressStrainModels.fs, src/MaterialLibrary/Domain.IsochroneModels.fs, src/MaterialLibrary/Domain.ExternalPressureChartModels.fs, src/MaterialLibrary/builders/StressStrainCurveBuilder.fs, src/MaterialLibrary/builders/IsochroneCurveBuilder.fs, src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, src/MaterialLibrary/MaterialLibrary.fsproj, AI_HISTORY.md
- Follow-up: Apply the same split to any remaining builder-side numeric/model logic outside these curve/chart families.

- Date: 2026-07-28
- Area: Code Case 2964 evaluation inputs
- Change: Added Material-level storage for Appendix III constants and factor rules, plus builder helpers, library getters, and published presets so input parameters remain available for future elaborations.
- Why: Preserve reusable Code Case 2964 inputs inside `Material` rather than only storing derived charts, enabling evaluation workflows for any material when source data exists.
- Impact: Material schema extended with stored Code Case 2964 inputs; tests now verify a material can carry both charts and evaluation parameters.
- Files: src/MaterialLibrary/Domain.CurveAndMechanicsTypes.fs, src/MaterialLibrary/Domain.MaterialTypes.fs, src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, src/MaterialLibrary/Library.fs, README.md, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: Implement the full Appendix III equation-driven chart generator using the stored constants, factor rule, and temperature-dependent material properties.

- Date: 2026-07-28
- Area: Code Case 2964 factor evaluation
- Change: Added evaluation of stored Appendix III factor rules from `Material`, resolving `R = σ_y / σ_ult` from temperature-specific tensile data when available and falling back to `SMYS / SMUTS` otherwise.
- Why: Turn stored Code Case 2964 inputs into executable material-specific factor values without requiring immediate full chart generation.
- Impact: New getter returns evaluated `m2` and `ε′p` inputs for downstream elaborations; build and tests stay green.
- Files: src/MaterialLibrary/Domain.CurveAndMechanicsTypes.fs, src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, src/MaterialLibrary/Library.fs, README.md, tests/MaterialLibrary.Tests/Tests.fs, AI_HISTORY.md
- Follow-up: Use the evaluated factors together with stored Appendix III constants to implement the full equation-driven chart generator.

- Date: 2026-07-28
- Area: Code Case 2964 equation-driven chart generation
- Change: Added generation of `A -> Sc` charts from stored Appendix III inputs (`A_i`, `B_i`, and evaluated `R`, `m2`, `ε′p`) using a log-spaced synthesis path, exposed through `MaterialLibrary`, and validated in tests.
- Why: Complete the next evolution step so stored Code Case 2964 inputs can directly produce reusable charts for future material-specific elaborations.
- Impact: New builder and library generation APIs, improved tests (including fixed factor-evaluation test flow), and updated README usage guidance.
- Files: src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, src/MaterialLibrary/Library.fs, tests/MaterialLibrary.Tests/Tests.fs, README.md, AI_HISTORY.md
- Follow-up: Add additional published material-family presets and compare generated curves against reference published points for calibration.

- Date: 2026-07-28
- Area: Result computation expression infrastructure
- Change: Replaced local ad-hoc `result` computation-expression helper in serialization modules with `ROP.ResultlBuilder()` from the `Ganfoss.ROP` package and removed the duplicated custom builder block.
- Why: Validate that NuGet ROP is still active and use a single shared third-party implementation instead of maintaining local CE duplicates.
- Impact: Serialization behavior is unchanged, dependency usage is now explicit/real, and test suite remains green after migration.
- Files: src/MaterialLibrary/PropertyTableSerialization.fs, src/MaterialLibrary/MaterialSerialization.fs, AI_HISTORY.md
- Follow-up: If desired, centralize the ROP builder instance in one small internal module to avoid repeating `let private result = ROP.ResultlBuilder()` across files.

- Date: 2026-07-28
- Area: Result computation expression centralization
- Change: Added a shared internal auto-open module exposing one `result` instance (`ROP.ResultlBuilder()`), updated project compile order, and removed per-module local builder declarations in serialization modules.
- Why: Eliminate repeated builder instantiation while keeping NuGet ROP as the single source for `result { ... }` behavior.
- Impact: No behavioral changes; cleaner architecture with one shared CE source; tests pass.
- Files: src/MaterialLibrary/Domain.ResultCE.fs, src/MaterialLibrary/MaterialLibrary.fsproj, src/MaterialLibrary/PropertyTableSerialization.fs, src/MaterialLibrary/MaterialSerialization.fs, AI_HISTORY.md
- Follow-up: Optionally reuse this shared `result` CE in other modules if future Result workflows expand.

- Date: 2026-07-28
- Area: Code Case 2964 presets and calibration metrics
- Change: Added stainless/nickel and duplex material-family initialization presets for Appendix III factor rules, and extended tests with quantitative generated-vs-reference comparison (`MAPE`, `MaxAPE`) against Figure 1M points.
- Why: Support broader family initialization workflows and make chart calibration quality measurable with explicit error metrics.
- Impact: New preset helpers in builder and richer test diagnostics for generated chart fidelity; no breaking API changes.
- Files: src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, tests/MaterialLibrary.Tests/Tests.fs, README.md, AI_HISTORY.md
- Follow-up: Replace initialization preset values with validated project-specific published values when those datasets are finalized.

- Date: 2026-07-28
- Area: Code Case 2964 generator calibration behavior
- Change: Enhanced stored-input chart generation with optional automatic log-domain affine calibration when a matching reference Code Case 2964 chart already exists at the same temperature and duration.
- Why: Reduce generated-vs-reference divergence during calibration workflows while preserving the existing API shape.
- Impact: Improved calibration metrics in reference-backed workflows; generation remains unchanged when no matching reference chart is present.
- Files: src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, README.md, AI_HISTORY.md
- Follow-up: Add configurable calibration strategies (off/affine/robust) if multiple calibration policies are needed.

- Date: 2026-07-28
- Area: Code Case 2964 calibration robustness
- Change: Refined reference-based calibration to a robust scale-only log mapping with automatic fallback to the raw generated curve whenever calibration does not improve MAPE.
- Why: Prevent calibration instability and avoid regressions in generated chart quality.
- Impact: Calibration workflow is safer and deterministic; tests remain green and reported metrics no longer degrade under unstable fits.
- Files: src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, AI_HISTORY.md
- Follow-up: Add an explicit API switch to select calibration mode per call.

- Date: 2026-07-28
- Area: Code Case 2964 calibration controls and quality gates
- Change: Added explicit calibration-mode model (`Off`, `ScaleOnlyLog`, `ScaleOnlyLogWithFallback`), exposed mode-aware generation APIs in builder/library, and added MAPE/MaxAPE acceptance thresholds in tests.
- Why: Give callers deterministic control over calibration strategy and enforce measurable chart-quality baselines during regression testing.
- Impact: New overload-based API surface for mode-aware generation; tests now fail fast when calibration metrics exceed thresholds.
- Files: src/MaterialLibrary/Domain.CurveAndMechanicsTypes.fs, src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, src/MaterialLibrary/Library.fs, tests/MaterialLibrary.Tests/Tests.fs, README.md, AI_HISTORY.md
- Follow-up: If needed, add configurable threshold values via configuration module instead of hard-coded test constants.

- Date: 2026-07-28
- Area: Code Case 2964 family preset policy
- Change: Replaced provisional stainless/nickel and duplex "example" presets with explicit published-preset functions that currently return `InvalidOperation` until validated published rows are integrated.
- Why: Avoid shipping unvalidated placeholder coefficients as if they were production-ready published presets.
- Impact: Public preset behavior is now explicit and safe; tests verify unsupported-preset status until validated data is added.
- Files: src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, tests/MaterialLibrary.Tests/Tests.fs, README.md, AI_HISTORY.md
- Follow-up: Add validated published rows for stainless/nickel and duplex families when source data is approved.

- Date: 2026-07-28
- Area: Code Case 2964 preset API ergonomics
- Change: Added a unified family-based published resolver (`createCodeCase2964FactorRulePublishedByFamily`) and test coverage for expected availability by family.
- Why: Provide one stable entry point for callers that select presets by material family instead of handling multiple family-specific functions manually.
- Impact: Non-breaking API addition; existing family-specific functions remain available.
- Files: src/MaterialLibrary/builders/ExternalPressureChartBuilder.fs, tests/MaterialLibrary.Tests/Tests.fs, README.md, AI_HISTORY.md
- Follow-up: Keep this resolver as the preferred path once additional validated family rows become available.

- Date: 2026-07-29
- Area: v2 safety, performance, persistence, and project validation
- Change: Reorganized source files by responsibility; replaced the local Result computation expression with Ganfoss.ROP; added cached PropertyTable validation, checked Norton/Garofalo APIs, adaptive Kachanov convergence metadata, immutable validated configuration, validated library construction, schema-versioned complete JSON, and a compiled examples project.
- Why: Start from a clean API without backward-compatibility constraints while reducing repeated work, side effects, malformed-state propagation, and numerical crash risks.
- Impact: Breaking v2 API update. Strict Release builds complete with zero warnings and all 16 tests pass.
- Files: src/MaterialLibrary, tests/MaterialLibrary.Tests, tests/MaterialLibrary.Examples, README.md, CONTRIBUTING.md, AI_HISTORY.md
- Follow-up: Add approved stainless/nickel and duplex Code Case 2964 published datasets when authoritative source rows are available.

- Date: 2026-07-29
- Area: Unified external-pressure material-table semantics
- Change: Replaced the separate D/t chart and Code Case 2964 chart models with one `ExternalPressureTable` storing Factor A versus allowable compressive stress. Added database/Code Case provenance, optional duration for time dependence, optional generation reduction factor, and time-independent Code Case generation.
- Why: External-pressure data is one material-table concept regardless of whether values are read from the database or generated using Code Case 2964.
- Impact: Breaking schema/API cleanup. Tables now live only in `Material.StrengthProperties.ExternalPressureTables`; compression records no longer embed charts; JSON schema is strictly version 3.
- Files: src/MaterialLibrary, tests/MaterialLibrary.Tests, tests/MaterialLibrary.Examples, README.md, src/MaterialLibrary/README-NUGET.md, AI_HISTORY.md
- Follow-up: Connect `createFromDatabase` directly to the production `.db` reader when its external-pressure query is finalized.

- Date: 2026-07-29
- Area: API 579 Annex 10B.5 implementation guard
- Change: Added an explicit runtime error and documentation warning for the not-yet-implemented API 579-1/ASME FFS-1 Annex 10B.5 isochronous stress-strain method.
- Why: Prevent callers from mistaking representative or other generated stress-strain data for a validated Annex 10B.5 implementation while the licensed PDF is unavailable.
- Impact: `Api579Annex10B5.ensureImplemented()` always returns `InvalidOperation` until the method is implemented and validated.
- Files: src/MaterialLibrary/Models/StressStrainModels.fs, tests/MaterialLibrary.Tests/Tests.fs, README.md, src/MaterialLibrary/README-NUGET.md, AI_HISTORY.md
- Follow-up: Replace the guard with the validated calculation API after the applicable PDF edition is supplied.

- Date: 2026-07-29
- Area: Unified creep-table provenance and explicit model selection
- Change: Added database/model provenance and mandatory applicability warnings to `CreepTable`, plus explicit Norton, Garofalo, and Kachanov-Omega table generators.
- Why: A creep table may come from the database or a selected model, but no available model represents every creep phase for every material and condition.
- Impact: JSON schema is now version 4. Callers select the model explicitly; generated tables retain the choice and its limitations.
- Files: src/MaterialLibrary/Domain/CreepTypes.fs, src/MaterialLibrary/Tables/CreepTable.fs, src/MaterialLibrary/builders/CreepTableBuilder.fs, src/MaterialLibrary/Serialization, tests/MaterialLibrary.Tests/Tests.fs, README.md, src/MaterialLibrary/README-NUGET.md, AI_HISTORY.md
- Follow-up: Add calibrated model-validity ranges by material, temperature, and stress when authoritative datasets become available.

- Date: 2026-07-29
- Area: Final safety, side-effect, and dead-code audit
- Change: Bounded Kachanov and temperature-grid allocations, rejected non-finite integration output, completed the Garofalo Arrhenius calculation path, added specialized creep/external-pressure validators, made configuration saves validated and replacement-based, removed no-op helpers, and deleted the unreferenced legacy ZIP archive.
- Why: Prevent excessive allocation, numerical invalid-state propagation, partial configuration writes, and ambiguous specialized-table data.
- Impact: JSON and public model behavior remain explicit; invalid inputs now return typed errors earlier. Strict regression coverage includes allocation limits, activation energy, specialized invariants, and configuration persistence.
- Files: src/MaterialLibrary, tests/MaterialLibrary.Tests/Tests.fs, README.md, AI_HISTORY.md
- Follow-up: Consolidate active `IsochroneTable` storage into duration-bearing `StressStrainTable` after the stress-strain source/provenance refactor is approved.

- Date: 2026-07-29
- Area: Isochronous stress-strain consolidation
- Change: Consolidated time-independent and isochronous data into `StressStrainTable`, using optional duration and source metadata; removed separate isochrone domain types, material collection, builder, model, interpolation, serializer, configuration, and facade APIs.
- Why: Both datasets have identical axes and differ only by reference duration and provenance, so separate storage allowed duplicated and inconsistent information.
- Impact: Breaking JSON schema version 5. Deleted `Tables/IsochroneTable.fs`, `Models/IsochroneModels.fs`, and `builders/IsochroneTableBuilder.fs`. Isochronous lookup is now `GetStressFromStrainAtDuration`.
- Files: src/MaterialLibrary, tests/MaterialLibrary.Tests/Tests.fs, README.md, src/MaterialLibrary/README-NUGET.md, AI_HISTORY.md
- Follow-up: Implement validated complete-table generation for ASME VIII-2 Annex 3-D and, when the licensed PDF is available, API 579 Annex 10B.5.
## 2026-07-29 - Removed redundant StressStrainCurve input type

- Removed `StressStrainCurve`; builders and representative databases now return validated `StressStrainTable` values directly.
- Removed the stress-strain curve-to-table converters and the unused multi-temperature curve converter.
- Changed Code Case 2964 generation and stress-strain interpolation to consume `StressStrainTable`.
- Retained `StressStrainPoint`, `StressStrainBasis`, and `StressStrainTableSource` as useful construction metadata.
- Updated tests and documentation for the table-only API.

## 2026-07-29 - Clarified external-pressure duration semantics

- Standardized `ExternalPressureTable.ReferenceDurationHours`: `None` is time-independent and `Some hours` is isochronous.
- Added `isTimeIndependent` and `isIsochronous` classification helpers.
- Added a regression test proving both regimes coexist and are selected independently at the same temperature.
## 2026-07-29 - Removed duplicate point-list conversion layers

- Removed `CreepCurve` and `CyclicCurve`; their builders now return validated table types directly.
- Removed the dead `PropertyTable.Converters.fs` layer and project entry.
- Removed discarded creep basis arguments that were accepted but never persisted.
- Expanded `CyclicStrainTable` to preserve amplitude and hysteresis tables plus Kcss/Ncss metadata.
- Renamed creep/cyclic storage to `CreepTables` and `CyclicStrainTables`.
- Bumped JSON schema to version 6 for the cyclic persistence change.
- Made external-pressure lookup deterministic for duplicate source matches and honor the requested interpolation mode.
## 2026-07-29 - Identified explicit cyclic hysteresis loops

- Corrected the former `HysteresisLoopPoint`: it represented stress/strain ranges, not loop coordinates, and is now `HysteresisRangePoint`.
- Added branch-identified `HysteresisLoopPoint` coordinates and amplitude-identified `HysteresisLoop` point lists.
- `CyclicStrainTable` now stores the range table and explicit closed loops separately.
- Corrected generated range coverage to twice the cyclic stress amplitude.
- Bumped JSON schema to version 7 and added full loop serialization.
## 2026-07-29 - Added database lookup and material allowable-stress selection

- Added `Database.Lookup` criteria, pure filtering, deterministic unique/set lookup, and read-only SQLite loading.
- Added the requested SA-516 70, SA-387 11 Class 2, SA-213 TP304/T11, and SA-193 B7 library factory.
- Added material-level `AllowableStressLevel` and explicit ASME Section I, VIII-1, and VIII-2 applicability.
- Mapped S1 to Division 1, S2 to Division 2, and S3 to bolting.
- Classified paired G5 curves as standard (lower) and high (higher) allowable stress.
- TP304 is exposed as standard and high selectable material instances.
- Added schema version 9 persistence through selected allowable-stress datasets.
- Added the ASME material `Family` classification (`CS`, `LTCS`, low-alloy chromium families, stainless families), database inference, filtering, and schema version 10 persistence.
- Restricted G5 Standard/High classification to Division 1 (`S1`) data; Section VIII-2 (`S2`) is unaffected.
- Extended ASME material families with `QT` and `LAS9.00`; classification now considers explicit quench-and-temper condition data.
- Split high Division 1 allowable stress into the explicit `Division1HighAllowableStress` (`S1H`) source while retaining compatibility with databases that currently store G5 pairs in S1.
- Preserved user-defined `Notes` and added structured ASME II-D note references for Tables 1A, 1B, 5A, 5B, Sy, Su, and S Bolting.

## 2026-07-30 - Full-repository robustness review

- Date: 2026-07-30
- Area: JSON serialization (`Serialization/PropertyTableSerialization.fs`, `Serialization/MaterialSerialization.fs`)
- Change: Added a shared `JsonOptions.value` (`PropertyNameCaseInsensitive = true`) and passed it explicitly to every `JsonSerializer.Serialize`/`Deserialize` call.
- Why: The `JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)` attributes on the JSON DTOs only take effect through a source-generated `JsonSerializerContext`, which this library never defines; every reflection-based `Serialize`/`Deserialize` call was silently case-sensitive, so differently-cased input JSON would deserialize with missing properties defaulted to zero instead of failing loudly.
- Impact: Round-trips of JSON produced by this library are unaffected (casing already matches); externally supplied JSON with different property casing is now tolerated instead of silently losing data. No public API signature changes.
- Files: src/MaterialLibrary/Serialization/PropertyTableSerialization.fs, src/MaterialLibrary/Serialization/MaterialSerialization.fs
- Follow-up: Consider a real `JsonSerializerContext` if AOT/trimming support is ever required.

- Date: 2026-07-30
- Area: `Database.Lookup/AsmeMaterialRepository.fs`, `Database.Lookup/RequestedMaterialLibrary.fs`
- Change: Added `AsmeMaterialRepository.findUniqueMany`, which opens one SQLite connection and loads the Materials table once, then resolves a batch of search criteria against the shared candidate list. `RequestedMaterialLibrary.loadMaterials` now uses it instead of calling `findUnique` once per requested material.
- Why: The previous code opened a new connection and re-read the full Materials table from disk once per requested material (5 full-table scans for 5 materials); this is unnecessary I/O for a fixed, known batch.
- Impact: Same results and error semantics as before; fewer connections and full-table reads. `findMany`/`findUnique` are unchanged for single-criterion callers.
- Files: src/MaterialLibrary/Database.Lookup/AsmeMaterialRepository.fs, src/MaterialLibrary/Database.Lookup/RequestedMaterialLibrary.fs
- Follow-up: None.

- Date: 2026-07-30
- Area: `Domain/MaterialDatabases.fs`, `Database.Lookup/MaterialFiltering.fs`, `Database.Lookup/RequestedMaterialLibrary.fs`
- Change: Corrected a stale XML doc comment (`nineCrOneMo_20C` documented n_css as 0.177 while the code used the table's correct 0.117), and added clarifying comments explaining the intentional "SA-5116" → "SA-516" typo normalization shared between the requested-material criteria and `MaterialFiltering.normalizeSpecification`.
- Why: Documentation must match the value actually returned; the typo-normalization behavior is non-obvious and otherwise reads as a bug to a new contributor.
- Impact: Documentation/comments only; no behavior change.
- Files: src/MaterialLibrary/Domain/MaterialDatabases.fs, src/MaterialLibrary/Database.Lookup/MaterialFiltering.fs, src/MaterialLibrary/Database.Lookup/RequestedMaterialLibrary.fs
- Follow-up: None.

Verification: `dotnet build src/MaterialLibrary/MaterialLibrary.fsproj` (0 warnings, 0 errors) and `dotnet test tests/MaterialLibrary.Tests` (31/31 passed) after all changes above.

## 2026-07-30 - Added Excel-DNA add-in project

- Date: 2026-07-30
- Area: New project `src/MaterialLibrary.Excel`
- Change: Added an Excel-DNA add-in (`ExcelDna.AddIn` 1.9.0, `net8.0-windows`, F#) exposing material search and property lookups as worksheet functions: `MatSearch`/`MatDescribe`/`MatOpenDatabase`/`MatOpenJsonLibrary`/`MatLibraryStatus`, physical properties (density, elastic modulus, Poisson's ratio, shear modulus, specific heat, thermal expansion, thermal conductivity, basic properties), and strength properties (tensile/compression vs temperature, allowable stress vs temperature and size, stress-strain, cyclic strain-strain, external pressure incl. Code Case 2964 generation, creep experimental curves and stored Norton/Garofalo/Kachanov-Omega models, stress-rupture, fatigue, Larson-Miller, Code Case 2964 Appendix III constants/factor rule/evaluated factors).
- Why: User request to interface the material library with Excel: search for a material, then report either the complete property table or an interpolated value as a function of temperature and size, covering everything under `StrengthProperties` plus physical properties.
- Impact: New project only; the core `MaterialLibrary` project and its public API are unchanged. All mutable state (loaded materials) is isolated in `LibraryCache` inside the new project, guarded by a lock; the rest of the add-in stays a thin, mostly-pure wrapper. Where the domain model stores a property as a plain list rather than a `PropertyTable` (elastic modulus, Poisson's ratio, thermal expansion/conductivity, tensile/compression vs temperature, Larson-Miller), interpolation is linear-only by design, to avoid a third reimplementation of the cubic-spline/Lagrange code already duplicated inside `Interpolations.fs`. Ambiguous selections (e.g. multiple allowable-stress size ranges, multiple cyclic tables at one temperature) return a clear error listing the available options instead of guessing.
- Files: src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj, src/MaterialLibrary.Excel/LibraryCache.fs, src/MaterialLibrary.Excel/ExcelHelpers.fs, src/MaterialLibrary.Excel/MaterialSearchFunctions.fs, src/MaterialLibrary.Excel/PhysicalPropertyFunctions.fs, src/MaterialLibrary.Excel/StrengthPropertyFunctions.fs, src/MaterialLibrary.Excel/AddIn.fs, README.md, AI_HISTORY.md
- Follow-up: This environment cannot open Excel, so packaging (`dotnet build` producing valid, dependency-bundled `.xll` files for both bitnesses) was verified, but actual worksheet-function registration and behavior inside Excel were not interactively tested — verify manually before relying on this add-in. Consider adding a dedicated test project once Excel-based or Excel-DNA test-harness tooling is available.

Verification: `dotnet build src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj` (0 warnings, 0 errors; produces `MaterialLibrary.Excel-AddIn-packed.xll` and `MaterialLibrary.Excel-AddIn64-packed.xll`) and `dotnet test tests/MaterialLibrary.Tests` (31/31 still passing, confirming the core library is unaffected).

## 2026-07-30 - Configuration/database helpers, MaterialLibrary persistence, and Excel interpolation/config/persistence functions

- Date: 2026-07-30
- Area: `Configuration.fs`, `Database.Lookup/AsmeMaterialRepository.fs`, `Domain/MaterialDatabases.fs`, `Library.fs` (core); `MaterialLibrary.Excel` (new files `ConfigurationFunctions.fs`, `InterpolationFunctions.fs`, `MaterialPersistenceFunctions.fs`; edits to `LibraryCache.fs`, `ExcelHelpers.fs`)
- Change:
  - Core: added `Configuration.resolveConfigPath`/`resolveAsmeDatabasePath`/`resolveEnDatabasePath` (default path resolution), `Configuration.checkFileAccessible` and `AsmeMaterialRepository.checkAccessible` (existence + actually-openable checks, the latter opening a real read-only SQLite connection), and `Configuration.setGeneralOptions`/`setInterpolationOptions`/`setCreepDefaults`/`setIoOptions`/`setInterpolationSection`/`setDatabaseFolder`/`setAsmeDatabaseFileName`/`setEnDatabaseFileName`/`updateAndSave` (immutable-update section setters plus a load-update-validate-save helper). `Domain/MaterialDatabases.fs`'s `AsmeFamilyParameters.get` now calls `Configuration.resolveAsmeDatabasePath` instead of duplicating the same config-path-then-fallback logic inline. Added `MaterialLibrary.saveToFile`/`loadFromFile`/`loadFromFileComplete` (in `Library.fs`) so a whole `MaterialLibrary` instance can be persisted/restored directly, without the caller converting to/from `Material list` via `MaterialLibrarySerialization` by hand.
  - Excel: `LibraryCache.defaultDatabasePath` now delegates to the new core resolver instead of duplicating it a third time; added `LibraryCache.addOrReplaceJsonMaterial` for merging a single loaded material into the cache. Added `ConfigurationFunctions.fs` (`MatConfigPath`, `MatDefaultAsmeDatabasePath`/`MatDefaultEnDatabasePath`, `MatCheckFileAccessible`/`MatCheckDatabaseAccessible`, `MatConfigTable`, `MatConfigSetDatabasePaths`/`MatConfigSetGeneralOptions`/`MatConfigSetInterpolationMode`/`MatConfigSetCreepDefaults`). Added `InterpolationFunctions.fs`: generic `MatInterpolate`/`MatCubicSplineInterpolate`/`MatLagrangeInterpolate` over arbitrary worksheet (x, y) ranges (reusing the core library's public `PropertyTableMath`, not a new reimplementation), `MatTemperatureGrid` (exposes `TemperatureGrid` presets), and `MatStressFromStrainMode`/`MatCreepStrainFromCurveMode`/`MatStressRuptureMode` — mode-aware (CubicSpline/Lagrange-capable) counterparts of the always-linear `MatStressFromStrain`/`MatCreepStrainFromCurve`/`MatStressRupture`, calling `StressStrainInterpolation`/`CreepInterpolation`/`StressRuptureInterpolation` directly instead of the always-linear `PropertyTable.lookup1D` path. Added `MaterialPersistenceFunctions.fs` (`MatSaveMaterial`/`MatLoadMaterial`/`MatSaveLibrary`).
- Why: User request to add helpers (in both projects) for default database-path read/write, database-file accessibility checks, configuration read/write, exposing the `Interpolations.fs` algorithms directly from Excel, and reading/writing a `MaterialLibrary`/`Material` from Excel.
- Impact: No breaking changes to existing public members; all additions. `StressRuptureInterpolation.stressFromTimeToRupture` operates on the legacy `Domain.StressRuptureCurve`/`StressRupturePoint` shape (not the `StressRuptureTable`/`PropertyTable` shape `StrengthProperties.StressRuptureCurves` actually stores), so `MatStressRuptureMode` adapts one to the other locally in the Excel project rather than changing the core type.
- Files: src/MaterialLibrary/Configuration.fs, src/MaterialLibrary/Database.Lookup/AsmeMaterialRepository.fs, src/MaterialLibrary/Domain/MaterialDatabases.fs, src/MaterialLibrary/Library.fs, src/MaterialLibrary.Excel/LibraryCache.fs, src/MaterialLibrary.Excel/ExcelHelpers.fs, src/MaterialLibrary.Excel/ConfigurationFunctions.fs, src/MaterialLibrary.Excel/InterpolationFunctions.fs, src/MaterialLibrary.Excel/MaterialPersistenceFunctions.fs, src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj, README.md, AI_HISTORY.md
- Follow-up: Same Excel-side caveat as the previous entry — packaging succeeds but interactive Excel verification of the new functions has not been done in this environment.

Verification: `dotnet build src/MaterialLibrary/MaterialLibrary.fsproj` (0 warnings, 0 errors), `dotnet test tests/MaterialLibrary.Tests` (31/31 passed), `dotnet build tests/MaterialLibrary.Examples` (0 warnings, 0 errors), `dotnet build src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj` in Debug and Release (0 warnings, 0 errors, packed `.xll` produced both times).

## 2026-07-30 - Retired the legacy StressRuptureCurve type; fixed the temperature-blind stress-rupture lookup

- Date: 2026-07-30
- Area: `Domain/StressRuptureTypes.fs`, `Interpolations.fs`, `Domain/MaterialTypes.fs`, `Library.fs` (core); `StrengthPropertyFunctions.fs`, `InterpolationFunctions.fs` (`MaterialLibrary.Excel`)
- Change:
  - Deleted `StressRuptureCurve`/`StressRupturePoint` from `Domain/StressRuptureTypes.fs`. They were a leftover point-list shape from an earlier schema iteration with no consumer anywhere except one interpolation function; the real, current storage shape for stress-rupture data (`Material.StrengthProperties.StressRuptureCurves`) has been `StressRuptureTable` (`PropertyTable`-based) all along.
  - Rewrote `StressRuptureInterpolation.stressFromTimeToRupture` to take a `StressRuptureTable` directly (same pattern as `CreepInterpolation.strainFromTime` and `StressStrainInterpolation.stressFromStrain`, both already table-based), instead of the orphaned `StressRuptureCurve`.
  - Fixed a stale XML-doc reference on `MaterialTypes.addStressRuptureCurves` that still named the deleted type.
  - Fixed `Library.fs`'s `GetStressFromStressRupture`: it previously ignored any notion of temperature and always used `List.head` of the stored curves (i.e. whichever curve happened to be first); it now takes a required `temperature` parameter and selects the matching curve, erroring clearly if none matches. **Breaking signature change**: `GetStressFromStressRupture(materialId, timeToRupture)` → `GetStressFromStressRupture(materialId, temperature, timeToRupture)`.
  - Simplified Excel's `MatStressRupture` to delegate to the now-fixed `Library.fs` member instead of duplicating temperature-selection logic; removed the `toLegacyStressRuptureCurve` adapter from `InterpolationFunctions.fs` and pointed `MatStressRuptureMode` at the rewritten `StressRuptureInterpolation.stressFromTimeToRupture` directly.
- Why: User-directed fix after we (accurately) diagnosed that `MatStressRuptureMode`'s local `StressRuptureTable -> StressRuptureCurve` adapter was working around a real type-shape mismatch in the core library rather than a problem specific to Excel. Confirmed via repo-wide grep that `StressRuptureCurve`/`StressRupturePoint` had zero consumers outside that one function before deleting them.
- Impact: No callers of `GetStressFromStressRupture` existed anywhere in the repo (tests, examples), so the signature change had zero blast radius here, but it is a breaking change for any external consumer of the `MaterialLibrary` NuGet package built before this change — flag it if/when a new package version is cut. No JSON schema impact (the deleted types were never serialized).
- Files: src/MaterialLibrary/Domain/StressRuptureTypes.fs, src/MaterialLibrary/Interpolations.fs, src/MaterialLibrary/Domain/MaterialTypes.fs, src/MaterialLibrary/Library.fs, src/MaterialLibrary.Excel/StrengthPropertyFunctions.fs, src/MaterialLibrary.Excel/InterpolationFunctions.fs, AI_HISTORY.md
- Follow-up: `Domain/FatigueTypes.fs`'s `FatigueCurve`/`FatigueCurvePoint` look like the same pattern (only consumed by the legacy `FatigueInterpolation.stressRangeFromCycles`/`cyclesFromStressRange` overloads, which coexist with the correct `FatigueTable`-based `stressRangeFromCyclesOnTable`/`cyclesFromStressRangeOnTable` already used by this project) — not touched here since it was out of the scope the user asked for, but worth the same treatment in a future pass.

Verification: `dotnet build src/MaterialLibrary/MaterialLibrary.fsproj` (0 warnings, 0 errors), `dotnet test tests/MaterialLibrary.Tests` (31/31 passed), `dotnet build tests/MaterialLibrary.Examples` (0 warnings, 0 errors), `dotnet build src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj` (0 warnings, 0 errors, packed `.xll` produced).

## 2026-07-30 - Retired the legacy FatigueCurve type (same cleanup as StressRuptureCurve)

- Date: 2026-07-30
- Area: `Domain/FatigueTypes.fs` (deleted), `Interpolations.fs`, `Domain/MaterialTypes.fs` (core); `StrengthPropertyFunctions.fs` (`MaterialLibrary.Excel`)
- Change:
  - Deleted `Domain/FatigueTypes.fs` (`FatigueCurve`/`FatigueCurvePoint`) and its `<Compile>` entry in `MaterialLibrary.fsproj`; nothing else remained in that file once the two types were removed. Confirmed via repo-wide grep that they had no consumer besides the legacy `FatigueInterpolation` overloads below.
  - In `Interpolations.fs`'s `FatigueInterpolation` module: removed the legacy `FatigueCurve`-based `buildStressPairs`, `mapLogCycleStress`, `mapLogLogStress`, `stressRangeFromCycles`, and `cyclesFromStressRange`; renamed the `FatigueTable`-based `stressRangeFromCyclesOnTable`/`cyclesFromStressRangeOnTable` to the now-unambiguous `stressRangeFromCycles`/`cyclesFromStressRange`, matching how `CreepInterpolation`/`StressStrainInterpolation`/`StressRuptureInterpolation` are named (no "OnTable" suffix, since there is only one shape now).
  - Fixed a stale XML-doc reference on `MaterialTypes.addFatigueCurves` that still named the deleted type.
  - Updated the two Excel call sites in `StrengthPropertyFunctions.fs` (`MatFatigueStressRangeFromCycles`, `MatCyclesFromFatigueStressRange`) to the renamed functions.
- Why: Follow-up from the `StressRuptureCurve` cleanup — this was flagged there as the same pattern (legacy point-list type with no real storage use, coexisting with a correct `PropertyTable`-based type) and the user asked for the same treatment.
- Impact: Internal rename only; `FatigueInterpolation.stressRangeFromCycles`/`cyclesFromStressRange` change meaning (from operating on the now-deleted `FatigueCurve` to operating on `FatigueTable`) for any external consumer that called the old overloads directly — same breaking-change caveat as the `StressRuptureCurve` cleanup. No public `MaterialLibrary`/`Library.fs` member was affected (nothing there wrapped the legacy fatigue functions). No JSON schema impact.
- Files: src/MaterialLibrary/Domain/FatigueTypes.fs (deleted), src/MaterialLibrary/MaterialLibrary.fsproj, src/MaterialLibrary/Interpolations.fs, src/MaterialLibrary/Domain/MaterialTypes.fs, src/MaterialLibrary.Excel/StrengthPropertyFunctions.fs, AI_HISTORY.md
- Follow-up: None outstanding from this pattern; both stress-rupture and fatigue now consistently key off their `PropertyTable`-based storage types.

Verification: `dotnet build src/MaterialLibrary/MaterialLibrary.fsproj` (0 warnings, 0 errors), `dotnet test tests/MaterialLibrary.Tests` (31/31 passed), `dotnet build tests/MaterialLibrary.Examples` (0 warnings, 0 errors), `dotnet build src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj` (0 warnings, 0 errors, packed `.xll` produced).

## 2026-07-30 - Tables-only cleanup: new creep-reference tables, fatigue amplitude correction, no hysteresis loops; schema v14

User-directed follow-up to the curve-vs-table discussion: adopt `PropertyTable`-backed tables exclusively wherever a "curve" concept still meant a raw point list, replacing several remaining ad hoc reuses. This is a breaking change to `StrengthProperties` and the JSON schema (bumped 13 -> 14).

- Date: 2026-07-30
- Area: `Domain/MaterialTypes.fs`, `Domain/CyclicStressStrainTypes.fs`, `Tables/CyclicStrainTable.fs`, `Tables/FatigueTable.fs`, `Tables/CreepStressRuptureTable.fs` (new), `Tables/CreepStrainRateTable.fs` (new), `builders/CyclicStrainTableBuilder.fs`, `builders/CreepStressRuptureTableBuilder.fs` (new), `builders/CreepStrainRateTableBuilder.fs` (new), `Interpolations.fs`, `Serialization/PropertyTableSerialization.fs`, `Serialization/MaterialSerialization.fs`, `Domain/MechanicalProperties.fs`, `Tables/ExternalPressureTable.fs` (core); `StrengthPropertyFunctions.fs` (`MaterialLibrary.Excel`)
- Change:
  - **New tables replacing a `TensileProperties`-reuse hack.** `StrengthProperties.CreepReferenceStress`/`AverageRuptureStress`/`MinimumRuptureStress` were all typed `TensileProperties list` with a `// Reuse structure for T → X mapping` comment — a 5-field tensile-test record hijacked to carry one stress value per temperature, with no dedicated table type and no builder. Replaced with two new `PropertyTable`-backed types (`CreepStressRuptureTable`: temperature vs. stress at a fixed reference duration; `CreepStrainRateTable`: temperature vs. stress at a fixed reference creep-rate criterion) and four `StrengthProperties` fields, one pair per basis: `AverageCreepRuptureStress`/`MinimumCreepRuptureStress: CreepStressRuptureTable list` and `AverageCreepStrainRateStress`/`MinimumCreepStrainRateStress: CreepStrainRateTable list` (basis is which list a table lives in, not a field on the type; each list can hold more than one reference duration/rate, unlike the old fixed-100,000h assumption). Added matching builders (`CreepStressRuptureTableBuilder`/`CreepStrainRateTableBuilder`, each with `create` + `addOrReplaceAverage`/`addOrReplaceMinimum`) and JSON serialization (`CreepStressRuptureTableJson`/`CreepStrainRateTableJson` in `PropertyTableSerialization.fs`).
  - **Fatigue: Sa, not Δσ.** Corrected `FatigueTable`'s Y-axis semantics from "stress range" to "stress amplitude" (they differ by 2x) in the type's XML docs and throughout `Interpolations.fs`'s `FatigueInterpolation` module: renamed `stressRangeFromCycles`/`cyclesFromStressRange` to `stressAmplitudeFromCycles`/`cyclesFromStressAmplitude`, and all internal parameter names/validation messages to match. `RValue`/`StressBasis` (present on the now-deleted legacy `FatigueCurve`) are confirmed *not* being added to `FatigueTable`.
  - **No hysteresis loops.** Removed `HysteresisBranch`/`HysteresisLoopPoint`/`HysteresisLoop` from `Domain/CyclicStressStrainTypes.fs` and the `HysteresisLoops` field from `CyclicStrainTable`, since a loop (loading branch vs. unloading branch at the same strain) is inherently bi-valued and cannot be a single ascending-X `PropertyTable`. `CyclicStrainTable` now carries only its two genuinely monotonic tables: `Table` (stress amplitude vs. strain amplitude) and `HysteresisRangeTable` (stress range vs. strain range). Removed the now-dead loop-construction code (`buildHysteresisLoops`, `interpolateRange`) from `CyclicStrainTableBuilder`, and the corresponding JSON types/fields from `PropertyTableSerialization.fs`.
  - **External pressure: Factor B, named accurately.** Added XML-doc clarification (no field rename) that `ExternalPressureTablePoint.CompressiveStress` *is* the ASME chart's Factor B — a material-chart value dimensioned as a stress and used directly in the UG-28 formulas — on `ExternalPressureTable`, `ExternalPressureTablePoint`, and the `StrengthProperties.ExternalPressureTables` doc comment.
  - Excel: renamed `MatFatigueStressRangeFromCycles`/`MatCyclesFromFatigueStressRange` to `MatFatigueStressAmplitudeFromCycles`/`MatCyclesFromFatigueStressAmplitude` (matching the core rename) and added eight new functions covering the four new `StrengthProperties` fields: `MatAverageCreepRuptureStress`/`MatMinimumCreepRuptureStress` (+ `...Table` variants) and `MatAverageCreepStrainRateStress`/`MatMinimumCreepStrainRateStress` (+ `...Table` variants), each selecting by an optional reference duration/rate (required only when more than one table is stored for that basis).
  - Bumped `MaterialSerialization.CurrentSchemaVersion` from 13 to 14.
- Why: User wants the domain model to represent every curve as a `PropertyTable` (rows = points) with no separate "dedicated point list" types, since a table already *is* a curve. Confirmed case by case: creep reference/rupture-at-duration data was the clearest violation (wrong record type, not just a raw list); fatigue's stress-range/amplitude mislabeling and the external-pressure Factor-B naming were corrected as part of the same pass since they were flagged during the review; hysteresis loops were deliberately kept out of the "must become a table" rule because they are not representable as one (non-monotonic), and the user confirmed dropping them entirely rather than splitting into two per-branch tables.
- Impact: **Breaking.** `StrengthProperties` field set changed (3 removed, 4 added); `FatigueInterpolation` function names changed; `CyclicStrainTable`/`CyclicStressStrainTypes` lost `HysteresisLoops`/`HysteresisLoop`/`HysteresisLoopPoint`/`HysteresisBranch`; JSON schema version 13 -> 14 (old JSON files will be rejected by `validateSchemaVersion` until re-exported). No production callers existed for the removed/renamed core members (confirmed via repo-wide search), so the only in-repo fallout was `Tests.fs` (updated) and the Excel add-in (updated); flag this for any external consumer of a previously published package version.
- Files: src/MaterialLibrary/Domain/MaterialTypes.fs, src/MaterialLibrary/Domain/CyclicStressStrainTypes.fs, src/MaterialLibrary/Domain/MechanicalProperties.fs, src/MaterialLibrary/Tables/CyclicStrainTable.fs, src/MaterialLibrary/Tables/FatigueTable.fs, src/MaterialLibrary/Tables/ExternalPressureTable.fs, src/MaterialLibrary/Tables/CreepStressRuptureTable.fs, src/MaterialLibrary/Tables/CreepStrainRateTable.fs, src/MaterialLibrary/builders/CyclicStrainTableBuilder.fs, src/MaterialLibrary/builders/CreepStressRuptureTableBuilder.fs, src/MaterialLibrary/builders/CreepStrainRateTableBuilder.fs, src/MaterialLibrary/Interpolations.fs, src/MaterialLibrary/Serialization/PropertyTableSerialization.fs, src/MaterialLibrary/Serialization/MaterialSerialization.fs, src/MaterialLibrary/MaterialLibrary.fsproj, tests/MaterialLibrary.Tests/Tests.fs, src/MaterialLibrary.Excel/StrengthPropertyFunctions.fs, README.md, AI_HISTORY.md
- Follow-up: `Domain/FatigueTypes.fs`'s equivalent legacy-type issue was already resolved in the prior entry. No further known "curve as raw point list" holdouts remain in `StrengthProperties`; `LarsonMillerCurve` is the one intentional exception left (not requested for conversion in this pass) since it is a genuinely simple, single-parameter (P → stress) relation without a size/duration dimension.

Verification: `dotnet build src/MaterialLibrary/MaterialLibrary.fsproj` (0 warnings, 0 errors), `dotnet test tests/MaterialLibrary.Tests` (31/31 passed after updating the two affected tests), `dotnet build tests/MaterialLibrary.Examples` (0 warnings, 0 errors), `dotnet build src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj` in Debug and Release (0 warnings, 0 errors, packed `.xll` produced both times).
