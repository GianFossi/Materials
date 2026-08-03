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

## 2026-07-30 - Bumped version to 1.0.2 everywhere

- Date: 2026-07-30
- Area: Version metadata
- Change: Bumped every version string in the repository from `1.0.0` to `1.0.2`: `Version`/`PackageVersion`/`Title` in `src/MaterialLibrary/MaterialLibrary.fsproj`, `Version` in `src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj`, the matching mentions in `README.md` (current-release bullets, packed `.nupkg` filename, package title), and `Configuration.createDefault`'s `ConfigurationVersion` default.
- Why: User-requested version update, reflecting the breaking `StrengthProperties`/JSON-schema (v13->v14) and Excel add-in changes made since the `1.0.0` release; user confirmed the version should read `1.0.2` everywhere, including `ConfigurationVersion` (a separate, independent config-file schema version, not the package version, but still requested to move in lockstep).
- Impact: Metadata only; no logic changes. `ConfigurationVersion` only affects newly created default configuration files (via `Configuration.createDefault`/`loadOrCreateDefault`) — existing saved `MaterialLibrary.config.xml` files keep whatever `ConfigurationVersion` they already have (it is free-text and only checked for non-blank, not validated against a specific value).
- Files: src/MaterialLibrary/MaterialLibrary.fsproj, src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj, src/MaterialLibrary/Configuration.fs, README.md, AI_HISTORY.md
- Follow-up: None.

Verification: `dotnet build src/MaterialLibrary/MaterialLibrary.fsproj` (0 warnings, 0 errors), `dotnet test tests/MaterialLibrary.Tests` (31/31 passed), `dotnet build src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj` (0 warnings, 0 errors, packed `.xll` produced).

## 2026-07-31 - New WPF desktop CRUD application (`MaterialLibrary.CrudApp`)

- Date: 2026-07-31
- Area: New project `src/MaterialLibrary.CrudApp`
- Change: Added a Windows-only WPF desktop application (`net8.0-windows`, `OutputType=WinExe`, `UseWPF=true`) that wraps `MaterialLibrary.Crud`'s `MaterialCrudRepository` in a UI, publishable to a standalone `MaterialLibrary.CrudApp.exe`. The UI is built entirely in F# code (`MainWindow.fs`, `MaterialEditWindow.fs`) with no XAML files, since F# has no supported partial-class/`x:Class` code-behind model for XAML compilation. Features: new/open/save a material library JSON file (`MaterialCrudRepository.LoadFromFile`/`SaveToFile`), a `DataGrid` listing materials (Id, Name, Specification, Grade, LastModified), and create/edit/delete via a modal dialog covering identity fields (`ProductForm`, `NominalComposition`, `Specification`, `Grade`, `Class_Condition_Tempering`, `AlloyIdentification_UNS`, using `Material.setIdentity` to recompose `Name`), `BasicProperties` (`ElongationPercent`, `ReductionOfAreaPercent`, SMYS, SMUTS), and `Notes`. Added `MaterialErrorFormat.fs` to render `MaterialError` as a single display string for `MessageBox`/status-bar output.
- Why: User request ("add wpf application to turn into an EXE") for a runnable desktop shell around the existing `MaterialLibrary.Crud` CRUD library.
- Impact: Additive only; no changes to `MaterialLibrary` or `MaterialLibrary.Crud`. Editing property tables (physical/strength/special properties, creep/fatigue curves, etc.) is out of scope for this first version and still goes through `MaterialLibrary.Crud`'s API or direct JSON file editing. Hit and fixed one non-obvious naming collision during development: `MaterialLibrary.Domain.TableDimension` has a nullary case named `Thickness`, which shadows `System.Windows.Thickness` when `open MaterialLibrary.Domain` follows the WPF opens — both new files now open `MaterialLibrary.Domain` before `System.Windows`/`System.Windows.Controls` to avoid this.
- Files: src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.fsproj, src/MaterialLibrary.CrudApp/MaterialErrorFormat.fs, src/MaterialLibrary.CrudApp/MaterialEditWindow.fs, src/MaterialLibrary.CrudApp/MainWindow.fs, src/MaterialLibrary.CrudApp/Program.fs, README.md, AI_HISTORY.md
- Follow-up: No automated UI tests (WPF `DataGrid`/dialogs are not unit-testable the same way as the core library); verified manually by publishing and launching the EXE. Consider adding property-table editors in a future pass if the CRUD app needs to cover more than identity/basic properties.

Verification: `dotnet build src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.fsproj` (0 warnings, 0 errors) and `dotnet publish src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.fsproj -c Release -r win-x64 --self-contained false -o publish/crud-app` (produces `MaterialLibrary.CrudApp.exe`); launched the published EXE and confirmed the window opens and stays running without crashing.

## 2026-07-31 - Rewrote `MaterialLibrary.CrudApp` in C# with XAML/MVVM

