# Data Model: BCL collections as runtime types, runtime type descriptors

No persistent data. The entities are the C# inputs the core reads, the IR it builds, and the runtime shapes the TypeScript target emits.

## 1. C# inputs

| Source | Read | Notes |
|---|---|---|
| BCL collection symbols (`RoslynTypeQueries.IsCollectionLike` / `IsDictionaryLike` / `IsSetLike`, `Queue`, `Stack`, `ImmutableList`, `ImmutableArray`, `ReadOnlyCollection`, the interfaces) | symbol identity + type arguments + variance of the interface's type parameters | Replaced by one `CollectionKindResolver` that returns `(IrCollectionKind, args, variance)`; string-prefix tests go away. |
| `Metano.Interop.JsArray<T>` | symbol identity | TS-only (MS0051 elsewhere). |
| `Metano.Interop.InteropExtensions.AsJsArray`, `Metano.Collections.ListViewExtensions.AsSpan` / `AsReadOnlySpan` over `List<T>`, `System.Runtime.InteropServices.CollectionsMarshal.AsSpan(List<T>)`, `List<T>.ToArray()` and the other in-scope `ToArray()` members | symbol identity of the member | Declarative member mappings (FR-055, FR-056): the free helpers `asJsArray(list)` / `asSpan(list)` / `asReadOnlySpan(list)` (they handle `null`) and `toArray()` on the runtime class; no IR node of their own. `AsJsArray` is TS-only (MS0051). |
| `System.Span<T>` / `System.ReadOnlySpan<T>` in type position | element type, read-only-ness | Decided by the type (FR-001's span row): `ReadOnlySpan<char>` → `IrPrimitiveTypeRef(String)` (today's string-search rule, now also in type position); every other span → `IrArrayTypeRef` with `SpanKind`. Every conversion to `ReadOnlySpan<char>` whose source is not a `string` (`char[]`, the implicit `Span<char>` → `ReadOnlySpan<char>`, `List<char>.AsReadOnlySpan()`) reports `MS0001`. |
| `RoslynTypeQueries.IsDictionaryLike` / `IsSetLike` / `IsCollectionLike` | — | Today's groupings merge mutable, concurrent and immutable types (`IsDictionaryLike` matches `Dictionary`, `IDictionary`, `IReadOnlyDictionary`, `ConcurrentDictionary`, `ImmutableDictionary`, `IImmutableDictionary`; `IsSetLike` matches `HashSet`, `ISet`, `SortedSet`, `ImmutableHashSet`; `IsCollectionLike` merges `List`, the list interfaces, `ImmutableList`, `ImmutableArray`, `Queue`, `Stack`). `CollectionKindResolver` MUST map each type to its own `IrCollectionKind` and never through these groupings (FR-054), so an immutable dictionary never becomes the mutable runtime `Dictionary`. |
| `Metano.Interop.Js.Code` / `Code<T>` | the `code` argument syntax (literal, const, interpolated string), the type argument | FR-031; holes are `InterpolationSyntax` expressions; a hole position inside a JS string literal of the text is detected by a small lexer over the literal parts (MS0056). |
| `Metano.Interop.InteropExtensions.UnsafeCast<T>` / `AsDynamic` | the receiver, the type argument | FR-035 / FR-036. |
| `IConversionOperation` with `Conversion.IsBoxing` (to `object` / `ValueType` / `Enum` only) over an enum / `[InlineWrapper]` type that has a descriptor, `[EmitRuntimeType]` / `[assembly: DisableBoxing]` attributes | source type, target type (`object`, `ValueType`, `Enum`, an interface), whether the source is an open `T`, nullable, an interop-bound `object` slot | FR-050 / FR-027; the conversion is lowered to `box(...)` and reaches the type. |
| `IDynamicTypeSymbol`, `IDynamic*Operation`, `IConversionOperation` from `dynamic` | operation kind, member name, arguments, whether the conversion is explicit | FR-030: implicit conversions emit nothing; explicit casts go through FR-026. |
| `IsPatternExpression`, `BinaryExpression(Is/As)`, `CastExpression` (explicit reference / unboxing / `dynamic` conversions), switch arms and labels, overload parameters, `catch` clauses (+ `CatchFilterClauseSyntax`), `GetType()` invocations, `TypeOfExpression`, `ObjectCreation` of `T` | the tested / target type, the tested expression's static type, the receiver's static type for `GetType()` | The *runtime questions* that drive reachability (FR-027) and reification (FR-024). |
| Collection constructions (`ObjectCreation`, collection initializers in both forms, collection expressions, LINQ terminals) | the collection kind and whether a runtime question about that kind exists | FR-008: `$typeArguments$<Class>` passed only then. |
| `[PlainObject]` types assignable to a tested static type | shape (required members with coarse kinds; 009 FR-019); derived plain objects of a receiver type | MS0047; the `GetType()` constant fold condition (FR-020). |
| `[StrictUnionGuard]` bases and their variants (`TypeGuardBuilder.TryFindDiscriminatedVariants`) | variant guard names and modules | the strict guard is emitted in its own `<base>.guard.ts` module that imports base and variants; the base imports no variant (FR-029). |
| `[Ignore(TargetLanguage)]` | per-target ignore | MS0051 suppression. |

