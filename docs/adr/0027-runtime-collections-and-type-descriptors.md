# ADR-0027 — Own runtime collections and runtime type descriptors

**Status:** Proposed
**Date:** 2026-11-01

## Context

`List<T>` maps to a native JS array, `Dictionary<K,V>` to a native `Map`, and about 40 declarative templates in `src/Metano/Runtime/{Lists,Dictionaries,Queues,Stacks,ImmutableCollections}.cs` encode Array semantics (`Count` → `length`, `RemoveAt` → `splice`, `Clear` → `$this.length = 0`, `Stack.Peek` → `$this[$this.length - 1]` with the receiver printed twice). Only `HashSet<T>` is a runtime class (`targets/js/metano-runtime/src/system/collections/hash-set.ts`). Three costs follow:

- no C# exception is ever thrown: `list[10]`, `dict["missing"]`, `stack.Pop()` on empty all yield `undefined`;
- no runtime identity: `IrRuntimeTypeTest.Of` (`src/Metano.Compiler/IR/IrRuntimeTypeTest.cs`) classifies collections, interfaces without guards, open type parameters and `object` as `None`, so `is List<T>`, `is T`, `typeof(T)` and `GetType()` report MS0001, and `catch (InvalidOperationException e)` is lowered to an untyped `catch (e)` because BCL exceptions are plain `Error`s (`IrToTsStatementBridge.MapTry`, `IrToTsTypeMapper.cs` L109-110);
- IIFEs: where a collection step is a template rather than a method, null-conditional chains and evaluate-once sites render as `TsImmediateInvocation` (`tests/Metano.Tests/Expected/null-conditional-guards.ts`).

