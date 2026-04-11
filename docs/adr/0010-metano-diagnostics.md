# ADR-0010 — `MetanoDiagnostic` + MS0001–MS0008 codes

**Status:** Accepted
**Date:** 2026-02-28

## Context

Early versions of the transpiler emitted a silent
`/* unsupported: <construct> */` comment whenever the transformer hit a
C# construct it couldn't translate yet. The comment vanished in the
generated output (TypeScript parsers drop comments from the AST), the
consumer's `tsgo` failed with a cascade of downstream errors pointing
at the wrong line, and the user had to reverse-engineer which C#
construct had been responsible.

Two problems with the silent-comment approach:

1. **Silent failures don't teach.** The TS error surfaced far from its
   C# cause, so a user seeing `TS2304: Cannot find name 'foo'` in
   `bar.ts:42` had no way to know the real problem was an
   untransformed `switch` expression in `Bar.cs:17`.
2. **No vocabulary for feature gaps vs. real bugs.** A missing
   lowering and an actual compiler bug both manifested as "the
   generated TS doesn't compile". Without stable error codes, user
   reports couldn't be triaged by pattern matching, and fixes couldn't
   be tracked to their originating diagnostic.

## Decision

Introduce `MetanoDiagnostic` as a first-class type in the core
(`Metano.Compiler`). A diagnostic carries:

- `Severity` — `Warning` or `Error`.
- `Code` — a stable identifier from `DiagnosticCodes` (e.g.
  `MS0001`, `MS0007`). Codes are permanent; a retired diagnostic keeps
  its number and the number is never reused.
- `Message` — human-readable, focused on *what* the user should do
  next.
- `Location` — a Roslyn `Location` pointing at the exact C# source
  span that triggered the diagnostic.

The compilation pipeline threads a `reportDiagnostic: Action<MetanoDiagnostic>`
callback into every transformer. The callback collects diagnostics,
and the CLI driver prints them in Roslyn-style format with colors
(yellow for warnings, red for errors). The presence of any `Error`
severity causes the CLI to exit with code `1`; warnings let the build
finish so the user sees the full list in one run.

Silent `/* unsupported: … */` placeholders were replaced with:

- A warning (usually MS0001) describing the unsupported construct, the
  C# source location, and a suggestion when possible.
- A small placeholder AST node in the generated TS that keeps the
  surrounding code compilable — so the user sees every diagnostic
  from a single run, not just the first one.

The code taxonomy at the time of writing:

| Code     | Category                   | Triggered by                                                            |
| -------- | -------------------------- | ----------------------------------------------------------------------- |
| `MS0001` | `UnsupportedFeature`       | Construct not yet lowered                                               |
| `MS0002` | `UnresolvedType`           | Type reference Roslyn couldn't resolve                                  |
| `MS0003` | `AmbiguousConstruct`       | Two lowerings apply and disambiguation is required                      |
| `MS0004` | `ConflictingAttributes`    | e.g. `[Transpile]` + `[NoTranspile]` on the same symbol                 |
| `MS0005` | `CyclicReference`          | Cyclic import chain detected by `CyclicReferenceDetector`               |
| `MS0006` | `InvalidTemplate`          | A `JsTemplate` failed to expand (placeholder out of range, etc.)        |
| `MS0007` | `CrossPackageResolution`   | Missing `[EmitPackage]` at the consumer or drift in `package.json#name` |
| `MS0008` | `EmitInFileConflict`       | Two namespaces resolved to the same `[EmitInFile]` file name            |

The `MS` prefix is the project's namespace (Metano, successor to
MetaSharp). The registry lives in `DiagnosticCodes.cs` and is the
single place to allocate a new code.

## Consequences

- (+) Users see a real diagnostic at the exact C# source span, with a
  stable code they can search for. `MS0007 metano` leads to the
  `docs/cross-package.md` page.
- (+) Warnings let the build finish. The user sees all the problems
  at once instead of fixing one and re-running to discover the next.
- (+) Cross-project diagnostics like `MS0007` deduplicate per type
  display name — one missing `[EmitPackage]` produces exactly one
  error per unique referenced type, not one per call site.
- (+) The core owns the diagnostic type, so every future target
  inherits the infrastructure with no extra code.
- (+) Stable codes make changelog entries, issue reports, and docs
  deep-linking all clean: "fixed MS0005 false positive when …".
- (−) Adding a new diagnostic means picking the next free code and
  committing to it forever. Mitigated by the central registry making
  allocation a one-line change.
- (−) Warnings can be ignored by users who don't read CLI output. The
  Roslyn-style coloring helps, but ultimately warnings are advisory
  and the user can build through them.

## Alternatives considered

- **Keep the silent `/* unsupported */` placeholders**: rejected
  (described in Context).
- **Throw exceptions on the first unsupported construct**: rejected.
  Aborts after the first problem, forces the user into a fix-and-retry
  loop, and makes it impossible to see related issues together.
- **Use Roslyn's own `Diagnostic` / `DiagnosticDescriptor` types
  directly**: rejected. Couples the diagnostic vocabulary to Roslyn's
  lifecycle and mixes Metano's diagnostics with Roslyn's own in the
  CLI output — users would have to learn two unrelated code sets.

## References

- `src/Metano.Compiler/Diagnostics/MetanoDiagnostic.cs`
- `src/Metano.Compiler/Diagnostics/DiagnosticCodes.cs`
- `src/Metano.Compiler/Diagnostics/MetanoDiagnosticSeverity.cs`
- `tests/Metano.Tests/DiagnosticsTests.cs`
- `tests/Metano.Tests/CyclicReferenceTests.cs` (MS0005)
- `tests/Metano.Tests/CrossPackageImportTests.cs` (MS0007)
- Related: [ADR-0011](0011-emit-package-ssot.md) — MS0007 is how
  `[EmitPackage]` drift is surfaced to the consumer.
