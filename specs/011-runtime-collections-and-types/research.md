# Research: BCL collections as runtime types, runtime type descriptors

Evidence tags: **[V]** = verified by reading the cited file on `main` at `5012eb57` (2026-11-01), by fetching the cited external source on 2026-11-01, or by the critic's bundler measurements (R13); **[I]** = inferred from code or docs, not executed; **[M]** = maintainer decision, recorded in the issue, the brief or the first critic pass.

Sources: the repository at `5012eb57`; the Deepkit documentation and source (R10); the maintainer brief for issue [#335](https://github.com/danfma/metano/issues/335), including its three amendments (`dynamic` support, the inline target-code function, and the `UnsafeCast` / `AsDynamic` escape hatches); the first critic review of PR #349 (tree-shaking measurements, R13; equality, R14).

## R1 — How collections are mapped today

**Type mapping (C# → IR)** [V], `src/Metano.Compiler/Extraction/IrTypeRefMapper.cs`, checked in this order:

| Lines | C# | IR |
|---|---|---|
| 147-149 | `Dictionary<K,V>.KeyCollection` / `.ValueCollection` | `IrArrayTypeRef` (snapshot copy) |
| 152-156 | `Dictionary`, `IDictionary`, `IReadOnlyDictionary`, `ConcurrentDictionary` (`RoslynTypeQueries.IsDictionaryLike`, L44-51) | `IrMapTypeRef` |
| 159-160 | `HashSet`, `ISet`, `SortedSet`, `ImmutableHashSet` (`IsSetLike`, L83-90) | `IrSetTypeRef` |
| 163-170 | `KeyValuePair<K,V>` | `IrKeyValuePairTypeRef` |
| 204-215 | `IEnumerable<T>`, `IReadOnlyCollection<T>`, `IQueryable<T>`, `IOrderedEnumerable<T>` | `IrIterableTypeRef` |
| 229-230 | `List`, `IList`, `IReadOnlyList`, `ICollection`, `ImmutableList`, `ImmutableArray`, `Queue`, `Stack` (`IsCollectionLike`, L99-110) | `IrArrayTypeRef` |
| 302-303 | `T[]` | `IrArrayTypeRef` |

The predicates are `ToDisplayString().StartsWith(...)` string tests [V].

**IR → TS** [V], `src/Metano.Compiler.TypeScript/Bridge/IrToTsTypeMapper.cs` `MapCore` L16-53: `IrArrayTypeRef` → `T[]` (L21), `IrMapTypeRef` → native `Map<K,V>` (L22-25), `IrSetTypeRef` → the runtime `HashSet<T>` class (L26), `IrIterableTypeRef` → `Iterable<T>` (L38), `IrKeyValuePairTypeRef` → the tuple `[K, V]` (L40-43). So today `List<T>`, `IList<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `Queue<T>`, `Stack<T>`, `ImmutableList<T>` and `ImmutableArray<T>` are all a native JS array, and `IReadOnlyCollection<T>` is an `Iterable<T>` that has no `length`, although `Lists.cs` L52-57 maps `IReadOnlyCollection.Count` → `length` [V].

**Member mappings** [V], `src/Metano/Runtime/`:

| File | Entries that depend on Array / Map semantics |
|---|---|
| `Lists.cs` (199 lines) | `Count` → `length` (L28-57); `Add` → `push` (L64-66); `AddRange` → `$this.push(...$0)` (L72); `Contains` → `includes` (L79-85, SameValueZero, so records compare by reference); `IndexOf` → `indexOf`; `Insert` → `$this.splice($0, 0, $1)` (L101-110); `Clear` → **`$this.length = 0`**, an assignment expression (L116-122); `Remove` → `listRemove($this, $0)` (L139-144); `RemoveAt` → `$this.splice($0, 1)` (L150-159); `Find` → `($this.find($0) ?? null)` (L168-172); constructors `[] as Array<T>` / `[...src]` (L182-199). |
| `Dictionaries.cs` (266 lines) | `Count` → `size` (L25-42); `Keys` / `Values` → **`[...$this.keys()]`** snapshot copies (L55-84); `ContainsKey` → `has`; `Add` → `addEntry($this,$0,$1)` (L132-143, throws .NET's message on a duplicate key); `Remove` → `delete`; `TryGetValue` → `tryGetValue(...)` (L174-197); `KeyValuePair` ctor → `[$0, $1] as [$T0, $T1]`, `.Key` → `$this[0]` (L251-266). The indexer is lowered by the IR bridge: reads become `.get(k)` (`IrToTsExpressionBridge.cs` L281-289), which yields `undefined` on a miss; there is no `KeyNotFoundException`. |
| `Sets.cs` (66 lines) | `Count` → `size`, `Add` → `add` (the runtime returns `this`, C# returns `bool`), `Contains` → `has`, `Remove` → `delete`, `Clear` → `clear`; no set algebra. |
| `Queues.cs` (15 lines) | `Enqueue` → `push`; `Dequeue` → **`shift`** (O(n), `undefined` on empty); `Peek` → `$this[0]`; `Clear` → `$this.length = 0`. |
| `Stacks.cs` (18 lines) | `Pop` → `pop` (`undefined` on empty); `Peek` → **`$this[$this.length - 1]`**: `$this` appears twice, and `Printer.PrintTemplate` (`TypeScript/Printer.cs` L1506-1576) prints the receiver at each `$this` with no evaluate-once guard, so `GetStack().Peek()` calls `GetStack()` twice. |
| `ImmutableCollections.cs` (146 lines) | `Add` / `Insert` / `RemoveAt` / `Remove` → `ImmutableCollection.<fn>($this, …)` (new arrays); `Clear` → `ImmutableCollection.clear()` with **no `RuntimeImports`** (L55-59, L120-124). |
| `Enumerators.cs` (45 lines) | `GetEnumerator` → `$this[Symbol.iterator]()` for `IEnumerable`, `List`, `HashSet`, `Dictionary`, `Queue`; `Stack` is intentionally unmapped because C# enumerates it top-first (L13-14). |
| `Linq.cs` | `ToList` → `ToArray` ("TS uses arrays for both", `IrLinqMapping.cs` L22+). |

**Runtime** [V], `targets/js/metano-runtime/src/system/collections/`: `hash-set.ts` (158 lines) is the only collection class: `class HashSet<T> implements Iterable<T>` with `Map<number, T[]>` buckets, a lazy native `Set` for primitives (L100-107), duck-typed `hashCode()` / `equals()` (L134-157), `size` / `add` / `has` / `delete` / `clear` / `values` / `toArray`. `dictionary-helpers.ts` (`getValueOrDefault`, `tryGetValue`, `addEntry`), `list-helpers.ts` (`listRemove` by `indexOf`), `immutable-collection.ts` (a namespace of array-copying functions). No `List`, `Dictionary`, `Queue`, `Stack`, `KeyValuePair` or `IEquatable` exists in the runtime. `equality.ts` `equals` (L35) recognises `Array`, `Map`, `Set`, `Date` and plain objects, but not `HashSet`, so two `HashSet`s fall through to a structural compare of private fields [V]. The LINQ pipe (`system/linq/linq.ts`, `linq(source: Iterable<Src>, ...ops)`) accepts any `Iterable<T>` and its terminals are `toArray` → `[...source]`, `toMap` → native `Map`, `toSet` → `new HashSet(source)` [V].

**Package exports** [V], `targets/js/metano-runtime/package.json`: `.`, `./system/json`, `./system/linq`, `./package.json`, `./*.js`, `./*`. There is no explicit `./system/collections` entry; `HashSet` and the helpers are imported from the root (`IrRuntimeRequirementToTsImport.cs` L70-95; `ImportCollector.cs` L187-198).

**Construction** [V]: `MapNewTarget` (`IrToTsExpressionBridge.cs` L1296-1314) hard-codes `IrMapTypeRef` → `Map` and `IrSetTypeRef` → `HashSet`; `MapDeclarativeConstruction` (L1418-1440) applies the `.ctor` templates; a constructor template that drops a side-effecting argument (a capacity) is wrapped in an IIFE by `BindSideEffectsInSourceOrder` (L1462-1488). Collection initializers (`IrExpressionExtractor.ObjectInitializers.cs` `ApplyInitializer` L29-51) produce `IrLetExpression(temp, creation, IrSequenceExpression(effects, temp))`, rendered as an IIFE (`tests/Metano.Tests/Expected/object-initializers.ts` L34-38). A collection expression typed as a set builds `new IrNewExpression(IrSetTypeRef, elements.Select(e => new IrArgument(e)))` (`IrExpressionExtractor.cs` L711-724), passing each element as a separate constructor argument, while the runtime constructor takes one `Iterable`; only the empty case is covered by a test (`CollectionInitTests.cs` L5-24) [V].

**Conclusion**: the mapping layer encodes Array semantics in about 40 templates spread over seven files, three of which produce composite expressions (`Clear` as an assignment, `Find` with `?? null`, `Peek` with a double receiver) and two of which silently return `undefined` where C# throws (`Dequeue`, `Pop`, the dictionary indexer, `list[i]` out of range).

## R2 — The evaluate-once machinery

[V] IR: `IrLetExpression(Name, Value, Body)` (`src/Metano.Compiler/IR/IrExpression.cs` L619-620) and `IrSequenceExpression(Effects, Result)` (L633-634). Creator: `BindReceiverOnce` / `BindValueOnce` / `BindOnce` (`IrExpressionExtractor.cs` L638-678), which reuse a receiver when `IrEvaluationTraits.IsReusableReceiver` holds and otherwise let-bind it.

[V] Sites that create a let: index-from-end `x[^n]` and ranges (`IrExpressionExtractor.IndexAndRange.cs` L127-156, L164-213); object and collection initializers (`ObjectInitializers.cs` L47, L203); extension-property / extension-indexer compound assignment and increments (`IrExpressionExtractor.cs` L2994, L3082, L3193, L3257); integer-division compound assignment (`IntegerDivision.cs` L95-108); lifted nullable operators (`LiftedOperators.cs` L286-342); `GetValueOrDefault` with an impure default (`NullableValueMembers.cs` L118-135); `as` casts with a non-stable operand (`IrExpressionExtractor.cs` L1494-1506); BCL exception argument reordering (L3885, L3917-3931); tuple equality (`Equality.cs` L148, L276-285).

[V] TS rendering: `TsImmediateInvocation.Of(bindings, body, isAsync)` (`src/Metano.Compiler.TypeScript/Bridge/TsImmediateInvocation.cs`, 42 lines) renders `((n1, n2) => { body })(v1, v2)`. Callers: `MapLetExpression` (`IrToTsExpressionBridge.cs` L1147-1160, with a `[PlainObject]`-initializer fold at L1171-1210), `MapSequence` (L1233-1242), `BindSideEffectsInSourceOrder` (L1462-1488), `MapNullConditionalAccess` (`IrToTsExpressionBridge.NullConditional.cs` L50-75, binding `$receiver` because a step is a template or helper such as `listRemove`), switch expressions (`SwitchExpression.cs` ~L132-140), `is` patterns with an impure subject (`Patterns.cs` L43), case guards that read designators (`PatternBindings.cs` L228).

[V] Emitted shapes (`tests/Metano.Tests/Expected/null-conditional-guards.ts` L9-13, L54-60):

```ts
if (order != null) { order.items.length = 0; }
return (($receiver) => $receiver == null ? null : listRemove($receiver.items, item))(order?.next);
```

**Conclusion**: most null-conditional IIFEs exist because a collection step is a *template or helper*, not a method. Once `Clear` and `Remove` are methods of a runtime class, `order?.items.clear()` is a native optional chain and the IIFE disappears without any new machinery. The remaining let sites that this spec covers (`[^n]`, `as`, initializers, constructor templates) are replaced by helpers or by factory methods; switch-expression and pattern-binding IIFEs are a different concern and stay out of scope (FR-009).

## R3 — Runtime type tests today

[V] One classifier, `IrRuntimeTypeTest.Of` (`src/Metano.Compiler/IR/IrRuntimeTypeTest.cs` L44-100): `Instanceof` (classes, records, structs, exceptions, temporal primitives, and any named type with `null` semantics), `Typeof(IrPrimitive)` (primitives, numeric and string enums, branded types), `Guard` (an interface or `[PlainObject]` with `HasGeneratedGuard`), `ArrayTuple(arity)` (`[JsTuple]`), `None` (arrays and `List`, maps, sets, `Iterable`, tuples, type parameters, delegates, `object`, interfaces and plain objects without a guard).

[V] Extraction gate: `HasRuntimeTypeTest` (`IrExpressionExtractor.cs` L1544-1546) skips the gate for Dart (`_target is not (null or TypeScript)`); on TS a `None` test reports **MS0001** (warning) and emits a placeholder (`ReportUnsupportedPattern` L843-848; `MapUnsupported` renders `(() => { throw new Error("Unsupported expression: …"); })()`, `IrToTsExpressionBridge.cs` L322-335). Tests in `tests/Metano.Tests/AsCastTranspileTests.cs` pin MS0001 for an unguarded interface (L113-123), `as T` with a class constraint (L126-143), a `[PlainObject]` without a guard (L146-154), `Person[]` (L176-183), `List<string>` (L186-195).

[V] TS rendering: `BuildTypeTest` (`Bridge/IrToTsExpressionBridge.Patterns.cs` L226-240) renders `Typeof` as `typeof v === "<kind>"` where `IrTypeCheckBuilder.TypeofKind` (L106-122) maps every numeric primitive to `"number"` (comment: "a typeof test cannot tell int from double"); `Guard` as `is<Name>(v)`; `Instanceof` as `v instanceof <mapped name>`, where a BCL exception maps to `Error` (`IrToTsTypeMapper.cs` L109-110, L142-143), so `is InvalidOperationException` renders `instanceof Error` (`BclExceptionConstructionTests.cs` L493-509, "the test cannot tell BCL exception types apart"). `x as T` renders `test ? x : null` through a let when `x` is not stable.

[V] A **second decision table** exists for overload and constructor dispatch: `IrTypeCheckBuilder.Generate` (L23-58) calls the validating runtime helpers (`isInt32`, `isString`, …, `targets/js/metano-runtime/src/type-checking/primitive-type-checks.ts`, 98 lines), `Array.isArray`, `instanceof Map`, `instanceof HashSet`, `typeof === "object"` for interfaces and iterables. Spec 010 NFR-004 already requires that this table be routed through the single classification; this spec finishes that (FR-021).

[V] `typeof(T)`: `TypeOfExpressionSyntax` has no arm in `ExtractUnbound` (L184-246) and reports MS0001. `GetType()`: no mapping in `src/Metano/Runtime/Objects.cs` (only `GetHashCode`, `Equals`, `ReferenceEquals`, L19-31) and no extractor special case. Typed `catch`: `ExtractCatch` (`IrStatementExtractor.cs` L303-317) records `IrCatchClause.ExceptionType`, and `MapTry` (`IrToTsStatementBridge.cs` L180-206) **drops it**: `catch (InvalidOperationException e)` becomes `catch (e)` with no filter (`BclExceptionConstructionTests.cs` L111-135), two catch clauses are concatenated into invalid TS, and `when` filters are never read. The Dart printer emits `on T catch (e)` (`IrBodyPrinter.cs` L170-181).

[V] Exceptions: `src/Metano/Runtime/Exceptions.cs` (23 lines) maps only `InnerException` → `cause` and `StackTrace` → `stack`; `throw new InvalidOperationException("x")` emits `throw new Error("x")` (`BclExceptionConstructionTests.cs` L79-86) through `IrExceptionConstructionExtractor` (role-classified arguments, `DefaultMessages` for 10+ BCL exceptions, L29-60+) and `IrToTsExceptionConstructionBridge`. No `extends Error` class exists in the runtime.

## R4 — Guards and the `UnionGuardRegistry`

[V] `[GenerateGuard]` (`src/Metano/Annotations/GenerateGuardAttribute.cs`) is implemented by `TypeGuardBuilder` (`src/Metano.Compiler.TypeScript/Transformation/TypeGuardBuilder.cs`, 793 lines, Roslyn-symbol based). `GenerateShapeGuard` (L471-587) emits an `instanceof` fast path for classes, then a field-by-field shape walk; a cross-package field type gets only `f != null` (follow-up noted in source). `[StrictUnionGuard]` (`Annotations/TypeScript/StrictUnionGuardAttribute.cs`) dispatches to per-variant guards through the runtime `UnionGuardRegistry` (`targets/js/metano-runtime/src/system/json/union-guard-registry.ts` L16-39: a global `Map<string, guard>` with `registerUnionGuard` / `getUnionGuard`), registered by `TryBuildVariantRegistration` (L316-342). ADR-0009 records the design.

**Conclusion**: guards are *shape validation* for trust boundaries. They are the right tool for `[PlainObject]` tests, which have no identity, and the wrong tool for `is IFoo` on class instances, which C# answers nominally. This spec keeps guards for plain objects and validation and adds nominal interface tests through descriptors (FR-019).

## R5 — The JSON `TypeSpec` and `TypeDescriptor`

[V] `targets/js/metano-runtime/src/system/json/types.ts` (139 lines): `TypeDescriptor` is a closed union on `kind` (`primitive`, `ref`, `temporal`, `decimal`, `map`, `array`, `hashSet`, `branded`, `enum`, `numericEnum`, `nullable`), `RefDescriptor.spec: () => TypeSpec<T>` is a **thunk** (L7-10), `TypeSpec<T>` has `type?` (the constructor, absent for `[PlainObject]`), `base?` (eager, not a thunk), `factory`, `properties`. Spec 009 adds `polymorphism`, `valueType`, `readOptions`, `ValueSpec<T>`, `JsonException` and `NotSupportedException`. The generator (`JsonSerializerContextTransformer.cs`, 967 lines) emits one lazy getter per reachable type with `this._x ??= this.createSpec({...})` and `spec: () => this.<getter>` for references (`tests/Metano.Tests/Expected/json-context-closure.ts`). Converters are closures (`converters.ts`), resolved per property visit; there is no code generation at runtime.

**Conclusion**: the JSON layer already is "a per-type descriptor as a typed object literal with thunks and closure-composed converters". What it lacks is *identity*: `TypeSpec.type` is the constructor, so dispatch is by constructor (009 FR-005 `byCtor`) and `[PlainObject]` variants need per-variant `test` guards (009 FR-019). The general `RuntimeType` of this spec supplies both the identity and the test, and `TypeSpec` links to it (FR-028).

## R6 — `dynamic`, `[Emit]`, `[Import]`, target scoping

[V] `IDynamicTypeSymbol` has no arm in `IrTypeRefMapper.Map`; it falls to `return new IrUnknownTypeRef()` (L305), which the TS mapper renders as `any` (`IrToTsTypeMapper.cs` L50). A collection initializer on a `dynamic` receiver is reported unsupported (`ObjectInitializers.cs` L216-236). [I] Member access and invocation on a `dynamic` receiver bind to no symbol (`GetSymbolInfo` returns null), so the extractor takes its symbol-less paths; what each path emits is unverified and is the first probe of stage 011e (plan.md). `tests/Metano.Tests/TranspileHelper.cs` does not reference `Microsoft.CSharp.dll`, which Roslyn needs to bind dynamic operations [I].

[V] `[Emit("…$0…")]` (`EmitAttribute.cs`) is lowered by `EmitOrImportRewriter` (`src/Metano.Compiler/Extraction/Invocation/EmitOrImportRewriter.cs`) into `IrTemplateExpression(Template, Receiver, Arguments, RequiredImports, TypeArguments, ExternalImports)` (`IrExpression.cs` L763-770); a coexisting `[Import]` becomes `ExternalImports`, collected by `ImportCollector.cs` L542-560. `[MapMethod]` (`MapMethodAttribute.cs`) already carries per-target members: `JsMethod` / `JsTemplate` / `RuntimeImports` and `DartMethod` / `DartTemplate` / `DartRuntimeImports`.

[V] Target scoping: `TargetLanguage { TypeScript, Dart }` (`Annotations/TargetLanguage.cs`); `[Ignore(TargetLanguage)]` (`IgnoreAttribute.cs`, resolution rules in its doc comment) read by `SymbolHelper.HasIgnore(symbol, target)` (`SymbolHelper.cs` L255); TS-only attributes live in `Metano.Annotations.TypeScript` (`Discriminator`, `External`, `JsCallable`, `JsTuple`, `JsxComponentBuilder`, `Optional`, `StrictUnionGuard`).

## R7 — Dart semantics relevant to this spec

[V] from the Dart language specification and `dart:core` documentation (not probed in this repo): Dart is soundly typed, so `x as T` always checks at runtime and throws `TypeError` on mismatch; there is no unchecked reinterpretation. `dynamic` disables static checking and dispatches through `noSuchMethod` at runtime. `is` is nominal and generics are reified (`x is List<int>` is answered exactly). `List` / `Map` / `Set` throw `RangeError` / return `null` on a missing key; `dart:collection` has `Queue` but no `Stack`. Typed catch is `on T catch (e)`, which the Dart printer already emits (R3). [V] The Dart type mapper maps `IrArrayTypeRef` → `List`, `IrMapTypeRef` → `Map`, `IrSetTypeRef` → `Set`, `IrIterableTypeRef` → `Iterable` (`src/Metano.Compiler.Dart/Bridge/IrToDartTypeMapper.cs` L26-40).

**Conclusion**: the Dart target needs the *IR* of this spec (categories, typed catch, the `UnsafeCast` / `AsDynamic` nodes) and almost none of its runtime machinery; its implications are listed in FR-035.

## R8 — Why not `Array` subclasses, wrappers with adapters, or a transformer

Evaluated for ADR-0027:

- **`class List<T> extends Array<T>`.** Species semantics (`map` / `filter` return a `List` through `Symbol.species`), `length` is a magic property that cannot be bounds-checked, `list[i]` cannot throw, `Array.isArray(list)` is `true` so every array test misclassifies, V8 de-optimises subclassed arrays with extra own properties, and JSON / spread / `Array.from` already work for any iterable, so the subclass buys nothing that `Iterable<T>` does not. Rejected [I, well documented in V8 and TC39 discussions; no probe].
- **JS wrappers with interop adapters (DllImport / JSImport style).** Every boundary crossing needs an adapter that copies or proxies. `Proxy` costs an order of magnitude per access and defeats inlining; copying hides O(n) at call sites. Rejected [I].
- **A Deepkit-style transformer.** See R10: a tsc transformer, a bytecode VM and `new Function` JIT are the wrong size and the wrong trust model (CSP) for generated code whose compiler already knows every type. Rejected [V].
- **Own runtime classes plus descriptors** (chosen): a `List<T>` is one object around one native array; `get(i)` is a monomorphic method call that V8 inlines; `instanceof List` answers `is List<T>` nominally; descriptors are emitted only where a runtime question is asked.

## R9 — Primitives at runtime

[V] JS has one `number`; `isInt32` (`primitive-type-checks.ts` L42) is `Number.isInteger(v) && v >= -2^31 && v <= 2^31-1`. So `1.0` boxed as `double` in C# is `1` in JS and passes `isInt32`. The limit is inherent: where the static type is known the compiler decides (C# itself warns CS0183 / CS0184 for constant results); where it is `object`, `dynamic` or an erased `T`, an `Int32` guard is the best answer and `object o = 1.0; o is int` returns `true` in TS and `false` in C# (FR-022, documented divergence).

## R10 — Deepkit lessons

Facts verified on 2026-11-01:

| Fact | Evidence |
|---|---|
| Deepkit embeds type information "using a custom bytecode and virtual machine" through a hook into "TypeScript's compilation process" | [V] https://deepkit.io/documentation/runtime-types (introduction) |
| The transformer emits bytecode as JS string arrays: "For each explicitly defined type that is exported or used by an exported type, a bytecode is emitted" (`__Ω`-prefixed constants; classes and functions carry it as a property); each `ReflectionOp` is one byte; the `Processor` class "is responsible for decoding and executing the encoded bytecode" | [V] https://deepkit.io/documentation/runtime-types/bytecode |
| Serializers are JIT-compiled from source strings: `CompilerContext.build` does `return new Function(...this.context.keys(), functionCode)(...this.context.values())`; `serializer.ts` assembles code through `compiler.build(code, 'data', 'state')` and caches it in a `JitStack` | [V] https://github.com/deepkit/deepkit-framework/blob/master/packages/core/src/compiler.ts and `packages/type/src/serializer.ts` |
| `is<T>` for a class validates **structurally**: `typeGuardObjectLiteral(type: TypeObjectLiteral \| TypeClass, …)` walks `ReflectionClass.from(type)` members behind the gate `accessor.constructor === Object \|\| isObjectLiteral(accessor)`; no `instanceof` check | [V] `packages/type/src/serializer.ts` (first 100 KB of the file) |
| Size: `@deepkit/type` 1.1.0 is 266,285 B minified and 68,612 B minified+gzipped (≈ 67 KB gz) | [V] https://bundlephobia.com/api/size?package=@deepkit/type |

**What Metano does not copy** [M]: the tsc transformer (Metano *is* the compiler and already knows every type), the bytecode and VM (an object literal is already the decoded form), the `new Function` JIT (CSP-hostile, unauditable, and converters as closures are fast enough for JSON-sized payloads), the structural `is<T>` (C# `is` is nominal for classes and interfaces), and the bundle size (NFR-006).

**What Metano borrows** [M]: a per-type descriptor as a *typed TS object literal* (generalising `TypeSpec`, R5); thunks for cycles and cross-module references (`RefDescriptor.spec` already is one); emit only what is used (Deepkit emits for exported types; Metano emits for types that a runtime question reaches, FR-027); closure-composed serializers with no code generation at runtime (already the case in `converters.ts`, kept as NFR-003).

## R11 — Kotlin/JS precedents for the amendments

[V] from kotlinlang.org "JavaScript interop" and "Dynamic type" (fetched 2026-11-01): the parameter of `js()` "is parsed at compile time and translated to JavaScript code 'as-is', it must be a string constant", and `js()` "returns a result of `dynamic` type, which doesn't provide type safety at compile time"; "the `dynamic` type basically turns off Kotlin's type checker" and `dyn.whatever(1)` "will be compiled 'as is'", operators included; `unsafeCast<T>()` does "no type checking … at all during runtime"; `external` declarations tell the compiler the implementation "is provided externally" and generate no code. [I] `asDynamic()` is documented in the stdlib API reference as an inline cast to `dynamic`; the two pages above do not describe it. Metano's `[Import]` / `[External]` are the counterpart of `external`; `Js.Code`, `AsDynamic()` and `UnsafeCast<T>()` are the counterparts of `js()`, `asDynamic()` and `unsafeCast()` (FR-030 to FR-036).

## R12 — Diagnostic allocation

[V] `MS0031` is the highest code in `src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs` (L316); `MS0032`–`MS0037` are reserved by spec 009 and `MS0038`–`MS0043` by spec 010; the catalog stated the next free code was `MS0044` (`diagnostic-catalog.md` L55). [M] Bug-fix PR #355 (#334, package root discovery) takes `MS0044` for an implemented diagnostic and merges before this spec, so this spec reserves **`MS0045`–`MS0056`** (contracts/diagnostics.md) and the next free code is `MS0057`.

## R13 — Tree-shaking: what bundlers keep (critic measurements)

Measured on 2026-11-01 by the first critic pass of PR #349 with a tiny consumer per shape and `bun build --minify`, esbuild 0.24 / 0.28 (`--bundle --minify`), Rollup 4 + `@rollup/plugin-node-resolve` + terser, and webpack 5 (production). Probe sources and the `bundle.sh` script lived in the review scratchpad; the facts are recorded in the critic's memory note and reproduced here. All [V] unless noted.

| Shape | bun | esbuild | Rollup | webpack | Consequence |
|---|---|---|---|---|---|
| `import "metano-runtime"` on today's 2.x runtime | 16 B | 16 B | 16 B | — | The root barrel is already shakable; the new modules must keep it so (NFR-008 row 1). |
| `HashSet` alone (2.x) | 1.33 KB gz | ≈ | ≈ | — | Baseline for the collection budgets. |
| A full `List` surface as a class with string-keyed fields (critic's `list.js` probe) | 1.67 KB gz | ≈ | ≈ | — | Classes are fine; the ≤ 2.5 KB budget has headroom. |
| `linq` `where` + `toArray` (2.x) | 0.36 KB gz | ≈ | ≈ | — | Standalone operator functions shake independently; keep them standalone. |
| `system/json` (2.x) | 2.3 KB gz | ≈ | ≈ | — | — |
| `@js-temporal/polyfill` alone | 43–47 KB gz | ≈ | ≈ | — | Runtime code must never `instanceof Temporal.*`; test by `Symbol.toStringTag`. |
| `export namespace X { export function f() {} }` unused | kept | kept | kept | kept | A TS namespace with members is an IIFE; once one member is used, every member is kept. Affects branded companions, nested types, 010 D13 union companions, runtime `UUID` / `ImmutableCollection` (#361). |
| `export enum E { … }` unused | kept | kept | kept | kept | Same IIFE shape; FR-044 emits `const … as const`. |
| `class X { static $t = factory({...}) }` unused (eager static, no annotation) | kept (whole class) | kept | kept | kept | Eager descriptors are out. |
| `class X { static $t = /*#__PURE__*/ factory({...}) }` unused (string key) | dropped | dropped | dropped | dropped | Acceptable, but still an eager call when the class *is* used. |
| `class X { static [sym] = /*#__PURE__*/ factory({...}) }` unused (computed symbol key) | dropped | **kept** | dropped | dropped | esbuild keeps any class with a computed-symbol static, PURE or not; the same for `static get [sym]()` and an instance field `[sym] = null`. `[Symbol.iterator]` is special-cased and fine. The first draft's symbol-keyed static is therefore rejected. |
| `class X { static get $t() { return (m ??= factory({...})); } }` unused (string-keyed lazy getter) | dropped | dropped | dropped | dropped | Dropped when unused, **but a used class keeps its getter body**, so a factory call there pulls `reflection` into every program that uses the class: one thrown exception measured 0.58 KB gz with a realistic `classType` versus 0.35 KB with a literal (second critic pass). |
| `class X { static get $t() { return (m ??= { kind: "class", …, test: v => v instanceof X }); } }` (string-keyed lazy getter returning a **data literal**) | dropped | dropped | dropped | dropped | **The chosen class shape** (FR-018): zero cost until read, dropped with the class, no runtime import when used. |
| `export const X$t = /*#__PURE__*/ factory({...})` unused | dropped | dropped | dropped | dropped | **The chosen shape for interfaces, plain objects, enums, unions** (FR-019, FR-020, FR-029, FR-044). Without the annotation: kept by Rollup / webpack. |
| Primitive descriptors as plain object literals (`export const Int32Type = { … }`) unused | dropped | dropped | dropped | dropped | Literals need no annotation; one primitive with `isInt32` ≈ 0.2 KB gz. |
| One exception class per module, lazy getter (critic's `exceptions-strkey-pure` probe, one class used) | ≈ 0.4 KB gz | ≈ | ≈ | — | Budget ≤ 0.5 KB per class; unused classes 0 bytes when each is its own module. |
| `registerX(…)` at module scope with package `sideEffects: false`, imported for side effect only | **dropped** | **dropped** | **dropped** | **dropped** | Today's `[StrictUnionGuard]` `registerUnionGuard` silently disappears in bundles (pre-existing defect, #368); any registry design is out (FR-029, NFR-006). |
| The same, bundled with Rollup `treeshake.moduleSideEffects: true` + `@rollup/plugin-node-resolve` | — | — | **1 B** | — | node-resolve still trusts the package's `sideEffects: false`, so a "side-effect-only import" check that resolves through the package is vacuous (second critic pass). FR-043 bundles a copy outside any package, or forces `moduleSideEffects: true` in a `resolveId` hook. |
| `Object.defineProperty(this, "$typeArguments", { value })` in a base ctor and again in a derived ctor | TypeError | TypeError | TypeError | TypeError | A non-configurable property cannot be redefined, and one slot cannot hold both `T` and `U`; FR-024 uses one slot per declaring class. |
| `export const E = {…} as const` + `type E = (typeof E)[keyof typeof E]` for a `[Flags]` enum | TS2322 | — | — | — | `E.A \| E.B` is `number`. |
| `type E = number & { readonly __enum?: "E" }` for a `[Flags]` enum (third pass; verified again by the author with `tsgo --noEmit --strict` on 2026-11-01) | accepts | — | — | — | `E.A \| E.B`, `e \|= E.B`, `e &= ~E.A`, `e ^ E.B`, the literal `0`, `3 as E`, a `JSON.parse` result cast to `E`, `switch` over members, arrays and `===` all type-check; a plain `number` is assignable (no weak-type error: the brand's only property is optional and the source is a primitive); an assignment from a differently branded enum is rejected (TS2322). FR-044 adopts the brand; the earlier claim that a brand would fail was wrong. |
| A `[StrictUnionGuard]` base module that imports its variant guards (variants run `class Circle extends Shape` at load), base imported first | `ReferenceError: Cannot access 'Shape' before initialization` (native) | `superclass is not a constructor` (bundle) | happens to reorder | — | Thunks only delay the call; the import itself is the cycle. FR-029 emits the guard in its own `*.guard.ts` module; the base never imports a variant (the cycle is why ADR-0009 used a registry). |
| A "copy outside any package" side-effect check under `/private/tmp` with a stray parent `package.json` declaring `sideEffects: false` | 0 B for a real canary | 0 B | 0 B | — | Bundlers read the nearest `package.json` upward; the temp dir must ship its own `package.json` without `sideEffects`, and a canary that must be kept is normative (FR-043). |
| Today's runtime under a truthful (non-trusting) side-effect-only import of `index` | 164 KB (3 KB with `@js-temporal/polyfill` external) | ≈ | ≈ | — | `temporal-helpers.ts` L3 builds `Temporal.PlainDate.from("0001-01-01")` at module scope, `property-naming-policy.ts` L120-125 assigns statics at module scope, `UUID` / `ImmutableCollection` are namespaces. Stage 011a rewrites the first two lazily, allowlists the namespaces, and marks third-party packages external. |
| `enum` / `namespace` in raw `.ts` compiled directly by esbuild or bun, unused | dropped | dropped | — | — | Only tsgo-compiled `dist` JS keeps the IIFE; FR-043 therefore compiles goldens with tsgo before bundling, and FR-044's claim is qualified to tsgo output. |
| `export namespace X { export const $type = {...} as const }` merged with an `as const` object | TS2451 | — | — | — | TS cannot merge a namespace with a `const` object; `X.$type` was impossible for `[StringEnum]` output anyway. |

Three more facts the critic recorded [V]: `[ModuleEntryPoint]` goldens contain legitimate top-level calls and the branded / nested-type goldens contain namespaces (#361), so a "no side effects" scan needs the exemption and the allowlist of FR-043; `system/equality.equals` falls back to a structural compare for any object without `equals()` (class instances, lists), so it is not `EqualityComparer<T>.Default` (R14); and `copyRecordWith` uses `Object.assign`, which copies enumerable own properties only, so a non-enumerable hidden field is dropped by `with` unless copied explicitly (FR-008, FR-024).

Also [V] on `main`: `targets/js/metano-runtime/package.json` `exports` carry no `source` / `types` conditions (only `.`, `./system/json`, `./system/linq`, `./package.json`, `./*.js`, `./*` as plain paths), and `scripts/smoke-test-runtime-package.sh` has no bundler step; FR-011 adds both.

**Conclusion**: tree-shaking is not a property of "small modules"; it is a property of *shapes*. Every emission shape of this spec is one of the dropped rows above, and FR-043 re-measures them on every PR.

## R14 — Equality semantics for collections

[V] `targets/js/metano-runtime/src/system/equality.ts` `equals` (L35) checks a custom `equals`, then arrays element-wise, `Map`, `Set`, `Date`, and finally compares **any** two objects structurally by their own enumerable properties. Two distinct instances of a class without `equals()` with equal fields are therefore `equals`-equal, which .NET's `EqualityComparer<T>.Default` (reference equality unless `Equals` is overridden) does not do. [V] `hash-set.ts` `_areEqual` (L146-157) and `_getHash` (L134-144) implement the .NET rule: `===`, then duck-typed `equals()` / `hashCode()`, else reference / `HashCode.combine`. [V] .NET: `List<T>.Contains`, `IndexOf`, `Remove`, `Dictionary<K,V>` and `HashSet<T>` use `EqualityComparer<T>.Default`; a record overrides `Equals` / `GetHashCode` (value semantics over its members, with reference semantics for a member that does not override them, so a `List<T>` member compares by reference). SameValueZero: `NaN` equals `NaN` for `double` in `EqualityComparer<double>.Default` (.NET `Double.Equals` returns `true` for two `NaN`s).

**Conclusion**: the collections key through a new `defaultEquals` / `defaultHashCode` pair modelled on today's `HashSet` (D18, FR-006), and `system/equality.equals` is not taught about collections (a record with a collection field compares it by reference, as .NET does).