- Date: 2026-07-31
- Area: `src/MaterialLibrary.CrudApp` (replaces the F# version added earlier the same day)
- Change: Replaced the hand-built F# WPF window with a C# project (`MaterialLibrary.CrudApp.csproj`) using XAML views, MVVM view models, and an explicit F#-interop layer. Structure: `Interop/` (`FSharpInterop`, `MaterialErrorFormat`, `MaterialFactory`), `ViewModels/` (`ObservableObject`, `RelayCommand`, `MaterialRowViewModel`, `MaterialEditViewModel`, `MainViewModel`), `Services/` (`IDialogService`, `DialogService`), `Views/` (`MainWindow.xaml`, `MaterialEditWindow.xaml`), plus `App.xaml` holding shared styles and composing the object graph in `OnStartup`. Feature set is unchanged (new/open/save library JSON, DataGrid listing, create/edit/delete via modal dialog) with SMYS and SMUTS added as grid columns and notes surfaced as a row tooltip.
- Why: User request - WPF's data binding, `ICommand`, and `x:Class` code-behind model are built around C# and around mutable, change-notifying view models, which F# records cannot provide. The F# version had to construct every control imperatively and could not use XAML at all.
- Impact: Additive/replacing at the app layer only; no changes to `MaterialLibrary` or `MaterialLibrary.Crud`. All F#-specific representations are now confined to `Interop/`, so view models and XAML see only C# shapes.
- Files: src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.csproj, App.xaml, App.xaml.cs, Interop/FSharpInterop.cs, Interop/MaterialErrorFormat.cs, Interop/MaterialFactory.cs, ViewModels/ObservableObject.cs, ViewModels/RelayCommand.cs, ViewModels/MaterialRowViewModel.cs, ViewModels/MaterialEditViewModel.cs, ViewModels/MainViewModel.cs, Services/IDialogService.cs, Services/DialogService.cs, Views/MainWindow.xaml(.cs), Views/MaterialEditWindow.xaml(.cs), README.md, AI_HISTORY.md (deleted: MaterialLibrary.CrudApp.fsproj, MainWindow.fs, MaterialEditWindow.fs, MaterialErrorFormat.fs, Program.fs)
- Follow-up: Property-table editors are still out of scope. The interop findings below are the reusable part of this work - re-read them before writing any further C# against this F# domain.

F#/C# interop constraints found and handled (verified empirically by reflection over the built assemblies, not assumed):

1. Immutable records: `Material` compiles to a class with get-only properties and a 23-argument positional constructor. C# has no `{ record with ... }`. `MaterialFactory.Copy` emulates it through that constructor, kept in one file so adding a field to the F# record breaks the build rather than silently mis-assigning positional arguments.
2. `None` is a null reference. `FSharpOption<T>.None` is literally `null`, so `option.Value` on `None` throws `NullReferenceException`, and - the trap that actually bit during development - `notes ?? material.Notes` can never clear an option-typed field, because the `None` the caller passes to clear it *is* the null that triggers the fallback. Option-typed values are nullable-annotated throughout so the C# compiler flags unchecked access; `FSharpInterop.ToOption` also maps blank/whitespace text to `None` (WPF `TextBox.Text` is `""`, never `null`).
3. `Result<'T,'TError>` surfaces as `FSharpResult<T,TError>` with `IsOk`/`ResultValue`/`ErrorValue`; reading the wrong branch throws. `FSharpInterop.TryUnwrap` converts it to a C# try-pattern so branching cannot be skipped.
4. `'T list` surfaces as `FSharpList<T>`. It implements `IEnumerable<T>` so WPF *can* bind to it, but it is a singly linked list (O(n) indexing) with no change notification, so it is projected into an `ObservableCollection<MaterialRowViewModel>`.
5. Discriminated unions compile to nested subclasses; C# matches them with a type-pattern `switch`, payloads are `Item` / `Item1` / `Item2`. Nullary cases (e.g. `InsufficientData`) get a mangled nested type name and must be matched via their `IsX` property instead. C# cannot verify exhaustiveness, hence explicit default arms.
6. Module/type name collisions: because a type `Material` already occupies `MaterialLibrary.Domain`, the `Material` *module* compiles to a class named `MaterialModule` (likewise `BasicPropertiesModule`, `PhysicalPropertiesTableModule`). Curried F# functions compile to ordinary .NET methods taking all arguments, with the record argument **last**, mirroring F# pipeline order.
7. Namespace shadowed by a type (the one that broke the build): the F# library declares a type `MaterialLibrary.MaterialLibrary`, so inside any namespace nested under `MaterialLibrary` the C# compiler binds the identifier `MaterialLibrary` to that *type*, not the namespace. Fully qualified names such as `MaterialLibrary.CrudApp.App` then fail with CS0426 - including those emitted by the WPF XAML generator, which does not prefix with `global::`. Fixed by setting the app's CLR namespace to `MaterialLibraryCrudApp`; `AssemblyName` stays `MaterialLibrary.CrudApp`.

Verification: `dotnet build src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.csproj` (0 warnings, 0 errors); launched the built EXE and confirmed the window opens with the bound title `Material Library CRUD - (unsaved)` and stays running. Additionally drove `MainViewModel` headlessly through a stub `IDialogService` (28 assertions, all passing): create/duplicate-rejection/edit/delete, command enablement following selection, non-numeric and blank-Id validation, `Name` recomposition via `Material.setIdentity`, notes cleared to `None` and surviving a save/load round trip through the F# JSON serializer, and a missing-file open reported as a message instead of an exception.

## 2026-07-31 - CRUD app grid now shows full material identification

- Date: 2026-07-31
- Area: `src/MaterialLibrary.CrudApp` (grid columns)
- Change: Replaced the main grid's columns with the full material identification set requested by the user: Id, Specification, Grade, Class/Condition/Tempering, UNS, Form, Product analysis, Family, Full name. Added the backing projections to `MaterialRowViewModel` (`ClassConditionTempering`, `AlloyIdentificationUns`, `ProductForm`, `ProductAnalysis`, `Family`) and widened the window from 960 to 1180 to fit nine columns.
- Why: User request - the grid previously showed only Id, Name, Specification, Grade plus SMYS/SMUTS and last-modified, which is not enough to identify a material.
- Impact: View-layer only; no domain, CRUD, or serialization changes. Two columns map to fields whose names differ from the requested headers: "Product analysis" is backed by the domain's `NominalComposition` (the only composition field on `Material`; strictly, an ASME product analysis is the chemistry measured on the finished product, so distinguishing them would need a new domain field), and "Full name" is `Material.Name`, which the domain composes as Specification + Grade + Class/Condition/Tempering + UNS. The SMYS, SMUTS, and last-modified columns were dropped because they are not part of the requested identification set; `LastModified` is still exposed on `MaterialRowViewModel` if a column is wanted again.
- Files: src/MaterialLibrary.CrudApp/ViewModels/MaterialRowViewModel.cs, src/MaterialLibrary.CrudApp/Views/MainWindow.xaml, README.md, AI_HISTORY.md
- Follow-up: If "product analysis" is meant to be distinct from nominal composition, it needs a new field on the F# `Material` record plus serialization support.

Interop note (adds to the list in the previous entry): `Material.Family` stacks two F# constructs - an `option` wrapping a discriminated union (`AsmeMaterialFamily option`). The option is unwrapped first (`None` is null, rendered as an empty cell), then the domain's own `AsmeMaterialFamily.code` maps the union case to its ASME display code. Calling `ToString()` on the case instead would print the F# identifier (`LAS1_00`, `SSDPlus`) rather than the intended code (`LAS1.00`, `SSD+`). Also confirmed by reflection: F# nullary DU cases surface to C# as static properties on the union type (`AsmeMaterialFamily.CS`, `AsmeMaterialFamily.SSDPlus`, ...), and the `AsmeMaterialFamily` module compiles to `AsmeMaterialFamilyModule` for the usual type/module name-collision reason.

Verification: `dotnet build src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.csproj` (0 warnings, 0 errors); launched the built EXE. Extended the headless view-model harness (17 assertions, all passing): every identification column returns the expected value for a fully populated material, `Family` renders an empty cell when `None`, all 13 `AsmeMaterialFamily` cases render their ASME codes (`CS`, `QT`, `LTCS`, `LAS1.00`, `LAS1.25`, `LAS2.25`, `LAS5.00`, `LAS9.00`, `SSA`, `SSF`, `SSM`, `SSD`, `SSD+`), and Family/UNS/product-analysis survive a save/load round trip through the F# JSON serializer.

## 2026-07-31 - CRUD app: mutable material mirror, user-settable Family, generic table editor

- Date: 2026-07-31
- Area: `src/MaterialLibrary.CrudApp`
- Change: Three additions. (1) `Interop/MaterialDraft.cs`: a complete mutable mirror of the immutable F# `Material` record, now the single sanctioned way to edit a material from C# and the only call site of the record's 23-argument positional constructor. (2) Family is user-settable: `ViewModels/MaterialFamilyChoice.cs` supplies the 13 `AsmeMaterialFamily` cases plus a "(not assigned)" entry standing in for `None`, bound to a `ComboBox` in the edit dialog. (3) A generic table editor: `Interop/MaterialTableSpec.cs` describes each editable table (columns, units, read, write), `ViewModels/TableRowViewModel.cs` + `ViewModels/MaterialTablesViewModel.cs` drive it, and `Views/MaterialTablesWindow.xaml` renders one grid whose columns are generated at runtime from the selected table's spec. Reached from the main toolbar via "Edit Tables...".
- Why: User request for full editing of the Material/MaterialLibrary objects, with Family set by the user, and an explicit instruction to guard the immutable-F#/mutable-C# mirror against drift.
- Impact: App layer only; no domain, CRUD, or serialization changes. Table writes delegate to the existing F# helpers (`PhysicalPropertyCrud`, `StrengthPropertyCrud`) so domain rules such as sort-by-temperature and the `LastModified` stamp stay in the library. Eight tables are wired: thermal expansion, elastic modulus, density, specific heat, thermal conductivity, tensile properties, allowable stresses, compression properties. Adding another table is one `MaterialTableSpec` entry, no new UI.
- Files: src/MaterialLibrary.CrudApp/Interop/MaterialDraft.cs, Interop/MaterialTableSpec.cs, ViewModels/MaterialFamilyChoice.cs, ViewModels/TableRowViewModel.cs, ViewModels/MaterialTablesViewModel.cs, ViewModels/MaterialEditViewModel.cs, ViewModels/MainViewModel.cs, Services/IDialogService.cs, Services/DialogService.cs, Views/MaterialTablesWindow.xaml(.cs), Views/MaterialEditWindow.xaml, Views/MainWindow.xaml, AI_HISTORY.md
- Follow-up: Still pending from the same request, in the agreed order - full editing of the remaining complex tables (stress-strain, creep, external pressure, cyclic strain, Norton/Garofalo/Kachanov models, Code Case 2964), then SQLite CRUD, then XML. Decisions taken with the user: the database write layer goes in `MaterialLibrary.Crud` (F#); the app works on a *copy* of `asme_materials.db` and writes only on explicit save; XML will be a new schema mirroring the existing JSON, written in F# next to `MaterialSerialization`.

Survey findings recorded for the pending work (verified against the sources and the live database, not assumed):

- The database is external to the repository. The working copy is `C:\Users\ganfossi\Documents\DataBase\data\asme_materials.db` (2.26 MB, 17 tables + 1 view, 2129 materials); other copies exist under `MyExcelTools` and `Downloads`.
- `AsmeMaterialRepository` opens it `Mode=ReadOnly` and reads only `Materials`, `AllowableStress1Table`, `AllowableStress1HTable` (when present), `AllowableStress2Table`, `YieldStrengthTable`, and `UltimateStrengthTable`. There is no write path anywhere in the library. Unread tables: `AllowableStress3Table`, `AssociationRules`, `DataTableASME`, `ElasticModulusTable`, `ExternalPressureTable`, `MaterialGroupMap`, `NotesDatabase`, `SpecificHeatTable`, `ThermalConductivityTable`, `ThermalDiffusivityTable`, `ThermalExpansionTable`.
- The database schema is pivoted: every property table stores one column per temperature (`T_40` ... `T_900`, and `T_-200`/`T_-125`/`T_-75` on `ElasticModulusTable`), whereas the domain models the same data as `(temperature, value)` row lists. Any DB CRUD layer needs an explicit pivot/unpivot step; the temperature grids differ per table, so they cannot share one.
- XML read/save for `Material`/`MaterialLibrary` does not exist. `MaterialSerialization`/`MaterialLibrarySerialization` are JSON-only (`System.Text.Json`). `MaterialLibraryDataXml` and `XmlDataCrud` handle the *staged external-pressure chart XML* under `src/MaterialLibrary/data`, which is a different concern.

Verification: `dotnet build src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.csproj` (0 warnings, 0 errors); launched the EXE. Two headless harnesses over the view models, all passing:
- Mirror deep check, 35 assertions: a fully populated material (every one of the 23 fields set to a non-default value, including nested tables) round-trips through `MaterialDraft` with **zero drift across all 22 non-timestamp fields**, compared field-by-field by reflecting over the F# record itself so a future field addition fails the check automatically. Also: `CreatedDate` preserved while `LastModified` is restamped; nested property tables passed through by reference; the source record never mutated; every option-typed field (Family, Notes, time-dependent start temperature, all three max temperatures, welding info) clears to `None` from both `null` and whitespace, and sets back to `Some`; half-filled welding info retained.
- Table editor, 39 assertions: all 8 tables filled through the view model and read back from the domain; rows sorted by temperature per the domain rule; optional cells (`Poisson ratio`, allowable-stress service levels) round-tripping as `Some`/`None`; all 8 tables coexisting on one material with identity untouched; emptying an optional table yielding `None` rather than `Some []`; non-numeric and missing-required cells rejected with the offending column named; wholly blank rows dropped rather than rejected; an invalid grid blocking a table switch; and the whole thing surviving a save/load round trip through the F# JSON serializer with the blank-optional pattern intact.

## 2026-07-31 - XML serialization, database CRUD with schema provisioning, and full table editing

- Date: 2026-07-31
- Area: `src/MaterialLibrary` (XML), `src/MaterialLibrary.Crud` (database), `src/MaterialLibrary.CrudApp` (UI)
- Change: Completed the three outstanding workstreams plus the staged-XML import.
  1. **XML** - new `src/MaterialLibrary/Serialization/MaterialXmlSerialization.fs` with `JsonXmlBridge`, `MaterialXmlSerialization`, and `MaterialLibraryXmlSerialization`. XML is defined as a faithful transform of the existing JSON rather than a second hand-written mapping from the domain, so the two formats cannot drift: a field added to `MaterialSerialization` appears in the XML automatically. Value kinds are preserved with a `t` attribute (`s` string, `n` number, `b` bool, `z` null, `a` array; no attribute means object), which is what makes an empty object distinguishable from an empty array and an empty string.
  2. **Database** - new `MaterialDatabaseSchema.fs` and `MaterialDatabaseCrud.fs`. `ensureSchema` creates 11 application-owned tables that the stock `asme_materials.db` has no home for, each linked by `MaterialID INTEGER NOT NULL REFERENCES Materials(ID) ON DELETE CASCADE` with a covering index, in normalized long form (one row per temperature) rather than the legacy pivoted `T_40 ... T_900` layout. Full CRUD: `createWorkingCopy`, `ensureSchema`, `listMaterials`, `readMaterial`, `upsertMaterial`, `upsertMaterials`, `deleteMaterial`.
  3. **Tables** - the generic editor's cell type moved from `double?[]` to `string?[]` so columns can be text as well as numeric, and four more tables were registered: Norton power law, Garofalo, Kachanov omega (7 numeric + 1 text column), and Code Case 2964 Appendix III (11 numeric + 1 optional text). Twelve tables total.
  4. **UI** - menu bar carrying the whole command set; `DatabaseViewModel`/`DatabaseWindow` for the database manager; library and single-material XML open/save; staged XML data-file import wired to the repository's existing `ImportXmlDataIntoMaterial`.
- Why: User request to complete the remaining three workstreams, with the added requirement that tables present in the `Material` object but missing from `ASME_Material.db` must be created, populated, and linked inside the database file.
- Impact: `MaterialLibrary` and `MaterialLibrary.Crud` both gain public API; no existing behaviour changed. `MaterialLibrary.Crud` now takes a direct `Microsoft.Data.Sqlite` package reference. The application never writes to the database file the user selects: opening one copies it to a `.working.db` beside the original and every operation targets the copy, with "Save Working Copy As..." the only route back to a permanent file.
- Files: src/MaterialLibrary/Serialization/MaterialXmlSerialization.fs, src/MaterialLibrary/MaterialLibrary.fsproj, src/MaterialLibrary.Crud/MaterialDatabaseSchema.fs, src/MaterialLibrary.Crud/MaterialDatabaseCrud.fs, src/MaterialLibrary.Crud/MaterialLibrary.Crud.fsproj, src/MaterialLibrary.CrudApp/Interop/MaterialTableSpec.cs, ViewModels/TableRowViewModel.cs, ViewModels/MaterialTablesViewModel.cs, ViewModels/DatabaseViewModel.cs, ViewModels/MainViewModel.cs, Services/IDialogService.cs, Services/DialogService.cs, Views/DatabaseWindow.xaml(.cs), Views/MainWindow.xaml, README.md, AI_HISTORY.md
- Follow-up: The tables that wrap a nested `Points` list plus per-table metadata - stress-strain, creep tables, stress-rupture, external pressure, cyclic strain, fatigue, Larson-Miller - are still not editable in the grid UI, because a flat one-grid-per-table shape cannot express them; they need a master/detail editor. They are nonetheless preserved without loss through `MaterialDraft`, both serializers, and the database document store, so no data is at risk in the meantime.

Design decision worth keeping: each material is persisted to the database **twice, deliberately**. Scalar identity goes to the ASME `Materials` row and tabular data to the normalized extension tables, so the values stay queryable with ordinary SQL; in parallel the complete material is written to `MaterialDocumentStore` as its canonical JSON, and that document is the source of truth on read. This is what guarantees that data with no dedicated table - creep models, stress-strain curves, fatigue curves, Code Case 2964 constants - survives a database round trip byte-for-byte. Materials that exist only in the shipped ASME rows have no document and are reported as "ASME reference" in the manager; they must be imported and saved once before `readMaterial` will return them.

Bug caught during the build, worth remembering: in F#, mixing a `for ... ->` comprehension with a trailing literal element in the same list expression **silently discards the literal**. `indexDefinitions` was written that way and the unique `MaterialKey` index was never being created; the compiler flagged it as FS3221 ("expression returns a value but is implicitly ignored"). Both arms now use an explicit `yield`.

Verification: all projects build with 0 warnings, 0 errors; Release EXE published and launched. Three headless harnesses, 106 assertions total, all passing:
- XML, 18 assertions: a fully populated material round-trips XML -> material with **byte-identical JSON**, structural record equality, `Some`/`None` preserved including a `None` nested inside a list, scientific notation preserved, library round trip keeping both materials in order, file save/load for both levels, and malformed XML reported as an error rather than an exception. (Note: `MaterialLibrarySerialization.toJson` stamps `CreatedDate`/`LastModified` with `DateTime.UtcNow` on every call, so two serializations of the same library can never compare equal - the materials must be compared instead.)
- Database, 45 assertions, run against a working copy of the real 2.26 MB / 2129-material `asme_materials.db`: all 11 tables created with FK links and 9 covering indexes, provisioning idempotent, reference data untouched, `None` stored as SQL NULL, lossless read-back including a Norton model that has no dedicated table, update replacing rows rather than duplicating them, delete cascading through every linked table, 2130 materials listed after insert, bulk upsert, a non-ASME database rejected with a clear error, and **the source database byte-length unchanged** throughout.
- Application, 43 assertions: all 12 tables registered, the four new creep/special tables committing and reading back, free text accepted in text columns while numeric columns are still validated, library and single-material XML through the view models with Kachanov text and Code Case 2964 constants surviving, database opened on a working copy with the created tables named in the status bar, export/import/delete through the manager with a lossless JSON-identical round trip, `MainViewModel` merging database imports, staged XML data import recording its reference on the material, and both the original database and the user's picked file left untouched.


## 2026-08-02 - Solution audit follow-up: release blocker, async safety, logging, documentation

- Date: 2026-08-02
- Area: `src/MaterialLibrary` (packaging), `src/MaterialLibrary.CrudApp` (logging, async, docs), `tests/MaterialLibrary.CrudApp.Tests`
- Change: Addressed the four findings from the whole-solution audit.
  1. **Release blocker.** `data/ASME_Materials.db` is packed into the NuGet output (`Pack="true"`) but was the only untracked file of the 93 under `data/`. It is now staged for commit, and a `VerifyPackagedDataFiles` MSBuild target fails `dotnet pack` with a clear message when the file is absent. Previously `pack` succeeded and silently produced a package with no database, which only failed later at the consumer.
  2. **Fire-and-forget async.** All nine `_ = SomethingAsync()` sites in `DatabaseViewModel` now route through a `RunDetached`/`ObserveAsync` pair that awaits the work and reports failures to both the log and the status bar. Three of them also had a status-message race - they set `StatusMessage` *before* starting a refresh that overwrites it - so those now set the message after the refresh settles.
  3. **Exception logging.** New `IAppLogger`/`AppLogger`/`AppLog` in `Services/`. Rolling per-day file under `%LOCALAPPDATA%\MaterialLibrary.CrudApp\logs\`, thread-safe, never throws. `AsyncRelayCommand` now logs through it instead of `Debug.WriteLine`, and `App` installs three global handlers (dispatcher, app domain, unobserved task).
  4. **XML documentation.** `GenerateDocumentationFile` is enabled, so undocumented public members are now a compiler warning rather than a convention. All 292 of them were documented across 21 files.
- Why: User request following the audit.
- Impact: No behavioural change to the domain, CRUD, or serialization layers. The app gains a log file and a `--diagnostic` switch; async failures that were previously invisible now surface. Documentation coverage is enforced by the build from here on.
- Files: src/MaterialLibrary/MaterialLibrary.fsproj, src/MaterialLibrary.CrudApp/MaterialLibrary.CrudApp.csproj, App.xaml.cs, Services/IAppLogger.cs, Services/AppLogger.cs, Services/AppLog.cs, Services/IDialogService.cs, Services/DialogService.cs, Services/SchemaUndo.cs, Services/TransactionJournal.cs, Services/TransactionReverter.cs, ViewModels/AsyncRelayCommand.cs, ViewModels/DatabaseViewModel.cs, ViewModels/MainViewModel.cs, ViewModels/MaterialDiffViewModel.cs, ViewModels/MaterialTablesViewModel.cs, ViewModels/{Creep,CyclicStrain,ExternalPressure,Fatigue,LarsonMiller,StressRupture,StressStrain}*EditorViewModel.cs, Views/MaterialDiffWindow.xaml.cs, Interop/MaterialFactory.cs, tests/MaterialLibrary.CrudApp.Tests/AppLoggerTests.cs, docs/desktop-app.md, AI_HISTORY.md
- Follow-up: `DatabaseViewModel` is ~2000 lines covering materials, raw tables, SQL, plotting, undo/redo, sessions, and exports; it is now fully documented but still wants splitting along those seams. Separately, "Save Working Copy As..." pre-fills the dialog with the *original* database path, so confirming without editing overwrites the reference file the working-copy design exists to protect - the default should point elsewhere.

Two details worth keeping:

- `System.Threading.Lock` is .NET 9+; this solution targets `net8.0-windows`, so the logger uses a plain `object` monitor.
- Only the five predefined XML entities are legal in doc comments. `&mdash;` produced CS1570 and was replaced with a plain hyphen.

Verification: all 7 projects build in Release with **0 warnings, 0 errors** (documentation generation on, so the zero includes CS1591). `MaterialLibrary.Tests` 42/42 and `MaterialLibrary.CrudApp.Tests` **23/23** (8 new logger tests) pass. The three standing harnesses - XML/interop 18, database CRUD 45 against a working copy of the real 2129-material file, application 43 - all still pass, confirming no regression. The pack guard was verified by temporarily removing the database (build fails with the intended message) and restoring it (pack succeeds). The logger was verified against a published **Release** binary: the log file records the session header with `build=RELEASE`, and `--diagnostic` flips the header to `diagnostic=True`.

## 2026-08-02 - Save As guard, and splitting the largest files by logical unit

- Date: 2026-08-02
- Area: `src/MaterialLibrary.CrudApp`, `src/MaterialLibrary`, `src/MaterialLibrary.Excel`, `tests/MaterialLibrary.CrudApp.Tests`
- Change: Two pieces of work.
  1. **"Save Working Copy As..." no longer defaults to the original database.** The dialog is now pre-filled with a `<name>.edited.db` beside the source (numbered if that already exists), so confirming without editing can never overwrite the reference file. Choosing the source anyway is still allowed but goes through a new `IDialogService.ConfirmOverwriteReference` prompt that defaults to No. Paths are compared after full-path normalisation, so a relative path or different letter case cannot slip past the guard.
  2. **Large files split by logical unit.** `DatabaseViewModel.cs` (2256 lines) became a `partial` class across a core file plus `ViewModels/Database/` - Lifecycle, Materials, RawTables, Sql, Schema, Exports, Plot, Transactions - with the supporting projections in their own types file. `Interpolations.fs` (986) became seven files under `Interpolation/`. `MaterialSerialization.fs` (867) became three: the JSON DTOs, single-material serialization, and library serialization. `StrengthPropertyFunctions.fs` (990) became eight themed modules under `Strength/`.
- Why: User request.
- Impact: No behavioural change beyond the Save As guard. Two accessibility widenings were required by the F# splits, both from `private` to `internal`, because a `private` module is scoped to its own declaration group and becomes unreachable once its callers move to another file: `Helpers` in `InterpolationCore.fs`, and `selectByTemperature`, which moved into a new auto-opened `StrengthHelpers` module. Excel worksheet names are unaffected - Excel-DNA discovers functions from every public module and the names come from the `ExcelFunction` attributes, not the module.
- Files: src/MaterialLibrary.CrudApp/ViewModels/DatabaseViewModel.cs, ViewModels/Database/*.cs (9 new), Services/IDialogService.cs, Services/DialogService.cs, src/MaterialLibrary/Interpolation/*.fs (7 new, replacing Interpolations.fs), src/MaterialLibrary/Serialization/{MaterialJsonTypes,MaterialSerialization,MaterialLibrarySerialization}.fs, src/MaterialLibrary/MaterialLibrary.fsproj, src/MaterialLibrary.Excel/Strength/*.fs (8 new, replacing StrengthPropertyFunctions.fs), src/MaterialLibrary.Excel/MaterialLibrary.Excel.fsproj, tests/MaterialLibrary.CrudApp.Tests/SaveWorkingCopyAsTests.cs, tests/MaterialLibrary.CrudApp.Tests/WpfSmokeTests.cs, AI_HISTORY.md
- Follow-up: Files still over 600 lines, with the reason each was left alone. `Library.fs` (809) is a **single F# class**, and F# has no partial classes; splitting it would mean optional type extensions, which compile as static extension methods and would change the shape of the public API for C# and Excel consumers - not worth it. `Tests.fs` (1165), `PropertyTableSerialization.fs` (694), `StressStrainModels.fs` (679), `MaterialDatabaseCrud.fs` (650), `MaterialDatabases.fs` (647), `MaterialTypes.fs` (636), `PropertyTable.Core.fs` (629), and `DatabaseViewModel.RawTables.cs` (612) are all splittable and simply were not reached in this pass.

Note on the F# split mechanics, since it is easy to get wrong: compile order in the `.fsproj` is semantic, not cosmetic. Every new file has to be inserted at exactly the position the original occupied, and the parts have to stay in their original relative order, because F# forbids forward references. The splits above were derived from the existing top-level declaration order, so the resulting order is the original order.

Verification: all 7 projects build in Release with **0 warnings, 0 errors**. `MaterialLibrary.Tests` 42/42 and `MaterialLibrary.CrudApp.Tests` **27/27** (4 new Save As tests) pass. The three standing harnesses - XML/interop 18, database CRUD 45 against the real 2129-material database, application 43 - all still pass, so the refactor is behaviour-preserving. Release EXE published and launched cleanly. The Save As tests assert all four paths: the suggested name is never the source, choosing the source prompts and is refused by default leaving the file byte-identical, choosing it with confirmation proceeds, and saving elsewhere never prompts.

## 2026-08-02 - Reference materials are now importable, and searchable by any identifier

- Date: 2026-08-02
- Area: `src/MaterialLibrary` (lookup), `src/MaterialLibrary.Crud` (database), `src/MaterialLibrary.CrudApp` (manager UI)
- Change: Selecting one of the 2129 shipped ASME rows and importing it now works, and the material list can be searched.
  1. **`AsmeMaterialRepository.findById`** - a key-based lookup. The existing lookups match on specification and grade, which can legitimately return several rows; a grid selection identifies exactly one material and needs an exact key.
  2. **`MaterialDatabaseCrud.readMaterial` falls back to hydration.** A material written by the application still reads from its stored document, losslessly. One that only exists in the reference tables is now assembled from them through `findById` instead of failing. Added `hasStoredDocument` so a caller can tell the two apart.
  3. **`DatabaseMaterialSummary` carries the full identity** - `ClassConditionTemper` and `Uns` were added, and the name is composed with `Material.composeMaterialName` when there is no extension row. Previously every reference row listed with a blank name.
  4. **Search** - a box over the materials list matching ID, material key, specification, grade, class/condition/tempering, UNS, and composed name. Terms are ANDed so adding a word narrows; matching is case-insensitive substring. Filtering happens against an in-memory copy, so typing does not re-query 2129 rows per keystroke.
  5. The import status now names the source, since a hydrated reference material carries only what the ASME schema holds.
- Why: User reported that after loading `asme_materials.db` and selecting a row, none of the other tables or properties were reachable. That was a real gap, not a usage error: `AsmeMaterialRepository` existed in the library but was referenced nowhere in the application.
- Impact: Additive. Existing application-written materials are unaffected - the document path is still tried first and still wins. A hydrated reference material is deliberately asymmetric: it carries identity, tensile rows, and allowable-stress datasets, and once saved it gains a document and becomes richer than the row it came from.
- Files: src/MaterialLibrary/Database.Lookup/AsmeMaterialRepository.fs, src/MaterialLibrary.Crud/MaterialDatabaseCrud.fs, src/MaterialLibrary.CrudApp/ViewModels/Database/DatabaseViewModel.Materials.cs, ViewModels/Database/DatabaseViewModelTypes.cs, Views/DatabaseWindow.xaml, tests/MaterialLibrary.CrudApp.Tests/ReferenceMaterialImportTests.cs, AI_HISTORY.md
- Follow-up: The Raw Tables tab still does not follow the materials selection - browsing a specific material's raw rows means typing its ID into the row filter by hand. Worth linking if it comes up.

Implementation detail worth keeping: `readMaterial` resolves the identifier on the read-write connection (the extension table lives there) and then releases it before hydrating, because `findById` opens its own read-only connection and SQLite will not allow the second connection to work while a reader is open on the first. Splitting the function into a resolve step and a fetch step is what makes that safe.

Verification: all 7 projects build in Release with **0 warnings, 0 errors**. `MaterialLibrary.Tests` 42/42 and `MaterialLibrary.CrudApp.Tests` **37/37** pass, including 7 new tests that run against the real 2129-material fixture rather than a stub - a simplified schema would not exercise the pivoted tables the hydration path reads. They assert: reference rows list with full identity and a composed name; search finds a material by ID, UNS, complete name, and lower-case multi-term input; adding a term narrows rather than widens; clearing restores all 2129; selecting a reference row and importing yields a material with tensile rows and a status naming the source; saving it back gives it a document after which the read is byte-identical JSON; and the fixture file itself is never modified. Confirmed against the live `asme_materials.db` beforehand: material ID 1 hydrated with 15 tensile rows, 1 allowable-stress dataset, 2 applicable ASME codes, family CS. Release EXE published and launched cleanly.

## 2026-08-02 - Reference hydration reads the whole schema, and Raw Tables follows the selection

- Date: 2026-08-02
- Area: `src/MaterialLibrary` (lookup), `src/MaterialLibrary.CrudApp` (manager UI)
- Change: Closed the two limitations left by the previous entry.
  1. **A hydrated reference material is no longer a stub.** `AsmeMaterialRepository.hydrate` previously read only the identity row, tensile tables, and allowable-stress datasets. It now also reads the physical-property tables. Three of them (`ThermalExpansionTable`, `ElasticModulusTable`, `ThermalConductivityTable`) are keyed by a *group* through `MaterialGroupMap` rather than by the material, because ASME publishes them per material group; `SpecificHeatTable` is keyed by `MaterialID` directly. Poisson ratio comes from `Materials.PoissonFactor`, and the ASME P/G welding classification from `DataTableASME`, which was previously ignored entirely. All are stored pivoted (one column per temperature, `T_<degC>`) and are unpivoted by `unpivotTemperatureRow`.
  2. **The Raw Tables tab follows the materials selection.** A "Show Raw Rows" button sets an exact `MaterialID` restriction and switches to that tab, which shows a banner naming the material and a "Show all rows" button to clear it. The restriction is a real `MaterialID = $materialId` comparison combined with the free-text search using AND, not a text match, so following material 77 cannot also pull in rows whose stress value happens to contain "77". Tables with no `MaterialID` column are unaffected.
- Why: User asked for both, having found that a hydrated material carried almost nothing and that the raw tables required typing the ID by hand.
- Impact: Additive; nothing that already worked changed shape. Hydration for the sample material went from identity plus 15 tensile rows to additionally 33 thermal-expansion rows, 14 elastic-modulus rows with Poisson ratio, 30 specific-heat rows, 30 conductivity rows, and P/G welding numbers.
- Files: src/MaterialLibrary/Database.Lookup/AsmeMaterialRepository.fs, src/MaterialLibrary.CrudApp/ViewModels/DatabaseViewModel.cs, ViewModels/Database/DatabaseViewModel.Materials.cs, ViewModels/Database/DatabaseViewModel.RawTables.cs, Views/DatabaseWindow.xaml, tests/MaterialLibrary.CrudApp.Tests/ReferenceMaterialImportTests.cs, AI_HISTORY.md
- Follow-up: `ThermalDiffusivityTable` is mapped in `MaterialGroupMap` but has no home in the domain `PhysicalPropertiesTable`, so it is still unread. `ExternalPressureTable` is read-capable but empty in the shipped database. Both are data-model questions rather than plumbing.

**Unit conversions, verified against the shipped database before writing the code** - these were checked by reading actual values rather than assumed, because a silent mis-scale here would be worse than missing data:

| Table | Stored as | Domain unit | Factor |
| --- | --- | --- | --- |
| `ThermalExpansionTable` | um/m/degC (e.g. `11.5`) | 1/degC | x 1e-6 |
| `ElasticModulusTable` | GPa (e.g. `216.0`) | MPa | x 1000 |
| `SpecificHeatTable` | J/(kg*K) (e.g. `430.58`) | J/(kg*K) | none |
| `ThermalConductivityTable` | W/(m*K) (e.g. `60.4`) | W/(m*K) | none |
| `Materials.Density` | kg/m^3 (e.g. `7750`) | kg/m^3 | none |

Verification: all 7 projects build in Release with **0 warnings, 0 errors**. `MaterialLibrary.Tests` 42/42 and `MaterialLibrary.CrudApp.Tests` **43/43** pass, with 6 new tests against the real 2129-material fixture asserting that hydration yields a full physical-property set, that each quantity lands inside a physically sensible range for steel in the domain's unit (proving the conversions rather than just the row counts), that welding info is present, that "Show Raw Rows" sets the filter and switches tab, that clearing restores, and that the filter genuinely narrows a `MaterialID`-linked table without emptying it. Confirmed against the live `asme_materials.db`: material ID 1 hydrates with alpha 1.15e-5 1/degC, E 216000 MPa, nu 0.3, Cp 430.6 J/(kg*K), k 60.4 W/(m*K), density 7750 kg/m^3, welding P1/G1. Release EXE published and launched cleanly. The three standing harnesses still pass.

## 2026-08-02 - Thermal diffusivity added to the domain; external-pressure rows given a home

- Date: 2026-08-02
- Area: `src/MaterialLibrary` (domain, serialization, lookup), `src/MaterialLibrary.Crud` (schema, CRUD), `src/MaterialLibrary.CrudApp` (table editor)
- Change: Placed the two tables the previous entry flagged as data-model questions.
  1. **Thermal diffusivity** joins specific heat and thermal conductivity on `PhysicalPropertiesTable`, as `ThermalDiffusivityTable: (float * float) list option`. The three describe the same heat-transfer behaviour and ASME publishes them together per material group, so grouping them is what the data already implies. `PhysicalPropertiesTable.create` gained a matching parameter. The value flows end to end: hydrated from `ThermalDiffusivityTable` through `MaterialGroupMap.ThermalDiffusivityGroupID`, persisted in a new `MaterialThermalDiffusivityRows` extension table beside the specific-heat and conductivity rows, editable in the app as a 13th table, and serialized automatically because `PhysicalProperties` is written as the domain record rather than through a separate DTO.
  2. **External pressure** gained `MaterialExternalPressureRows` in the application-owned schema, beside `MaterialTensileRows` and `MaterialAllowableStressRows` - the strength group. Each chart point is flattened to one row carrying its parent's reference temperature, duration, reduction factor, and source, so a row is meaningful on its own in SQL. The domain already held `StrengthProperties.ExternalPressureTables`, so no domain change was needed. Empty today, as expected.
- Why: User request following the previous entry, which flagged both as needing a placement decision.
- Impact: `PhysicalPropertiesTable.create` changed arity - a breaking signature change for external callers, updated at all three call sites in this repository. JSON schema went to **15**, with reads still accepting **14**: the added field is optional, so a document written before it existed loads with the field as `None`. Rejecting 14 outright would have made every previously saved library unreadable for no benefit. XML inherits the change for free, being a transform of the JSON.
- Files: src/MaterialLibrary/Domain/BasicAndPhysicalTypes.fs, Serialization/MaterialSerialization.fs, Database.Lookup/AsmeMaterialRepository.fs, src/MaterialLibrary.Crud/MaterialDatabaseSchema.fs, MaterialDatabaseCrud.fs, MaterialTableCrud.fs, src/MaterialLibrary.CrudApp/Interop/MaterialFactory.cs, Interop/MaterialTableSpec.cs, tests/MaterialLibrary.Tests/Tests.fs, tests/MaterialLibrary.CrudApp.Tests/ReferenceMaterialImportTests.cs, AI_HISTORY.md
- Follow-up: The legacy `ExternalPressureTable` in the shipped database is still unread, and deliberately so. Its columns are `ID, MaterialID, ReferenceData, Notes, T_40 ... T_900` - temperature-pivoted with **no Factor A axis**, whereas the domain models an external-pressure chart as Factor A to allowable compressive stress at a reference temperature. The table holds zero rows, so the meaning of its temperature columns cannot be established from data, and guessing a mapping would be worse than leaving it. Once it is populated the mapping can be settled and the read wired.

Unit decision, following the precedent already set by thermal expansion (um/m/degC converted to 1/degC): the database publishes diffusivity in **mm^2/s** (values around `18.1`), and the domain stores **m^2/s**, so the read multiplies by 1e-6. That keeps the physical properties coherent SI throughout, matching W/(m*K) and J/(kg*K).

Also fixed: the pre-existing `material JSON strictly enforces current schema` test hardcoded `schemaVersion.*14` in its substitution regex. After the bump the substitution silently matched nothing, so the test asserted against unmodified valid JSON and failed. It now matches whatever version the serializer writes, and additionally asserts that the minimum readable version is accepted while one below it is refused - so the next bump cannot repeat the same silent no-op.

Verification: all 7 projects build in Release with **0 warnings, 0 errors**. `MaterialLibrary.Tests` **44/44** and `MaterialLibrary.CrudApp.Tests` **44/44** pass, including new tests that thermal diffusivity round-trips through JSON, that a version 14 document still loads while version 13 is refused, and that a hydrated reference material carries diffusivity inside a physically sensible range for steel in m^2/s. All three standing harnesses pass: the database harness now reports **13 provisioned tables and 11 row indexes**, and the interop harness confirms the v15 document carries `ThermalDiffusivityTable`, that its value survives the round trip, and that a v14 document still loads. Confirmed against the live `asme_materials.db`: material ID 1 hydrates with 33 diffusivity rows at 1.81e-5 m^2/s at 20 degC. Release EXE published and launched cleanly.

## 2026-08-02 - Allowable stresses made visible; Sy/Su and allowables grouped by Size/Diameter/Thickness; elongation moved to the room-temperature test

- Date: 2026-08-02
- Area: `src/MaterialLibrary` (domain, tables, serialization, lookup), `src/MaterialLibrary.Crud` (schema, CRUD), `src/MaterialLibrary.CrudApp` (table editor, material editor), `src/MaterialLibrary.Excel`
- Change: Three linked corrections to how strength data is modelled and shown.
  1. **The allowable-stress grid was always empty.** It read `StrengthProperties.AllowableStresses`, the hand-entered Section III service-level list, which the reference importer never populates - it fills `AllowableStressDatasets`. The data had been hydrated correctly since the reference-import work; nothing was ever bound to it. A new grid, **"Allowable stresses by size group (Div 1 / Div 2)"**, reads the datasets and shows division, case, size band, maximum temperature, creep onset, temperature, and S. The service-level grid is kept and retitled, for materials whose allowables are supplied that way.
  2. **Sy(T) and Su(T) lost their size grouping.** `loadTensileProperties` picked one yield row and one ultimate row with `List.tryHead` and discarded the rest, so a material published across several thickness bands collapsed to a single curve - silently giving a heavy section the strength of a light one. New `TensileStrengthDataset` keeps one curve per published band, alongside the flat governing curve for callers that name no size.
  3. **Elongation and reduction of area were on the wrong record.** They sat on the per-temperature `TensileProperties` row, where the importer copied the room-temperature elongation to every temperature and wrote `0.0` for reduction of area. They come from the room-temperature tensile coupon test and are single scalars, so they now live on `BasicProperties`, with elongation split by rolling direction: `ElongationLongitudinalPercent` and `ElongationTransversePercent`, both `float option`.
- Why: User report - "Allowable stresses are missing! Nothing to see", and "Elongation ... and Area Reductions are referred to the material tensile test at room temperature only, along with SMYS and SMTS. Sy vs. Temperature and Su vs. Temperature shall be shown with Size/Diameter/THK groups."
- Impact: Breaking domain changes. `BasicProperties.create` takes five arguments and both elongations are now `float option`; `TensileProperties` lost `ElongationPercent` and `ReductionOfAreaPercent`; `AllowableStressDataset.SizeMinimum`/`SizeMaximum` were replaced by a single `SizeRange`; `StrengthProperties` gained `TensileStrengthDatasets`. JSON schema went to **16**, read window still opening at **14**.
- Files: src/MaterialLibrary/Domain/BasicAndPhysicalTypes.fs, Domain/MechanicalProperties.fs, Domain/MaterialTypes.fs, Tables/SizeThicknessRange.fs (new), Tables/TensileStrengthDataset.fs (new), Tables/AllowableStressTable.fs, Serialization/MaterialJsonTypes.fs, Serialization/MaterialSerialization.fs, Database.Lookup/AsmeMaterialRepository.fs, MaterialLibrary.fsproj, src/MaterialLibrary.Crud/MaterialDatabaseSchema.fs, MaterialDatabaseCrud.fs, MaterialTableCrud.fs, src/MaterialLibrary.Excel/ExcelHelpers.fs, PhysicalPropertyFunctions.fs, Strength/TensileFunctions.fs, Strength/AllowableStressFunctions.fs, src/MaterialLibrary.CrudApp/Interop/MaterialFactory.cs, Interop/MaterialDraft.cs, Interop/MaterialTableSpec.cs, ViewModels/MaterialEditViewModel.cs and the seven table-editor view models, Views/MaterialEditWindow.xaml, tests/MaterialLibrary.Tests/Tests.fs, tests/MaterialLibrary.CrudApp.Tests/SizeGroupedTableSpecTests.cs (new), ReferenceMaterialImportTests.cs, README docs, AI_HISTORY.md
- Follow-up: SA-53 E/A (ID 15) hydrates three Division 1 "Normal" datasets that all report "All sizes" and differ only in value - 80.7, 94.5, 80.7 MPa, which look like the seamless value and the same value multiplied by the 0.85 welded-pipe joint factor. Nothing in the reference schema distinguishes them, so all three are surfaced as published rather than guessing which applies. 746 size groups in `AllowableStress1Table` hold more than one row; only the 171 whose rows all carry note G5 are classifiable, and those are what produce the high case.

**The inclusive flags matter, and were being dropped.** `SizeThkMIN_Included` and `SizeThkMAX_Included` were read from neither table. The reference data uses them: `(5.0, excluded, NULL, -)` is "over 5 mm" and `(NULL, -, 5.0, included)` is "up to 5 mm incl.". Treating both ends as inclusive makes a 5 mm section match both bands, and the caller silently gets whichever came first. The new `SizeThicknessRange` carries `Minimum`, `MinimumIncluded`, `Maximum`, `MaximumIncluded`, and `SizeThicknessRange.contains` honours them; `describe` renders a band the way ASME prints it. Sizes are in mm.

**Backward compatibility, all three directions.** A version 14 or 15 document still loads: the legacy `elongationPercent` seeds the longitudinal value, because that is the column the importer used to fill it from; an absent `tensileStrengthDatasets` reads as empty; and an absent inclusivity flag defaults to inclusive, which is how ASME prints an unqualified limit. The legacy bare `SizeMinimum`/`SizeMaximum` fields are still read and are no longer written. `TensileProperties` losing two fields is read-compatible without special handling, since unknown JSON properties are ignored and missing ones take the default.

**Schema migration, needed because `CREATE TABLE IF NOT EXISTS` can neither widen nor narrow an existing table.** Provisioning now also (a) adds a reference column this build writes that an older file lacks, via `ALTER TABLE` - currently `Materials.RuptureElongationTransv`; and (b) drops an application-owned row table left in a superseded shape so the current definition recreates it - currently `MaterialTensileRows`, recognised by its obsolete `NOT NULL` `ElongationPercent` column, which made every insert fail. Only application-owned tables are ever dropped, and they are a projection of `MaterialDocumentStore`, which is untouched.

**Two new persisted tables**, both flattened one row per point with the band and discriminators repeated, so a single row answers a question without a join: `MaterialTensileStrengthDatasetRows` (`Kind`, band, `Temperature`, `Strength`) beside `MaterialTensileRows`, and `MaterialAllowableStressDatasetRows` (`Division`, `StressCase`, band, `MaximumTemperature`, `CreepTemperature`, `Temperature`, `AllowableStress`) beside `MaterialAllowableStressRows`. Fifteen application-owned tables and thirteen row indexes now.

**Grid editing of grouped data.** The generic editor shows one flat grid, so a size-grouped table is flattened to one row per point with its tags repeated, and regrouped on those tags when committed. Editing a tag on every row of a group moves that whole group; editing it on one row splits that point into its own group. Duplicate temperatures inside a group collapse to the last one entered, because the underlying `PropertyTable` rejects a repeated X and the write path has no channel to report a validation error back to the grid.

Verification against the live 2129-material database: **1513** materials carry a Division 1 normal allowable, **159** also carry the Division 1 high case, **590** carry Division 2, **117** bolting, **403** have at least one bounded size band, and **1810** have Sy/Su datasets. SA-325 (ID 260) hydrates two diameter bands - 13-25 mm incl. at Sy 634 MPa / S 159 MPa, and 29-38 mm incl. at Sy 558 MPa / S 139 MPa - and both survive a save and read-back through the new tables. SA-334 7 (ID 736) hydrates Division 1 normal at 109 MPa and high at 128 MPa at 40 degC. **Elongation is `None` for all 2129 materials**: `RuptureElongationLong` and `RuptureElongationTransv` are entirely NULL in the shipped database, so the fields are modelled and editable but the reference data supplies nothing - which is why they had to be optional rather than defaulting to zero.

Verification: all 7 projects build in Release with **0 warnings, 0 errors**. `MaterialLibrary.Tests` **50/50** and `MaterialLibrary.CrudApp.Tests` **49/49** pass. Eleven new tests cover the boundary behaviour of an exclusive band, that SA-325 keeps four Sy/Su datasets across two bands with the heavier band weaker, that Division 2 always allows more than Division 1 for the same material, that the normal and high Division 1 cases are both present and ordered correctly, that elongation stays optional and round-trips as `None`, that datasets and their inclusivity flags survive JSON, that both new grids populate for an imported reference material, that a flattened grid regroups cell for cell, and that editing one group's bound leaves the others alone. Also generalised the schema-window test so it exercises every version in the read window instead of naming one.

## 2026-08-03 - Raw tables include SQLite internal objects

- Date: 2026-08-03
- Area: `src/MaterialLibrary.CrudApp` raw-table browser, `tests/MaterialLibrary.CrudApp.Tests`, `docs/desktop-app.md`
- Change: Removed the `sqlite_%` exclusion from the raw-table discovery query so the table picker now includes SQLite-managed internal tables (for example `sqlite_sequence`), and added an integration-style test that opens a working copy, selects `sqlite_sequence`, edits `seq`, saves, and verifies the persisted value.
- Why: Fix the reported CRUD-app limitation where internal tables in `asme_materials.db` were not accessible in the raw-table workflow and therefore could not be edited there.
- Impact: Raw Tables now exposes all entries returned by `sqlite_master` for `table`/`view`; direct maintenance CRUD on internal tables is available from the same UI flow, while working-copy safety remains unchanged.
- Files: src/MaterialLibrary.CrudApp/ViewModels/Database/DatabaseViewModel.RawTables.cs, tests/MaterialLibrary.CrudApp.Tests/InternalTablesCrudTests.cs, docs/desktop-app.md, AI_HISTORY.md
- Follow-up: If users should optionally hide internal SQLite objects, add a UI toggle instead of hard-filtering them out.

## 2026-08-03 - Size-ranged Sy/Su tables and separate allowable-stress editors

- Date: 2026-08-03
- Area: Domain, CRUD, Serialization, ASME repo, Excel, CrudApp ViewModels/Views
- Change: Replaced flat `TensileProperties list` and `AllowableStresses list` in `StrengthProperties` with `SyTable: PropertyTable option` and `SuTable: PropertyTable option` (2D PropertyTable supporting size-range columns). Allowable stress data already lived in `AllowableStressDatasets: AllowableStressDataset list` and is unchanged at domain level. Added five dedicated CrudApp editor tabs (Sy, Su, Allowable Div.1, Div.1 High, Div.2), each a temperature × size-range 2D grid. Column headers carry editable SizeMin/SizeMax bounds (mm); rows are temperatures independent per table.
- Why: User request: edit Sy/Su/allowable tables as temperature × size-range matrices with column headers showing the range, one table per stress type.
- Impact: Breaking domain change (schema version 14 → 15, no backward compatibility). Excel Sy/Su functions now use PropertyTable lookup. ASME DB loading builds 2D PropertyTable from per-row size ranges.
- Files:
  - src/MaterialLibrary/Domain/MechanicalProperties.fs (removed TensileProperties, AllowableStress types)
  - src/MaterialLibrary/Domain/MaterialTypes.fs (StrengthProperties: replaced TensileProperties/AllowableStresses with SyTable/SuTable)
  - src/MaterialLibrary/Serialization/MaterialJsonTypes.fs (StrengthPropertiesJson updated)
  - src/MaterialLibrary/Serialization/MaterialSerialization.fs (version 14→15, strengthProperties round-trip)
  - src/MaterialLibrary/builders/ExternalPressureTableBuilder.fs (tryResolveStrengthRatioR updated)
  - src/MaterialLibrary/Database.Lookup/AsmeMaterialRepository.fs (loadStrengthTable2D, hydrate returns Result)
  - src/MaterialLibrary.Crud/CrudTypes.fs (StoredMaterialTableKind updated)
  - src/MaterialLibrary.Crud/MaterialTableCrud.fs (setSyTable, setSuTable added)
  - src/MaterialLibrary.Crud/MaterialDatabaseCrud.fs (removed MaterialTensileRows / MaterialAllowableStressRows inserts)
  - src/MaterialLibrary.Excel/Strength/TensileFunctions.fs (Sy/Su Excel functions updated)
  - src/MaterialLibrary.CrudApp/Interop/MaterialTableSpec.cs (removed TensileProperties/AllowableStresses specs)
  - src/MaterialLibrary.CrudApp/ViewModels/RelayCommand.cs (added generic RelayCommand<T>)
  - src/MaterialLibrary.CrudApp/ViewModels/SizeRangedColumnViewModel.cs (new)
  - src/MaterialLibrary.CrudApp/ViewModels/SizeRangedTableEditorViewModel.cs (new abstract base)
  - src/MaterialLibrary.CrudApp/ViewModels/StrengthTableEditors.cs (new: Sy/Su concrete editors)
  - src/MaterialLibrary.CrudApp/ViewModels/AllowableStressTableEditorViewModel.cs (new: handles AllowableStressDataset list per source)
  - src/MaterialLibrary.CrudApp/ViewModels/MaterialTablesViewModel.cs (added 5 strength editors + TryBuildMaterial wiring)
  - Various existing CrudApp ViewModels (StrengthProperties constructor updated)
  - tests/MaterialLibrary.Tests/Tests.fs (round-trip test uses SyTable/SuTable; schema version regex updated)
- Follow-up: Add MaterialTablesWindow.xaml tabs and XAML code-behind for the 5 new editors; add XAML data-grid with dynamic columns per size range.

## 2026-08-03 - Merged GitHub main: adopted its size-ranged Sy/Su model, kept this branch's additions

- Date: 2026-08-03
- Area: whole solution
- Change: Resolved the merge of `origin/main` (7d33c86) into local `main` (b1089de). Both branches had implemented "Sy, Su and allowable stresses grouped by Size/Diameter/Thickness" independently and incompatibly. GitHub main's design was adopted.
- Why: User instruction to update the project from the GitHub main repository, plus one decisive technical fact - `PropertyTable` already carried `SizeColumnRange` with `Inclusive`/`Exclusive` bounds in the merge base, before either branch started. GitHub main reuses it through 2D tables; this branch had introduced a parallel `SizeThicknessRange` type that duplicated it. Reusing the existing abstraction wins.
- Impact: `TensileStrengthDataset` and `SizeThicknessRange` are gone. `StrengthProperties` now exposes `SyTable`/`SuTable` as 2D `PropertyTable option`, one column per size band. Editing moved from the flat generic grids to five dedicated tabs (Sy, Su, S Div.1, S Div.1H, S Div.2). JSON schema is **16**, readable back to **15**.
- Files: every conflicted file plus docs; see the resolution notes below.
- Follow-up: `MaterialTensileRows` and `MaterialAllowableStressRows` are now defined but never written - the material document is the source of truth for those curves. If SQL-side querying of Sy/Su matters, a projection of the 2D tables would have to be added deliberately.

**Resolution was not a side-picking exercise.** Taking `--theirs` wholesale would have silently discarded work that has nothing to do with the Sy/Su question, because this branch's versions of several files are supersets: `AsmeMaterialRepository.fs` alone carried `findById`, `loadPhysicalProperties`, `loadWeldingInfo`, `unpivotTemperatureRow`, `loadWideTable` and `materialScalar` - 351 added lines against the base, of which GitHub main touched only the strength loader. Those files were rebuilt as *this branch's version with the remote's specific change applied*, rather than replaced.

**Carried forward from this branch** (GitHub main branched before all three): the room-temperature elongation split into `ElongationLongitudinalPercent`/`ElongationTransversePercent`, both optional; the `ThermalDiffusivityTable` with its hydration, its CRUD setter, its editor grid and its mm^2/s to m^2/s conversion; the enriched physical-property and welding hydration; and the schema migration that adds a missing reference column with `ALTER TABLE` and rebuilds an application-owned table left in a superseded shape.

**Adopted from GitHub main**: the 2D `SyTable`/`SuTable` model, the removal of the `TensileProperties` and `AllowableStress` types, the five size-ranged editor tabs with editable column bounds, `RelayCommand<T>`, the raw-table browser including SQLite internal objects, and `InternalTablesCrudTests`.

**Two bugs found while merging, both fixed.**

The first was already red on GitHub main: `requested database library loads six materials...` asserted Sy(400 degC) = 381 MPa for SA-193 B7 while reading "the first entry of the first column". SA-193 B7 publishes three diameter bands - 534, 483 and 381 MPa at 400 degC for up to 64, over 64 to 100, and over 100 to 180 mm - so the assertion only held if the columns happened to sort with the heaviest band first, and after `create2D` they do not. Verified pre-existing by running the suite on a pristine `origin/main` worktree, which failed identically. The test now selects the column by diameter through `SizeColumnRange.contains` and asserts all three bands, plus that 64 mm belongs to the light band only.

The second was a repeat of one fixed earlier on this branch and reintroduced by the remote's file: the schema-enforcement test hardcoded `"schemaVersion"\s*:\s*15` in its substitution regex. After the bump to 16 the pattern matched nothing, so every "this version must be refused" assertion ran against unmodified valid JSON and passed vacuously. The pattern is now built from `MaterialSerialization.CurrentSchemaVersion`, with an assertion that the substitution actually changed the document, and the test walks the whole supported window.

**Schema window narrowed deliberately.** Version 14 is now refused rather than read. Its tensile data lived in a `tensileProperties` array that no longer has any field to deserialize into, so accepting it would appear to succeed while dropping the strength curves - worse than a clear error naming the version. Version 15 still loads in full: the legacy single `elongationPercent` seeds the longitudinal value, and the absent diffusivity table reads as `None`.

Verification: all 7 projects build in Release with **0 warnings, 0 errors**. `MaterialLibrary.Tests` **48/48** and `MaterialLibrary.CrudApp.Tests` **45/45** pass, including six tests added to cover the combination neither branch tested alone: elongation optionality and its `None` round trip, a pre-split document still loading, thermal diffusivity round-tripping, physical-property and welding hydration in physically sensible SI ranges, the division and case labels with Division 2 allowing more than Division 1, and SA-325 keeping one Sy/Su column per diameter band with the heavier band weaker.
