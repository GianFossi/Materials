# Copilot Chat Instructions

## Project Coding Rules
- Use English XML comments for modules, functions, methods/subroutines, and enums.
- For enums used in UI dropdowns, include display metadata (Description or EnumMember labels).
- Add practical inline comments for complex logic, loops, and recursion.
- Units are fixed a priori in this project; write the unit explicitly in XML comments for every property and table.

## Engineering Principles
- Prioritize maintainability with autonomous sections and modular files.
- Avoid side effects; prefer pure functions and explicit boundaries.
- Ensure APIs are friendly for C#, Python, and C++ consumers.

## Reliability and Performance
- Always check robustness: null/empty/range/state validation.
- Avoid idle loops and unsafe constructs.
- Minimize potential crash sources and optimize hot paths.

## Documentation and Examples
- Update README.md after relevant feature or design changes.
- Update tests/examples for new public capabilities.
- Update AI_HISTORY.md with a short, structured entry for each meaningful modification.
- When implementing Material serialization/deserialization, document units in XML comments for each serialized property and table.