The JSON context already has a per-type descriptor (`TypeSpec` / `TypeDescriptor`, `system/json/types.ts`) with thunks and closure-composed converters, but it carries no identity and is JSON-only. Specs 009 (polymorphic JSON) and 010 (C# unions) both depend on "a per-type runtime test" and were written against the capability, not an implementation.

The first review of the spec measured, with bun, esbuild, Rollup and webpack, that several natural emission shapes defeat tree-shaking: a TS `namespace` with members and an `export enum` are IIFEs every bundler keeps; a class static initialized by a factory call is kept with the whole class, and esbuild keeps any class with a computed-symbol static even under `/*#__PURE__*/`; a module-level `registerX()` under `sideEffects: false` is dropped by every bundler (today's `[StrictUnionGuard]` registration already disappears in bundles). The maintainer made tree-shaking mandatory. Full research is in `specs/011-runtime-collections-and-types/research.md` (R1–R14).

## Decision

Make the BCL collections **Metano's own `metano-runtime` classes** (`List`, `Dictionary`, `HashSet`, `Queue`, `Stack`, `ImmutableList`, `ReadOnlyList`, the dictionary views and the BCL interfaces) that implement `Iterable<T>`, are not `Array` subclasses, throw the .NET exceptions, key with `EqualityComparer<T>.Default` semantics (`equals()` / `hashCode()` when present, reference identity otherwise), use non-enumerable string-keyed storage (no `#private`), implement `toJSON`, and carry their type-argument descriptors only when the compilation asks about that collection kind. Keep `T[]` native and add `JsArray<T>` as the explicit C# name of the native array. Make BCL exceptions runtime classes, one module each, and lower typed `catch` to one `catch` with an `instanceof` chain in TS and an `is` chain in Dart. Replace every IIFE that served collection semantics by a method or a runtime helper.

Add **runtime type descriptors** (`RuntimeType`, `metano-runtime/system/reflection`): a `kind`-discriminated typed object literal per type whose `test` is self-contained and duck-typed, linked by thunks and memoized for closed generics. On classes the descriptor is a **string-keyed, lazily memoized static getter** (`static get $runtimeType()`) that returns a **data literal** written out by the compiler (no runtime import, so a used class pulls nothing from `system/reflection`), read by `getType` through `Object.hasOwn` so `GetType()` answers the concrete type; interfaces, plain objects, enums and unions get a separate exported literal (`X$runtimeType`), never a merged namespace; assignability is a reflection function over the data. Classes test with `instanceof`, interfaces nominally through the class descriptor's interface list (with a plain-object fallback through declared `[PlainObject]` implementers), `[PlainObject]` structurally through the synthesized guard, open `T` through a hidden descriptor parameter added only when the body asks. Descriptors are emitted only for types a runtime question reaches. The JSON `TypeSpec` links to the descriptor instead of growing its own identity; the `[StrictUnionGuard]` registry is replaced by a strict guard emitted in its own module that imports the base and the variants, so the base never imports a variant and the ESM cycle that ADR-0009's registry avoided does not return.

Tree-shaking is a requirement with a CI gate: no call in a top-level statement or initializer without `/*#__PURE__*/`, a truthful `sideEffects` (the list of entry-point modules, not a flat `false`), per-symbol gzip budgets, "unused means 0 bytes", measured with bun and esbuild (and Rollup for side-effect-only imports, bundled from a copy in a directory with its own `package.json`, third-party packages external, with a canary that must be kept so the check cannot be vacuous) on every PR, plus a bundle check of every tsgo-compiled golden ("nothing evaluated at module load") with `[ModuleEntryPoint]` exempt and a shrinking allowlist for the pre-existing namespace emissions (#361), which are part of the 3.0.0 release gate. Numeric enums are emitted as `const` objects (an optional brand over `number` for `[Flags]`).

Three interop escape hatches complete the model: `dynamic` as TS `any` with pass-through operations and unchecked implicit conversions, `Js.Code` inline target code with a constant string and transpiled holes, and `UnsafeCast<T>()` / `AsDynamic()` as compile-time-only reinterpretations; explicit casts stay checked wherever a descriptor can answer. Everything ships in one 3.0.0 major.

## Consequences

- (+) C# semantics hold where developers rely on them: exceptions, `EqualityComparer<T>.Default` keys and lookups, nominal `is` / `as` / `catch`, `GetType()` on factory results.
- (+) The mapping layer shrinks to name-only member mappings; no template prints a receiver twice; null-conditional chains become native optional chains.
- (+) One classification table serves patterns, dispatchers, unions, JSON and guards (010 NFR-004), and specs 009 / 010 need no second mechanism.
- (+) Zero cost by construction *and by measurement* for code that asks no runtime question: a lazy getter is shaken with its class, a pure export with its module; CSP-safe (no `new Function`).
- (+) The strict-union dispatch that bundlers drop today survives bundling.
- (−) A JS consumer receives a `List`, not an array; interop needs `Iterable`, `toJSON`, `toArray()`, `adopt`, or `JsArray<T>` in signatures. This is the breaking change of 3.0.0.
- (−) `list.get(i)` replaces `list[i]`; a bounds check costs a method call (budgeted by NFR-001 and measured before merge).
- (−) Hidden descriptor parameters change the TS signature of generic members whose bodies ask about `T`; `MS0050` names each interop-visible one.
- (−) Identity is lost for objects created without `new` (`JSON.parse`, spread); `GetType()` falls back and interface tests fall back to plain-object guards. Duplicated package copies break `instanceof` and descriptor identity. Documented, not solved.
- (−) `int` / `double` remain indistinguishable at runtime; the static type decides where it can.
- (−) The pre-existing namespace emissions (branded companions, nested types, `UUID`) and spec 010's union companions must move to flat exports (#361) to meet the same bar, and the generated `sideEffects: false` must become the entry-point list (#368); both gate 3.0.0.

## Alternatives considered

- **`class List<T> extends Array<T>`.** `Symbol.species`, a magic `length`, `Array.isArray(list) === true`, no way to throw on `list[i]`, and engine de-optimisation of subclassed arrays. Rejected.
- **Keep native arrays / `Map` and add interop adapters (DllImport / JSImport style) at every boundary.** Adapters copy (hidden O(n)) or proxy (an order of magnitude per access); the boundary is every generated call. Rejected.
- **A Deepkit-style transformer with bytecode, a VM and a `new Function` JIT.** Verified: Deepkit hooks tsc, emits bytecode executed by a `Processor` VM, builds serializers with `new Function` (`@deepkit/core` `CompilerContext.build`), validates classes structurally, and weighs 68.6 KB gzipped (`@deepkit/type` 1.1.0, Bundlephobia). Metano already is the compiler, already knows every type, and must stay CSP-safe and small. Borrowed instead: the typed-object-literal descriptor, thunks, emit-only-what-is-used, closure-composed serializers.
- **Symbol-keyed static descriptors (`static [runtimeType] = classType(...)`) and merged `namespace X { export const $type }`.** The first draft of this spec. Measured: esbuild keeps every class with a computed-symbol static, and a namespace with members is an IIFE that no bundler drops; `$type` also collides with 009's wire discriminator. Replaced by the string-keyed lazy getter and separate exports.
- **A lazy getter that calls a `classType` factory.** The second draft. Measured: a used class keeps its getter body, so the factory pulls `system/reflection` into every program that uses the class (one thrown exception: 0.58 KB gz vs 0.35 KB with a literal). Replaced by compiler-written data literals; assignability became a function over the data.
- **A runtime registry for strict union guards (ADR-0009's `UnionGuardRegistry`).** A module-level registration is a side effect that `sideEffects: false` lets bundlers drop. Replaced by a guard module that imports base and variants; a variant table inside the base's own module (the third draft) was measured to recreate the ESM cycle (`ReferenceError` in Bun, `superclass is not a constructor` in esbuild) and rejected.
- **Structural `equals` (`system/equality`) for collection keys.** Merges two distinct instances of a class with equal fields; .NET's `EqualityComparer<T>.Default` does not. Replaced by `defaultEquals` / `defaultHashCode`.
- **Structural `is` for interfaces (guards everywhere).** Matches TS, not C#; `is IFoo` would accept any object with the right members. Kept for `[PlainObject]` and validation only.
- **No descriptors; `instanceof` plus `constructor.name`.** Cannot answer interface tests, generic specializations, `typeof(T)`, or `GetType()` identity across minified bundles. Rejected.

## References

- Spec: `specs/011-runtime-collections-and-types/` (issue #335, PR #349); follow-up #361 (flat exports for existing namespace emissions)
- Code pointers: `src/Metano/Runtime/Lists.cs`, `Dictionaries.cs`, `Queues.cs`, `Stacks.cs`; `src/Metano.Compiler/IR/IrRuntimeTypeTest.cs`; `src/Metano.Compiler.TypeScript/Bridge/IrToTsExpressionBridge.Patterns.cs`, `IrTypeCheckBuilder.cs`, `IrToTsStatementBridge.cs`, `TsImmediateInvocation.cs`; `src/Metano.Compiler.TypeScript/Transformation/TypeGuardBuilder.cs`; `targets/js/metano-runtime/src/system/collections/hash-set.ts`, `system/equality.ts`, `system/record-copy.ts`, `system/json/types.ts`, `system/json/union-guard-registry.ts`
- Related ADRs: 0003 (declarative BCL mappings), 0008 (overload dispatch), 0009 (type guards; its registry part is superseded here), 0012 (LINQ runtime), 0013 (shared IR)
- Related specs / issues: 009-polymorphic-json, 010-csharp-unions, #328, #298, #291, #296, #334 (takes `MS0044`), #361 and #368 (3.0.0 release gate), #369 (`[Flags]` JSON converter)
- External: Deepkit runtime types documentation (runtime-types, runtime-types/bytecode), `deepkit-framework/packages/core/src/compiler.ts`, Bundlephobia `@deepkit/type`; Kotlin/JS `js()`, `dynamic`, `asDynamic()`, `unsafeCast()`; the bundler measurements of research R13
