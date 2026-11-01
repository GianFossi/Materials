# AI Project Instructions

Shared rules for all AI coding assistants (Codex, GitHub Copilot Chat, Claude, etc.).
This is the single source of truth: the tool-specific files (`.claude/CLAUDE.md`, `.codex/INSTRUCTIONS.md`, `.github/copilot-instructions.md`) must only point here.

## 1. Mandatory Style
- XML comments must be in English only.
- Document modules, functions, methods/subroutines, and enums.
- Enum entries intended for dropdown UI must expose a display literal/label via attributes (`Description` or `EnumMember`).
- Units of measure are fixed a priori for the project; declare units explicitly in XML comments for every property and table.

### File Header (mandatory in every source file)
Every file must start with a header comment, in English, containing exactly these three fields:
- **Author**: name of the author of the file.
- **Last revision**: date of the last revision, in ISO format `YYYY-MM-DD`.
- **Purpose**: short description of what the file contains and why it exists.

Rules:
- Update `Last revision` every time the file is modified.
- Do not change `Author` when editing; add a line `Contributors` only if another author is explicitly named.
- Use the comment syntax of the language; examples:

```fsharp
// Author:        Gian-Luca Anfossi
// Last revision: 2026-11-01
// Purpose:       Pure functions for shell thickness calculation (no I/O).
```

```csharp
// Author:        Gian-Luca Anfossi
// Last revision: 2026-11-01
// Purpose:       DTOs exposed to C#/Python/C++ consumers for material data.
```

```python
# Author:        Gian-Luca Anfossi
# Last revision: 2026-11-01
# Purpose:       Import pipeline for the material database.
```

```javascript
// Author:        Gian-Luca Anfossi
// Last revision: 2026-11-01
// Purpose:       Client-side helpers for unit display.
```

- For files that cannot hold comments (e.g. JSON), put the same three fields in a sibling `README` entry or in the `docs/` topic file that describes them.

## 2. Readability for New Developers
- Add clear inline comments for non-obvious logic.
- In loops, document exit/termination conditions.
- In recursive functions, document the base case and the recursive step.

## 3. Maintainability
- Split large logic into autonomous sections.
- Prefer multiple focused files/modules over monolithic files.
- Keep debugging paths easy: small functions, predictable control flow.

## 4. Functional Safety
- Avoid side effects by default; prefer pure functions and explicit boundaries.
- Keep pure computations separate from I/O.
- Make behavior deterministic where possible.

## 5. Interoperability
- Public APIs must be consumable from C#, Python, and C++ interop scenarios.
- Prefer simple DTO-like data shapes and explicit types on public boundaries.

## 6. Robustness and Performance
- Validate inputs (null/empty/range/state) and return errors explicitly.
- Avoid crash-prone code paths, unsafe constructs, and unchecked assumptions.
- Avoid idle loops and unnecessary re-computation; optimize hot paths.
- Evaluate potential performance bottlenecks before finalizing changes.

## 7. Documentation, Tests and History (Update Policy)
- After a feature or design change, update the relevant topic file under `docs/`.
- Update the root `README.md` only when the documentation index or the project summary changes.
- Keep tests/examples updated with working, robust usage samples for every new public capability.
- Keep `AI_HISTORY.md` updated with one short, structured entry per meaningful change.
- Material serialization/deserialization code: XML comments must describe the units of every serialized property and table.
