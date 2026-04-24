# ADR-0015 — Attribute family for compile-time erasure and inlining

**Status:** Proposed
**Date:** 2026-04-23

## Context

Over the course of the `Metano.TypeScript.DOM` bindings effort we ran
into three overlapping pains:

1. `[External]` (introduced in #94) had to do two jobs at once — suppress
   file emission AND flatten call-site access of static members — because
   we lacked a dedicated attribute for "container class that vanishes at
   the call site".
2. `[NoEmit]` was used as an ambient declaration marker (for BCL / DOM
   types that exist in the JS runtime). But callers who want to mark a
   type as ".NET-only, must never cross into transpiled code" had no
   way to express that. The name `NoEmit` fits the second meaning better
   than the first, and today's double-duty makes the painting of
   non-transpilable code ambiguous.
3. `[InlineWrapper]` describes *how* the compiler lowers a struct (inline
   + wrap) rather than *what* lands in TypeScript (a branded primitive).
   As we introduced a sibling attribute for erased wrappers (see the
   closed-catalog follow-up in #96) the name stopped being self-explanatory.

Simultaneously, the DOM bindings use case (`document.createElement(...)` →
`HTMLElementTagNameMap` style narrowing) exposed missing primitives we
could not spell today: a way to enforce literal arguments at call sites
(`[Constant]`), a way to inline field initializers and method bodies at
use sites (`[Inline]`), and the container-erasure `[Erasable]` on top of
which those layer.

`[ExportedAsModule]` also has a latent defect: member access
`MyModule.Foo()` is **not** flattened at call sites (only `[External]`
triggered that pass, per
`src/Metano.Compiler/Extraction/IrExpressionExtractor.cs:938`). The
shipping samples never exercised the broken path because the only
consumers of `[ExportedAsModule]` are entry-point classes called from
hand-written test code. The first cross-module call would emit a
dangling `MyModule.foo` reference.

## Decision

Introduce a six-attribute family that cleanly separates the orthogonal
concerns — scope, declaration, inlining, branding, constants:

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[Erasable]` | `static class` | Class scope vanishes at the call site. Members emit according to their own attributes (plain body → top-level function in class-named file, `[External]` → ambient, `[Emit(...)]` → template, `[Inline]` → expansion, `[Ignore]` → dropped). |
| `[External]` | class or member | Runtime-provided. No declaration emitted. Transpiled code can reference the symbol (class-qualified access preserved when the class is *not* also `[Erasable]`). |
| `[NoEmit]` | type | **Redefined**: .NET-only. The type is painted as non-transpilable; any reference from transpiled code is a compile error (MS0013 `NoEmitReferencedByTranspiledCode`). Previous ambient usages migrate to `[External]`. |
| `[Branded]` | `struct` / `record` | Branded primitive wrapper with a companion namespace. Renames `[InlineWrapper]` — no behavioral change, just a better name. |
| `[Constant]` | parameter or field | Value must be a compile-time constant literal. Validator emits MS0014 `InvalidConstant` when violated. Enables literal-type narrowing in `[Emit]` templates and safe `[Inline]` expansion. |
| `[Inline]` | field, method, or static class | Expansion at the use site. Field: initializer substitutes each access. Expression-bodied method: body substitutes each call with parameter renaming. Static class: propagates `[Inline]` to every member (ergonomic shortcut). |

Each attribute stays single-responsibility. Composability — not
implication — is the contract. `[Erasable]` on a class does *not*
automatically mark its members `[External]`; the user attaches the
second attribute when the member's value is runtime-provided. The
separation matters for the mixed case where a single `[Erasable]` class
holds both runtime-global accessors (`[External]`) and user-authored
helpers (plain bodies → top-level functions).

`[ExportedAsModule]` is marked `[Obsolete]` and points to `[Erasable]`;
one release cycle later it disappears. The move simultaneously fixes
the call-site flatten bug, because `[Erasable]` populates the same
`IsDeclaringTypeErasable` flag in `IrMemberOrigin` and drives the
existing flatten branch in `IrToTsExpressionBridge.MapMemberAccess`.

The class-level semantics of `[External]` (no file + flatten) shipped in
#94 and were consumed only by the in-progress `Js` binding. Going
forward, class-level `[External]` means "ambient class, declaration
elsewhere, access stays class-qualified". The flatten behavior now
lives exclusively under `[Erasable]`.

The four features roll out as stacked PRs:

- PR-0 (#97) — `[Erasable]` + `[External]` refactor + `[NoEmit]` redefinition + `[ExportedAsModule]` deprecation
- PR-1 (#98) — `[Constant]` (parameter + field)
- PR-2 (#99) — `[Branded]` rename of `[InlineWrapper]`
- PR-3 (#100) — `[Inline]` (field + expression-bodied method + static-class propagation)

## Consequences

- (+) Callers of DOM bindings (and similar runtime-global wrappers) can
      write ergonomic C# (`Js.Document.CreateElement(HtmlElementType.Div)`)
      and have it lower to idiomatic TypeScript
      (`document.createElement(({tagName: "div"}).tagName) as HTMLDivElement`)
      without a new special-case in the compiler.
- (+) Closes the latent call-site bug in `[ExportedAsModule]` without a
      bespoke patch.
- (+) `[NoEmit]` finally has a single meaning — ".NET-only, untranspilable"
      — and the diagnostic MS0013 catches stray references early rather
      than letting them escape as dangling symbols in generated TS.
- (+) `[Branded]` vs. a future erased-wrapper (closed-catalog follow-up)
      read as a pair, matching the already-accepted `[Erasable]`
      counterpart on the class side.
- (+) The `[Constant]` + `[Inline]` duo is usable standalone (SQL column
      names, event names, typed literal factories) well beyond the DOM
      motivator.
- (−) Breaking change at the attribute surface: class-level `[External]`
      behavior moves to `[Erasable]`; the `Js` binding in the in-progress
      `feat/jsx` worktree must migrate (`[External, NoEmit]` on `Js` →
      `[Erasable]`, per-type `[NoEmit]` → `[External]`). No shipped
      consumers outside the active experiments.
- (−) `[InlineWrapper]` → `[Branded]` is a mechanical rename, but every
      internal call site (`SymbolHelper.HasInlineWrapper`,
      `IrTypeSemantics.IsInlineWrapper`, the bridge class name) has to
      move with the surface attribute. Coordinated with the existing
      same-week refactor on the `feat/jsx` branch.
- (−) The `[NoEmit]` redefinition requires re-examining every existing
      use in the codebase. The majority are DOM bindings that should
      become `[External]`; a minority are legitimate .NET-only (internal
      test helpers) and remain `[NoEmit]`.

## Alternatives considered

- **Keep `[External]` as class-level flatten + ambient** — the shipped
  #94 behavior. Rejected because the dual role makes it impossible to
  express "ambient class that keeps class-qualified access" (needed
  when the runtime publishes a namespace rather than loose globals).
- **Keep `[InlineWrapper]` name** — avoids churn but names the
  mechanism rather than the outcome, and reads inconsistently next to
  the sibling erased-wrapper attribute.
- **Imply `[External]` on every member of an `[Erasable]` class** —
  Kotlin-`external object`-style. Rejected because it precludes the
  common mixed case where an `[Erasable]` class holds both ambient
  bindings and user-authored helpers.
- **Merge `[Erasable]` and `[ExportedAsModule]` into one attribute with
  two modes** — fewer attributes but muddies the orthogonality users
  benefit from. Rejected.
- **Reinterpret `[NoEmit]` inside the union with `[External]`** —
  single attribute, flag argument. Rejected; flags on an attribute
  imply one responsibility with variants, which is not the shape
  here (separate cross-target vs. target-specific intents).

## References

- Umbrella issue: #96
- Stack: #97, #98, #99, #100
- Prior art: ADR-0005 (Inline wrapper branded types — being renamed),
  #93 (`[Name]` override on `[NoEmit]` types),
  #94 (`[External]` attribute first shipment)
- Code pointers that move under this ADR:
  - `src/Metano/Annotations/TypeScript/ExternalAttribute.cs`
  - `src/Metano/Annotations/ExportedAsModuleAttribute.cs`
  - `src/Metano/Annotations/InlineWrapperAttribute.cs`
  - `src/Metano.Compiler/SymbolHelper.cs` (HasExternal / HasInlineWrapper)
  - `src/Metano.Compiler/Extraction/IrExpressionExtractor.cs:938`
  - `src/Metano.Compiler.TypeScript/Bridge/IrToTsExpressionBridge.cs` (MapMemberAccess)
