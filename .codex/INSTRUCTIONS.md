# Codex Project Instructions

## Mandatory Style
- XML comments must be in English only.
- Document modules, functions, subroutines/methods, and enums.
- Enum entries intended for dropdown UI must expose a display literal/label via attributes (Description or EnumMember).
- Units of measure are fixed a priori for the project; declare units explicitly in XML comments for each property and table.

## Readability for New Developers
- Add clear inline comments for non-obvious logic.
- In loops, document exit/termination conditions.
- In recursive functions, document base case and recursive step.

## Maintainability
- Split large logic into autonomous sections.
- Prefer multiple focused files/modules over monolithic files.
- Keep debugging paths easy: small functions, predictable control flow.

## Functional Safety
- Avoid side effects by default.
- Keep pure computations separate from I/O.
- Make behavior deterministic where possible.

## Interoperability
- Public APIs must be consumable from C#, Python, and C++ interop scenarios.
- Prefer simple DTO-like data shapes and explicit types on public boundaries.

## Robustness and Performance
- Validate inputs and return errors explicitly.
- Avoid crash-prone code paths and unchecked assumptions.
- Avoid idle loops and unnecessary re-computation.
- Evaluate potential performance bottlenecks before finalizing changes.

## Update Policy
- Keep README.md synchronized with features and rules.
- Keep tests/examples updated with working, robust usage samples.
- Keep AI_HISTORY.md updated with one entry per meaningful change.
- For Material serialization/deserialization code, require XML comments describing units for every serialized property and table.
