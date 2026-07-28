# Contributing Guide

This checklist keeps contributions consistent, robust, and easy to maintain.

## 1) Documentation Requirements

- Write XML comments in English only.
- Add XML comments for modules, functions, methods/subroutines, and enums.
- Keep summaries concise and behavior-oriented.
- For enums used in UI dropdowns, expose display labels with metadata attributes such as Description or EnumMember.

## 2) In-Code Clarity

- Add short comments in non-trivial functions.
- In loops, explain intent and exit conditions.
- In recursive functions, explain base case and recursive step.
- Prefer comments that help onboarding new developers.

## 3) Maintainability and Structure

- Organize code into autonomous sections and cohesive modules.
- Split large features into focused files when practical.
- Keep control flow predictable to simplify debugging.

## 4) Side Effects Policy

- Avoid side effects by default.
- Keep pure calculations separate from I/O and persistence.
- Isolate unavoidable side effects at clear boundaries.

## 5) Interoperability (F#, C#, Python, C++)

- Keep public APIs CLR-friendly and explicit.
- Prefer stable signatures and simple data shapes for cross-language use.
- Avoid exposing advanced F#-specific patterns directly unless wrapped.

## 6) Robustness and Performance

- Validate inputs (null/empty/range/state) before use.
- Return explicit errors rather than crashing where possible.
- Avoid idle/infinite loops and define safe termination conditions.
- Watch for unnecessary allocations and repeated work in hot paths.

## 7) Mandatory Updates

- Update README.md whenever behavior, architecture, or public APIs change.
- Update examples/tests for new public capabilities.
- Update AI_HISTORY.md with one structured entry for each meaningful change.

## 8) Build and Validation

Before opening a PR:

1. Build the library:
   dotnet build .\src\MaterialLibrary\MaterialLibrary.fsproj
2. Run tests:
   dotnet test .\tests\MaterialLibrary.Tests\MaterialLibrary.Tests.fsproj
3. Compile examples:
   dotnet build .\tests\MaterialLibrary.Examples\MaterialLibrary.Examples.fsproj
4. Ensure examples and docs reflect current APIs.