## 2. Core IR (`Metano.Compiler`, target-agnostic)

```mermaid
classDiagram
    class IrCollectionTypeRef {
        IrCollectionKind Kind
        IReadOnlyList~IrTypeRef~ TypeArguments
        IReadOnlyList~IrVariance~ Variance
    }
    class IrCollectionKind {
        <<enumeration>>
        List
        IList
        IReadOnlyList
        ICollection
        IReadOnlyCollection
        Dictionary
        IDictionary
        IReadOnlyDictionary
        HashSet
        ISet
        IReadOnlySet
        Queue
        Stack
        ImmutableList
        ReadOnlyList
        ImmutableHashSet
        ImmutableDictionary
        IImmutableList
        IImmutableSet
        IImmutableDictionary
        SortedSet
        SortedDictionary
        SortedList
        ConcurrentDictionary
        KeyCollection
        ValueCollection
    }
    class IrArrayTypeRef {
        IrTypeRef ElementType
        bool IsJsArray
        IrSpanKind SpanKind
    }
    class IrSpanKind {
        <<enumeration>>
        None
        Span
        ReadOnlySpan
    }
    class IrDynamicTypeRef
    class IrRuntimeTypeTest {
        <<abstract>>
        +Of(IrTypeRef, IrTypeRef staticType) IrRuntimeTypeTest
    }
    class Instanceof
    class Interface { IrNamedTypeRef Descriptor; IReadOnlyList~IrNamedTypeRef~ ConsumerImplementers }
    class Structural { string GuardName }
    class Typeof { IrPrimitive Primitive; bool Validating }
    class ArrayTuple { int Arity }
    class Array
    class Collection { IrCollectionKind Kind; IReadOnlyList~IrTypeRef~ TypeArguments; IReadOnlyList~IrVariance~ Variance }
    class Reified { string TypeParameter }
    class Iterable
    class Null
    class Object
    class Union { IReadOnlyList~IrTypeRef~ Cases }
    class Constant { bool Result }
    class None
    IrRuntimeTypeTest <|-- Instanceof
    IrRuntimeTypeTest <|-- Interface
    IrRuntimeTypeTest <|-- Structural
    IrRuntimeTypeTest <|-- Typeof
    IrRuntimeTypeTest <|-- ArrayTuple
    IrRuntimeTypeTest <|-- Array
    IrRuntimeTypeTest <|-- Collection
    IrRuntimeTypeTest <|-- Reified
    IrRuntimeTypeTest <|-- Iterable
    IrRuntimeTypeTest <|-- Null
    IrRuntimeTypeTest <|-- Object
    IrRuntimeTypeTest <|-- Union
    IrRuntimeTypeTest <|-- Constant
    IrRuntimeTypeTest <|-- None
    class IrReifiedTypeParameter {
        string Name
        string HiddenParameterName
        int Position
        bool HasDefault
    }
    class IrMethodSignature {
        IReadOnlyList~IrReifiedTypeParameter~ ReifiedTypeParameters
    }
    class IrClassDeclaration {
        IReadOnlyList~IrReifiedTypeParameter~ ReifiedTypeParameters
        string? TypeArgumentSlot
        IReadOnlyList~IrTypeRef~ BaseTypeArguments
        IrRuntimeTypeDescriptor? Descriptor
    }
    class IrRuntimeTypeDescriptor {
        string Name
        string FullName
        string Namespace
        IrTypeRef? Base
        IReadOnlyList~IrTypeRef~ Interfaces
        IReadOnlyList~IrTypeRef~ PlainImplementers
        bool IsAbstract
        bool IsValueType
        bool HasParameterlessCtor
    }
    class IrCatchClause {
        IrTypeRef? ExceptionType
        string? VariableName
        IrExpression? Filter
        IReadOnlyList~IrStatement~ Body
        bool CatchesEveryException
        bool HandlesEveryException
    }
    class IrRethrowStatement
    class IrTypeTestExpression { IrExpression Value; IrRuntimeTypeTest Test; IrTypeRef Target }
    class IrAsExpression { IrExpression Value; IrRuntimeTypeTest Test; IrTypeRef Target }
    class IrCheckedCastExpression { IrExpression Value; IrRuntimeTypeTest Test; IrTypeRef Target; bool Unchecked }
    class IrGetTypeExpression { IrExpression Receiver; IrTypeRef? ConstantType }
    class IrBoxExpression { IrExpression Value; IrTypeRef BoxedType }
    class IrUnboxExpression { IrExpression Value; IrTypeRef Target; bool Interop }
    class IrTypeOfExpression { IrTypeRef Type }
    class IrIndexerUpdateExpression { IrExpression Receiver; IrExpression Index; IrLambda Update; bool ReturnsOld }
    class IrInlineCodeExpression { IReadOnlyList~IrInlineCodePart~ Parts; IrTypeRef? ResultType; TargetLanguage Target; bool IsStatement }
    class IrUnsafeCastExpression { IrExpression Value; IrTypeRef Target }
    class IrAsDynamicExpression { IrExpression Value }
    class IrDynamicMemberAccess { IrExpression Receiver; string Name }
    class IrDynamicInvocation { IrExpression Target; IReadOnlyList~IrExpression~ Arguments }
    class IrDynamicIndexerAccess { IrExpression Receiver; IReadOnlyList~IrExpression~ Arguments }
    class RuntimeTypeReachability {
        +Compute(IrCompilation) ReachabilityResult
    }
    class ReachabilityResult {
        IReadOnlySet~IrTypeRef~ ReachedTypes
        IReadOnlySet~IrCollectionKind~ QuestionedCollectionKinds
    }
```

