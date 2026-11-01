# Contract: Diagnostics `MS0044`–`MS0054`

**Normative for**: FR-004, FR-014, FR-020, FR-021, FR-024–FR-026, FR-030, FR-031, FR-033, FR-040. This spec PR reserves the codes in [diagnostic-catalog.md](../../001-project-baseline-evolution/baseline/diagnostic-catalog.md); `MS0032`–`MS0037` belong to spec 009 and `MS0038`–`MS0043` to spec 010; the next free code after this block is `MS0055`. Symbolic names become constants in `src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs` when implemented. Each reason needs one test asserting code, severity and location; a warning test also asserts that the output is emitted.

| Code | Name | Severity | Location |
|---|---|---|---|
| `MS0044` | `UnanswerableRuntimeTypeTest` | Error (OD-20) | the `is` / `as` / pattern / case label / dispatch parameter |
| `MS0045` | `ErasedTypeArgumentTest` | Warning | the test |
| `MS0046` | `PlainObjectShapeOverlap` | Warning | the test (once per tested / overlapping pair) |
| `MS0047` | `RuntimeTypeOfPlainObject` | Warning | the `GetType()` call |
| `MS0048` | `UnsupportedReflection` | Error | the member access or `typeof` |
| `MS0049` | `ReifiedParameterOnInteropSurface` | Warning | the member declaration (once) |
| `MS0050` | `TargetSpecificInterop` | Error | the reference |
| `MS0051` | `InvalidInlineCode` | Error | the argument |
| `MS0052` | `UnsupportedDynamicOperation` | Error | the operation |
| `MS0053` | `UnreifiableTypeParameter` | Error | the runtime question (`is T`, `typeof(T)`, …) |
| `MS0054` | `UncheckedCast` | Warning (OD-21 below) | the cast |

## Reasons

**MS0044 — UnanswerableRuntimeTypeTest** (Error). Replaces today's MS0001 warning + placeholder for runtime tests.

1. The tested type is a delegate, a pointer or `void`.
2. The tested type is a non-transpilable class with no runtime class (`[External]` without a constructor, a BCL type outside FR-013 / FR-001 / the temporal set).
3. A `[PlainObject]` for which `TypeGuardBuilder` produces no guard.
4. An interface from a referenced package whose descriptor the package did not emit (OD-6 (b) only).

Example: `'o is Action<int>' cannot be lowered: delegates have no runtime type test in TypeScript (spec 011 FR-021).`

**MS0045 — ErasedTypeArgumentTest** (Warning). The output is `Array.isArray(v)` (arrays) or the `instanceof` + wildcard test (collections).

1. `is` / `as` / pattern / cast against `T[]` or `JsArray<T>` where `T` is not `object`.
2. `is List<X>` (or another runtime collection) where the tested expression's static type cannot guarantee the element descriptor is known and `X` is not `object` (the test treats `Unknown` as a match).

**MS0046 — PlainObjectShapeOverlap** (Warning).

1. Another `[PlainObject]` type assignable to the tested expression's static type covers the tested type (009 FR-019 rule 3 relation). The message names both types, the shared members and that the guard answers structurally.

Example: `'o is UserDto' is structural: 'NamedDto' { Name, Age } also satisfies the shape { Name }, so a NamedDto value tests as UserDto.`

**MS0047 — RuntimeTypeOfPlainObject** (Warning).

1. `GetType()` on a receiver whose static type is `object`, `dynamic`, an interface implemented by a `[PlainObject]` type, or an unconstrained type parameter, when at least one `[PlainObject]` type is reachable. The runtime answer for a plain object is `Object`.

**MS0048 — UnsupportedReflection** (Error).

1. A `System.Type` member outside the supported surface (Edge Cases): `GetMethod`, `GetProperties`, `GetConstructor`, `MakeGenericType`, `Assembly`, `GUID`, …
2. `Activator.CreateInstance`, `Type.GetType(string)`, `typeof(dynamic)`, `typeof(void)`, an open generic definition (`typeof(List<>)`), a pointer type.
3. `GetType()` on a `null` literal.

**MS0049 — ReifiedParameterOnInteropSurface** (Warning).

1. A hidden descriptor parameter is added to a member reachable by JS callers: a public member of an `[EmitPackage]` library, an `[ExportedAsModule]` function, a `[JsxComponentBuilder]` interface member, or a method converted to a `[JsCallable]` delegate. The message lists the parameter and `Unknown` as the wildcard argument.

**MS0050 — TargetSpecificInterop** (Error).

1. `Js.Code` (any `Metano.Interop.Js` member) reached by a non-TypeScript emission.
2. `JsArray<T>` referenced (signature or body) by a non-TypeScript emission.
3. A `Metano.Interop.Dart` member (reserved) reached by a non-Dart emission.

The containing member or type carrying `[Ignore(<that target>)]` suppresses the reason (the member is not emitted there).

**MS0051 — InvalidInlineCode** (Error).

1. The `code` argument is not a constant string expression or an interpolated string with constant literal parts.
2. A hole contains a `ref` / `out` argument, a `dynamic`-bound operation that MS0052 rejects, or an `await`.
3. `Js.Code<T>` with `T` a type parameter that is reified (the cast is `as T`; a reified `T` has no static meaning inside raw text) — reported only when the hole text needs the descriptor.
4. The call is used as an argument to `[Emit]` / `[MapMethod]` templates that print the receiver twice (none exist after FR-009; kept as a guard).

**MS0052 — UnsupportedDynamicOperation** (Error).

1. A collection initializer or object initializer on a `dynamic` receiver.
2. A `ref` / `out` / `in` argument on a dynamically bound call, or a named argument.
3. A dynamically bound `await`, `foreach` over `dynamic` (JS `for..of` works only on iterables; the DLR enumerates `IEnumerable`), `using` on `dynamic`, or a `dynamic` event accessor.
4. A user-defined conversion or operator whose operand is `dynamic` (the DLR would bind it at runtime; TS cannot).

**MS0053 — UnreifiableTypeParameter** (Error).

1. A runtime question about `T` (`is T`, `as T`, a `T` pattern, `typeof(T)`, `new T()`, `default(T)` for an unconstrained `T`) in a body that cannot carry a hidden parameter: an `[Emit]` / `[Import]` extern, an `[External]` interface member, a `[JsCallable]` delegate body, a `[ModuleEntryPoint]`, an operator, an explicit interface implementation of an `[External]` interface, a `[Queryable]` lambda.
2. The same question in a member of an override / implementation group where another member of the group is one of the above.

**MS0054 — UncheckedCast** (Warning; see OD-21).

1. An explicit cast `(T)x` whose target has an erased element (`T[]` / `JsArray<T>` with a non-`object` element) or a runtime collection whose argument cannot be verified; the cast is emitted as `x as unknown as T`.
2. An explicit cast or an implicit `dynamic` conversion whose target has no descriptor (a `None` category that is not an MS0044 error because the cast does not need a test to compile: a delegate type).
3. `(T)x` where `T` is an open, non-reified type parameter at a site that MS0053 does not reject (an unconstrained `T` in a non-reified body: the cast is a no-op).

The message ends with: `Use x.UnsafeCast<T>() to state that the cast is intentionally unchecked.`

**OD-21 — `MS0054` severity** (recorded here because it belongs with the code). (a) Warning; (b) Info; (c) Warning only outside `[EmitPackage]` libraries. **Recommend (a)**: an unchecked cast is a semantic gap the author should acknowledge once by writing `UnsafeCast`, and `TreatWarningsAsErrors` consumers get the strictness they asked for.
