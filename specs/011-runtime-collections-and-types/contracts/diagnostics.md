# Contract: Diagnostics `MS0045`–`MS0056`

**Normative for**: FR-004, FR-014, FR-020, FR-021, FR-024–FR-026, FR-030, FR-031, FR-033, FR-035. This spec PR reserves the codes in [diagnostic-catalog.md](../../001-project-baseline-evolution/baseline/diagnostic-catalog.md); `MS0032`–`MS0037` belong to spec 009, `MS0038`–`MS0043` to spec 010 and `MS0044` to #334 / PR #355; the next free code after this block is `MS0057` (which OD-22 would take if accepted). Symbolic names become constants in `src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs` when implemented. Each reason needs one test asserting code, severity and location; a warning test also asserts that the output is emitted.

| Code | Name | Severity | Location |
|---|---|---|---|
| `MS0045` | `UnanswerableRuntimeTypeTest` | Error (OD-20) | the `is` / `as` / pattern / case label / dispatch parameter / `catch` clause |
| `MS0046` | `ErasedTypeArgumentTest` | Warning | the test |
| `MS0047` | `PlainObjectShapeOverlap` | Warning | the test (once per tested / overlapping pair) |
| `MS0048` | `RuntimeTypeOfPlainObject` | Warning | the `GetType()` call |
| `MS0049` | `UnsupportedReflection` | Error | the member access or `typeof` |
| `MS0050` | `ReifiedParameterOnInteropSurface` | Warning | the member declaration (once) |
| `MS0051` | `TargetSpecificInterop` | Error | the reference |
| `MS0052` | `InvalidInlineCode` | Error | the argument or the hole |
| `MS0053` | `UnsupportedDynamicOperation` | Error | the operation |
| `MS0054` | `UnreifiableTypeParameter` | Error | the runtime question (`is T`, `typeof(T)`, …) |
| `MS0055` | `UncheckedCast` | Warning (OD-21 below) | the cast |
| `MS0056` | `InlineCodeHoleInStringLiteral` | Warning | the hole |

## Reasons

**MS0045 — UnanswerableRuntimeTypeTest** (Error). Replaces today's MS0001 warning + placeholder for runtime tests.

1. The tested type is a delegate, a pointer or `void`.
2. The tested type is a non-transpilable class with no runtime class (`[External]` without a constructor, a BCL type outside FR-013 / FR-001 / the temporal set), including a BCL exception outside FR-013's list at a `catch` / `is` / `as` site (FR-014: no widening to `Exception`).
3. A `[PlainObject]` for which `TypeGuardBuilder` produces no guard.
4. An interface from a referenced package whose descriptor the package did not emit (OD-6 (b) only).

Example: `'o is Action<int>' cannot be lowered: delegates have no runtime type test in TypeScript (spec 011 FR-021).`

**MS0046 — ErasedTypeArgumentTest** (Warning). The output is `asArray(v) !== null` (arrays) or the `instanceof` + wildcard test (collections).

1. `is` / `as` / pattern / cast against `T[]` or `JsArray<T>` where `T` is not `object`.
2. `is List<X>` (or another runtime collection) where the tested expression's static type cannot guarantee that the collection carries `$typeArguments$<Class>` and `X` is not `object` (an absent field or `UnknownType` matches).

**MS0047 — PlainObjectShapeOverlap** (Warning).

1. Another `[PlainObject]` type assignable to the tested expression's static type covers the tested type (009 FR-019 rule 3 relation). The message names both types, the shared members and that the guard answers structurally.

Example: `'o is UserDto' is structural: 'NamedDto' { Name, Age } also satisfies the shape { Name }, so a NamedDto value tests as UserDto.`

**MS0048 — RuntimeTypeOfPlainObject** (Warning).

1. `GetType()` on a receiver whose static type is `object`, `dynamic`, an interface implemented by a `[PlainObject]` type, or an unconstrained type parameter, when at least one `[PlainObject]` type is reachable. The runtime answer for a plain object is `ObjectType`.
2. `GetType()` on a receiver whose static type is a `[PlainObject]` that has derived `[PlainObject]` types (the constant fold of FR-020 does not apply; the runtime answers `ObjectType`).

**MS0049 — UnsupportedReflection** (Error).

