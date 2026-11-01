# ADR-0026 — Own runtime collections and runtime type descriptors

**Status:** Proposed
**Date:** 2026-11-01

## Context

`List<T>` maps to a native JS array, `Dictionary<K,V>` to a native `Map`, and about 40 declarative templates in `src/Metano/Runtime/{Lists,Dictionaries,Queues,Stacks,ImmutableCollections}.cs` encode Array semantics (`Count` → `length`, `RemoveAt` → `splice`, `Clear` → `$this.length = 0`, `Stack.Peek` → `$this[$this.length - 1]` with the receiver printed twice). Only `HashSet<T>` is a runtime class (`targets/js/metano-runtime/src/system/collections/hash-set.ts`). Three costs follow:

- no C# exception is ever thrown: `list[10]`, `dict["missing"]`, `stack.Pop()` on empty all yield `undefined`;
- no runtime identity: `IrRuntimeTypeTest.Of` (`src/Metano.Compiler/IR/IrRuntimeTypeTest.cs`) classifies collections, interfaces without guards, open type parameters and `object` as `None`, so `is List<T>`, `is T`, `typeof(T)` and `GetType()` report MS0001, and `catch (InvalidOperationException e)` is lowered to an untyped `catch (e)` because BCL exceptions are plain `Error`s (`IrToTsStatementBridge.MapTry`, `IrToTsTypeMapper.cs` L109-110);
- IIFEs: where a collection step is a template rather than a method, null-conditional chains and evaluate-once sites render as `TsImmediateInvocation` (`tests/Metano.Tests/Expected/null-conditional-guards.ts`).

The JSON context already has a per-type descriptor (`TypeSpec` / `TypeDescriptor`, `system/json/types.ts`) with thunks and closure-composed converters, but it carries no identity and is JSON-only. Specs 009 (polymorphic JSON) and 010 (C# unions) both depend on "a per-type runtime test" and were written against the capability, not an implementation. Full research is in `specs/011-runtime-collections-and-types/research.md`.

## Decision

Make the BCL collections **Metano's own `metano-runtime` classes** (`List`, `Dictionary`, `HashSet`, `Queue`, `Stack`, `ImmutableList`, `ReadOnlyList`, the dictionary views and the BCL interfaces) that implement `Iterable<T>`, are not `Array` subclasses, throw the .NET exceptions, and carry their type-argument descriptors. Keep `T[]` native and add `JsArray<T>` as the explicit C# name of the native array. Make BCL exceptions runtime classes and lower typed `catch` to one JS `catch` with an `instanceof` chain. Replace every IIFE that served collection semantics by a method or a runtime helper.

Add **runtime type descriptors** (`RuntimeType`, `metano-runtime/system/reflection`): a `kind`-discriminated typed object literal per type, linked by thunks and memoized for closed generics; a symbol-keyed static on classes (so `GetType()` answers the concrete type), a merged-namespace `$type` for interfaces, plain objects and enums. Classes test with `instanceof`, interfaces nominally through the class descriptor's interface list, `[PlainObject]` structurally through the synthesized guard, open `T` through a hidden descriptor parameter added only when the body asks. Descriptors are emitted only for types a runtime question reaches. The JSON `TypeSpec` links to the descriptor instead of growing its own identity.

Three interop escape hatches complete the model: `dynamic` as TS `any` with pass-through operations, `Js.Code` inline target code with a constant string and transpiled holes, and `UnsafeCast<T>()` / `AsDynamic()` as compile-time-only reinterpretations. Everything ships in one 3.0.0 major.

## Consequences

- (+) C# semantics hold where developers rely on them: exceptions, value-equality keys and lookups, nominal `is` / `as` / `catch`, `GetType()` on factory results.
- (+) The mapping layer shrinks to name-only member mappings; no template prints a receiver twice; null-conditional chains become native optional chains.
- (+) One classification table serves patterns, dispatchers, unions, JSON and guards (010 NFR-004), and specs 009 / 010 need no second mechanism.
- (+) Zero cost by construction for code that asks no runtime question; CSP-safe (no `new Function`); tree-shakable modules.
- (−) A JS consumer receives a `List`, not an array; interop needs `Iterable`, `toArray()`, `adopt`, or `JsArray<T>` in signatures. This is the breaking change of 3.0.0.
- (−) `list.get(i)` replaces `list[i]`; a bounds check costs a method call (budgeted by NFR-001 and measured before merge).
- (−) Hidden descriptor parameters change the TS signature of generic members whose bodies ask about `T`; `MS0049` names each interop-visible one.
- (−) Identity is lost for objects created without `new` (`JSON.parse`, spread); `GetType()` falls back to `object` and interface tests to plain-object guards. Documented, not solved.
- (−) `int` / `double` remain indistinguishable at runtime; the static type decides where it can.

## Alternatives considered

- **`class List<T> extends Array<T>`.** `Symbol.species`, a magic `length`, `Array.isArray(list) === true`, no way to throw on `list[i]`, and engine de-optimisation of subclassed arrays. Rejected.
- **Keep native arrays / `Map` and add interop adapters (DllImport / JSImport style) at every boundary.** Adapters copy (hidden O(n)) or proxy (an order of magnitude per access); the boundary is every generated call. Rejected.
- **A Deepkit-style transformer with bytecode, a VM and a `new Function` JIT.** Verified: Deepkit hooks tsc, emits bytecode executed by a `Processor` VM, builds serializers with `new Function` (`@deepkit/core` `CompilerContext.build`), validates classes structurally, and weighs 68.6 KB gzipped (`@deepkit/type` 1.1.0, Bundlephobia). Metano already is the compiler, already knows every type, and must stay CSP-safe and small. Borrowed instead: the typed-object-literal descriptor, thunks, emit-only-what-is-used, closure-composed serializers.
- **Structural `is` for interfaces (guards everywhere).** Matches TS, not C#; `is IFoo` would accept any object with the right members. Kept for `[PlainObject]` and validation only.
- **No descriptors; `instanceof` plus `constructor.name`.** Cannot answer interface tests, generic specializations, `typeof(T)`, or `GetType()` identity across minified bundles. Rejected.

## References

- Spec: `specs/011-runtime-collections-and-types/` (issue #335)
- Code pointers: `src/Metano/Runtime/Lists.cs`, `Dictionaries.cs`, `Queues.cs`, `Stacks.cs`; `src/Metano.Compiler/IR/IrRuntimeTypeTest.cs`; `src/Metano.Compiler.TypeScript/Bridge/IrToTsExpressionBridge.Patterns.cs`, `IrTypeCheckBuilder.cs`, `IrToTsStatementBridge.cs`, `TsImmediateInvocation.cs`; `targets/js/metano-runtime/src/system/collections/hash-set.ts`, `system/json/types.ts`
- Related ADRs: 0003 (declarative BCL mappings), 0008 (overload dispatch), 0009 (type guards), 0012 (LINQ runtime), 0013 (shared IR)
- Related specs / issues: 009-polymorphic-json, 010-csharp-unions, #328, #298, #291, #296
- External: Deepkit runtime types documentation (runtime-types, runtime-types/bytecode), `deepkit-framework/packages/core/src/compiler.ts`, Bundlephobia `@deepkit/type`; Kotlin/JS `js()`, `dynamic`, `asDynamic()`, `unsafeCast()`