Notes:

- `IrCollectionKind` has one value per runtime class and interface of FR-001 / FR-054; stage 011c adds the `List`, `HashSet`, `Dictionary`, `Queue`, `Stack` values with their interfaces and views, stage 011h the read-only, immutable, sorted and concurrent ones. Until 011h lands those types keep their 2.x IR.
- `IrArrayTypeRef.SpanKind` marks a `Span<T>` (`Span`) or `ReadOnlySpan<T>` (`ReadOnlySpan`, never with a `char` element, which is a string); the value is still the native array (a view of a `List<T>` is its backing array, FR-056, FR-057), only the TS type differs: the runtime `Span<T>` interface or `ReadonlyArray<T>`. `None` is every other array. Dart ignores `SpanKind` and maps the node to `List<T>` as any array.
- `IrRuntimeTypeTest.Of(type, staticType)` gains the static type of the tested expression so that `Typeof.Validating` and `Constant` can be decided in one place (FR-022). Every existing caller passes `staticType: null` and gets today's behaviour plus the new categories; `ArrayTuple` is kept unchanged.
- `IrReifiedTypeParameter.Position` is the index in the emitted parameter list after the trailing rule of OD-7; `HasDefault` is `true` when a preceding parameter is optional. A generic class's `ReifiedTypeParameters` describe its hidden constructor parameter and `TypeArgumentSlot` names the per-declaring-class slot it defines (`$typeArguments$Box`), `null` for a class that defines none; `BaseTypeArguments` are the descriptors a derived class passes to `super` (FR-024). The variant guards of a `[StrictUnionGuard]` base are data of the guard emitter, not of the descriptor.
- `IrRuntimeTypeDescriptor` is attached to a declaration only when `RuntimeTypeReachability` reaches it (FR-027); exception classes always carry one. The Dart target ignores it.
- `ReachabilityResult.QuestionedCollectionKinds` drives FR-008: a collection construction passes `$typeArguments$<Class>` only when its kind is in the set.
- `IrBoxExpression` / `IrUnboxExpression` come from Roslyn `IConversionOperation` with `Conversion.IsBoxing` over an enum or `[InlineWrapper]` type (FR-050); `Interop` marks the gated unbox at `[Import]` / `[Emit]` / `Js.Code` / `dynamic` / `AsDynamic()` boundaries (FR-051: only when the argument's static type can hold a box and the compilation has a boxing conversion). Both are skipped when `[assembly: DisableBoxing]` is present or the source is an open type parameter. Dart ignores them.
- `IrIndexerUpdateExpression` is the evaluate-once node for `list[i] op= v`, `list[i]++`, `dict[k] ??= v` (FR-010); it renders as `update` / `updateReturningOld`.
- `IrInlineCodePart` is `Text(string)` or `Hole(IrExpression)`. `ResultType` is the type argument of `Js.Code<T>` and `null` for the untyped `Js.Code` (whose C# result is `dynamic`), so a target decides the cast from the node alone, independent of how `dynamic` maps; `Target` is the target the API belongs to (`TypeScript` for `Metano.Interop.Js`), a fact of the API, not of the running emission.
- The existing `IrIsPatternExpression` + `IrTypePattern` keep working; `IrTypeTestExpression` is the node the extractor produces for `x is T` without a designator so the target can render helpers without a pattern walk. Both carry the classification so no target recomputes it.
- `IrCatchClause.Filter` was delivered ahead by #628 (`ExtractCatch` reads `CatchFilterClauseSyntax`), together with the computed `CatchesEveryException` (a general `catch` or `catch (System.Exception)`) and `HandlesEveryException` (that, without a filter), and with `IrRethrowStatement` for `throw;` (TS: `throw e$;`, Dart: `rethrow;`). The try statement keeps its clause list; **each target** renders the single-catch chain from it (FR-015), so the core holds the data and no target recomputes the arm order.

## 3. TypeScript target

| Component | Role |
|---|---|
| `Bridge/IrToTsCollectionBridge` | `IrCollectionTypeRef` → runtime class / interface type refs; a `foreach` whose collection is `IrCollectionTypeRef(List)` → the indexed loop over `_items`, with the receiver always bound once to a temporary (a local can be reassigned in the body), that checks `_version` and the recorded length at the start of every step including the final one, so never after a `break` / `return` (FR-057; each check carries FR-058's inline guard), every other `foreach` stays `for..of`; `IrArrayTypeRef.SpanKind` → `Span<T>` (imported from `metano-runtime/system/collections`) / `ReadonlyArray<T>` (FR-056); factories for `new` / both initializer forms / collection expressions; `adopt(…, { fixedSize: true })` at array→interface conversions (FR-003); `$typeArguments$<Class>` only for questioned kinds (FR-008); `IrIndexerUpdateExpression` → `update` / `updateReturningOld`. |
| `Transformation/RuntimeTypeEmitter` | `IrRuntimeTypeDescriptor` → the lazy `static get $runtimeType()` returning a data literal + module-level `let X$rt` on classes; an exported data literal `X$runtimeType` for interfaces, plain objects, enums, unions (FR-018–FR-020, FR-029, FR-044). No factory call is emitted. |
| `Bridge/IrToTsRuntimeTestBridge` | renders every `IrRuntimeTypeTest` category (replaces `BuildTypeTest`; `IrTypeCheckBuilder.Generate` delegates to it, FR-021); `withImplementers` at cross-package interface tests (FR-019). |
| `Bridge/IrToTsCatchChainBridge` + `TypeScript/AST/TsTryStatement` | FR-015: a real AST node printed by `Printer`, replacing `MapTry`'s string rendering. |
| `Bridge/IrToTsInlineCodeBridge` | FR-031 / FR-032. |
| `Analysis/IrRuntimeRequirementScanner` | imports for `isType`, `asType`, `castTo`, `getRuntimeType`, `getRuntimeTypeFromType`, `getNumberType`, `box`, `unbox`, `enumFromRaw`, `tryFilter`, `withImplementers`, `fromEnd`, `isIterable`, `asArray`, `toException`, descriptors, factories, collection classes and per-class exception modules; nothing for an unreached type. |
| `Transformation/TypeGuardBuilder` | plain-object guard synthesis shared with 009 / 010; a `[StrictUnionGuard]` base's `isBase` is emitted into a separate `<base>.guard.ts` source file (a new `TsSourceFile` beside the base's) that imports the base and the variant guards and holds the `variants` table; `ImportCollector` resolves `isBase` references to the guard module (FR-029). |
| `JsonSerializerContextTransformer` | `runtimeType` thunk on specs; `list` / `dictionary` / … descriptor kinds (FR-028). |
| `Transformation/EnumTransformer` (existing enum path) | `export const E = {…} as const` + the closed-union alias, or `type E = number` for `[Flags]` (FR-044). |
| `PackageJsonWriter` | `sideEffects: ["./src/<entry>.ts", "./dist/<entry>.js", …]` from the `[ModuleEntryPoint]` modules, both condition paths (FR-045, #368). |

## 4. Runtime shapes

See [contracts/runtime-collections.md](./contracts/runtime-collections.md) and [contracts/runtime-types.md](./contracts/runtime-types.md). Modules and their dependency direction (acyclic; `reflection` never imports `collections`):

```mermaid
flowchart LR
  typechecking["type-checking (isInt32, …)"] --> reflection["system/reflection (RuntimeType types, isType, isAssignableFrom, getRuntimeType, castTo, tryFilter, primitives)"]
  exceptions["system/exceptions/* (one module per class; lazy $runtimeType literal)"] -. "type imports only" .-> reflection
  reflection --> collections["system/collections (classes, default-comparer, helpers, descriptors)"]
  exceptions --> collections
  reflection --> exceptions
  collections --> linq["system/linq (toList, toDictionary, toHashSet)"]
  reflection --> json["system/json (TypeSpec.runtimeType)"]
  collections --> json
```

Before the literal fix there was a value-level cycle `reflection → exceptions` (`castTo` / `getRuntimeType` throw `InvalidCastException` / `NullReferenceException`) and `exceptions → reflection` (`classType` inside the getters). With FR-018's literal getters, `exceptions` imports only **types** from `reflection` (erased at build), so the value graph is acyclic: `reflection` imports the two exception modules it throws, and nothing imports `reflection` from `exceptions`. A program that throws but never tests pays for the one exception module alone (**budget** ≤ 0.5 KB gz, NFR-008; the critic's probe measured 0.35 KB for the literal shape, research R13).

## 5. Dart target

`IrCollectionTypeRef` → `List` / `Map` / `Set` / `Queue`, and for the FR-054 classes the unmodifiable views and `SplayTreeMap` / `SplayTreeSet` of FR-038; `IrDynamicTypeRef` → `dynamic`; `IrRuntimeTypeTest` → native `is`; the catch clause list → one `catch (e$)` with an `is` chain and `when` filters inside (FR-015, FR-038); `IrIndexerUpdateExpression` → `list[i] = f(list[i])` with a temp for the index; `IrUnsafeCastExpression` → `as T`; `IrAsDynamicExpression` → `as dynamic`; `AsSpan()` → the list itself, `AsReadOnlySpan()` → `UnmodifiableListView(list)`, `AsJsArray()` → MS0051 (FR-056); `IrInlineCodeExpression` with `Target = TypeScript` → MS0051; descriptors and reified parameters ignored. Each node's Dart mapping lands in the same PR as the node (FR-038) — deferred to after 3.0.0 (D28); until then a new node may lower to an `MS0001` placeholder in Dart.