1. A `System.Type` member outside the supported surface (Edge Cases): `GetMethod`, `GetProperties`, `GetConstructor`, `MakeGenericType`, `Assembly`, `GUID`, …
2. `Activator.CreateInstance`, `Type.GetType(string)`, `typeof(void)`, an open generic definition (`typeof(List<>)`), a pointer type. (`typeof(dynamic)` is CS1962 in C# and never reaches Metano.)
3. `GetType()` on a `null` literal.

**MS0050 — ReifiedParameterOnInteropSurface** (Warning).

1. A hidden descriptor parameter is added to a member reachable by JS callers: a public member of an `[EmitPackage]` library, an `[ExportedAsModule]` function, a `[JsxComponentBuilder]` interface member, or a method converted to a `[JsCallable]` delegate. The message lists the parameter and `UnknownType` as the wildcard argument.

**MS0051 — TargetSpecificInterop** (Error).

1. `Js.Code` (any `Metano.Interop.Js` member) reached by a non-TypeScript emission.
2. `JsArray<T>` referenced (signature or body) by a non-TypeScript emission.
3. A `Metano.Interop.Dart` member (reserved) reached by a non-Dart emission.

The containing member or type carrying `[Ignore(<that target>)]` suppresses the reason (the member is not emitted there).

**MS0052 — InvalidInlineCode** (Error).

1. The `code` argument is not a constant string expression or an interpolated string with constant literal parts.
2. A hole contains a `ref` / `out` argument, a `dynamic`-bound operation that MS0053 rejects, or an `await`.

**MS0053 — UnsupportedDynamicOperation** (Error).

1. A collection initializer or object initializer on a `dynamic` receiver.
2. A `ref` / `out` / `in` argument on a dynamically bound call, or a named argument.
3. A dynamically bound `await`, `foreach` over a `dynamic` that is not statically known to be iterable, `using` on `dynamic`, or a `dynamic` event accessor.
4. A user-defined operator whose operand is `dynamic`, or an explicit cast `(T)d` where `T` declares a user-defined conversion (the DLR would bind it at runtime; TS cannot).

**MS0054 — UnreifiableTypeParameter** (Error).

1. A runtime question about `T` (`is T`, `as T`, a `T` pattern, `typeof(T)`, `(T)x`, `new T()`) in a body that cannot carry a hidden parameter: an `[Emit]` / `[Import]` extern, an `[External]` interface member, a `[JsCallable]` delegate body, a `[ModuleEntryPoint]`, an operator, an explicit interface implementation of an `[External]` interface, a `[Queryable]` lambda.
2. The same question in a member of an override / implementation group where another member of the group is one of the above, or where the group's interface or base member comes from a **referenced package** whose emitted signature has no hidden parameter.

**MS0055 — UncheckedCast** (Warning; see OD-21).

1. An explicit cast `(T)x` whose target has an erased element (`T[]` / `JsArray<T>` with a non-`object` element) or a runtime collection whose argument cannot be verified; the cast is emitted as `x as unknown as T`.
2. An explicit cast whose target has no descriptor and is not an MS0045 error because the cast does not need a test to compile: a delegate type.
3. `(T)x` where `T` is an open, non-reified type parameter at a site that MS0054 does not reject (an unconstrained `T` in a non-reified body: the cast is a no-op).

Implicit conversions from `dynamic` are **not** a reason (FR-030: they emit nothing and are not diagnosed). The message ends with: `Use x.UnsafeCast<T>() to state that the cast is intentionally unchecked.`

**MS0056 — InlineCodeHoleInStringLiteral** (Warning).

1. A `Js.Code` hole falls inside a JS string literal of the text (`'…{x}…'`, `"…{x}…"`, `` `…{x}…` `` with no `${`): the hole is spliced as code, not as a quoted value. Tested with a single-quoted, a double-quoted and a template-literal case. The output is emitted.

**OD-21 — `MS0055` severity** (recorded here because it belongs with the code). (a) Warning; (b) Info; (c) Warning only outside `[EmitPackage]` libraries. **Recommend (a)**: an unchecked cast is a semantic gap the author should acknowledge once by writing `UnsafeCast`, and `TreatWarningsAsErrors` consumers get the strictness they asked for.
