# Contract: `metano-runtime/system/collections`

**Normative for**: FR-001–FR-012, FR-016, FR-028 (runtime half of the collection kinds), FR-054–FR-058, NFR-001, NFR-006–NFR-009. Every class below ships in 3.0.0 (FR-054, D24): stage 011c delivers `List`, `HashSet`, `Dictionary`, `Queue`, `Stack`, their interfaces and the dictionary views; stage 011h the read-only, immutable, sorted and concurrent classes. Every member below has a runtime test (FR-041). Names follow OD-2 (C#-like camelCase). Exceptions are the classes of [contracts/runtime-types.md](./runtime-types.md) § Exceptions, imported per class from `system/exceptions/*`. `RuntimeType` and `UnknownType` come from `metano-runtime/system/reflection`; the collection descriptor factories live here (`descriptors.ts`).

**Stage 011c API freeze (R0, 2026-11-19, D30).** The sections up to § 011h classes are the exact TypeScript API that the compiler (stage 011c, group C) emits against and that the runtime (group R) implements. A signature, module path, field name, helper name or message written here changes only through a spec amendment. The .NET behaviour quoted below was probed on .NET 10 / 11 (`List<T>`, `Dictionary<K,V>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`, `System.Text.Json`); each probed behaviour becomes a runtime test in R1 and, where a C# probe can show it end to end, a conformance row in C.

### Corrections the code forced (R0)

| Was (this contract before R0) | Now | Forced by |
|---|---|---|
| `defaultEquals` / `defaultHashCode` defined in `default-comparer.ts` | The collections key and search through **`elementEquals` / `elementHashCode`** (`default-comparer.ts`). `elementEquals` delegates to the existing `defaultEquals` of `system/equality.ts` except that a top-level array compares by reference | `system/equality.ts` already exports a `defaultEquals` (`EqualityComparer<T>.Default` for open-typed record fields, #674; Boxed-aware since 011b B3) that compares arrays element by element (a C# tuple is a JS array). A second `defaultEquals` would collide in the root barrel (`export *` from both drops the name). Inside a collection an array element is a C# array (tuple-typed collections keep `MS0001`, FR-054), which .NET compares by reference; 2.x's `includes` / `Map` did so too, so element-wise comparison would be a regression for `List<int[]>` / `HashSet<int[]>`. |
| `HashSet` "keeps its storage" (`hash-set.ts` L100-157) | `HashSet` and `Dictionary` share one internal keyed store (`hash-core.ts`, the one D24 names for `ConcurrentDictionary`) | Today's storage enumerates primitives before objects (not insertion order, which .NET keeps until a removal), hashes every object without `hashCode()` to bucket `0` (`HashCode.combine` → `hashValue` returns `0`, so reference keys are O(n) per operation) and declares enumerable class fields (`_buckets`, `_size`, `_primitiveSet`), against FR-002. |
| `tryGetValue(key): [true, V] \| [false, null]`; `tryDequeue` / `tryPeek` / `tryPop` likewise; `find` / `findLast` return `T \| null` | Each takes the value C# stores on a miss: `tryGetValue(key, missingValue)` returns `[false, missingValue]`; `find(match, defaultValue)` returns `defaultValue` | C# stores `default(T)` (`0` for `int`, `false`, …), not `null`; the compiler already passes it for `TryGetValue` / `GetValueOrDefault` (`Dictionaries.cs`, `IrOutArgumentCall`) and for `Find` (`Lists.cs`). |
| `sort(comparison?)`, `binarySearch(item, comparison?)` | Both take `comparison?: Comparison<T> \| IComparer<T> \| null`, told apart by `typeof comparison === "function"` (a lowered `IComparer<T>` is always an object) | `Sort` has both C# overloads; `BinarySearch(T, IComparer<T>)` exists; one parameter shape shared with 011h's `ImmutableList` (group H's request). |
| `IReadOnlyList<T>` declares `slice` / `getRange` | Removed from the interface (kept on `List<T>`) | C# cannot call them through `IReadOnlyList<T>` (no `Slice` / `GetRange` members), and they would force every 011h implementer to return and import a `List`. |
| Interfaces carry the class conveniences (`fromEnd`, `update`, `updateReturningOld`, `getValueOrDefault`, read-only `indexOf` / `contains`) | Each TS interface declares only what its C# interface declares; those stay class members, and interface receivers lower through the interface members (§ Interface receivers) | A user C# class implementing `IList<T>` must type-check against the interface without synthesized members (supervisor decision W4-13, R0 review). |
| Class `$runtimeType` = `listType(UnknownType)` with `interfaces` thunks and a `close` hook | A data literal importing nothing (`interfaces` as kind names with slot-index mappings); reflection's `specializeCollection` specializes and memoizes | FR-018 (descriptors are data) and research R13 row 18: a used class keeps its getter body, so a factory call there would pull `descriptors.ts` into every `List` bundle (R0 review). |
| `elementHashCode` hashes any object without `hashCode` by identity | `Decimal` and Temporal values hash by their normalized text; any other value with `equals` but no `hashCode` hashes to a constant | decimal.js and Temporal values have `equals` but no `hashCode`; an identity hash would put equal keys in different buckets (`HashSet<decimal>.Contains`, `Dictionary<DateOnly, V>.get` silently missing; R0 review blocker B1). |
| `IReadOnlyDictionary.keys: KeyCollection<K>` | `keys: IReadOnlyCollection<K>`, `values: IReadOnlyCollection<V>` (`Dictionary` narrows them to `KeyCollection` / `ValueCollection`) | `ConcurrentDictionary` (011h) returns snapshots, not `KeyCollection`. |
| Fixed-size adopted list: `clear` throws `Collection was of a fixed size.`; `set` / `sort` / `reverse` bump nothing stated | `clear` throws `Collection is read-only.`; a fixed-size adopted list's writes never bump `_version` and `isReadOnly` is `true` | .NET (an array as `IList<T>`): `Clear` → `NotSupportedException: Collection is read-only.`; array enumerators never detect writes; `ICollection<T>.IsReadOnly` of an array is `true`. |
| `remove` and `clear` are the only non-invalidating dictionary members; "`add` / `set` of a new key / `update` do" | `Dictionary`: only an insertion of a new key invalidates (`add`, `set` / `tryAdd` of a new key); overwriting an existing key (`set`, `update`) does not. `HashSet`: only an addition invalidates (`add` of a new item, `unionWith` / `symmetricExceptWith` when they add); `remove`, `clear`, `exceptWith`, `intersectWith`, `removeWhere` do not | Probed on .NET 10: `foreach (kv in d) d[kv.Key] = 5` and `d[kv.Key] += 1` do not throw; `HashSet.Remove` / `Clear` / `ExceptWith` / `IntersectWith` / `RemoveWhere` during enumeration do not throw. |
| `Stack` enumeration failure uses the common message | `Stack`'s enumerator throws `Collection was modified after the enumerator was instantiated.` | .NET's `Stack<T>.Enumerator` message. |
| `Queue` / `Stack` share the `of` factory | No `of` on `Queue` / `Stack` | C# has no collection initializer or collection expression for them (no `Add`). |
| `List.of(items: readonly T[])` (copy implied) | `List.of(items: T[])` takes ownership of the array (no copy) | FR-002 (one pass, no extra copy) and NFR-001: the compiler always passes a fresh array literal. |
| Comparer-less factories (`HashSet.of(items, typeArguments?)`, `Dictionary.of` / `assign(entries, typeArguments?)`) | A comparer goes before `typeArguments`: `of(items, comparer?, typeArguments?)`, `assign(entries, comparer?, typeArguments?)` | A collection initializer on a comparer constructor (`new HashSet<T>(cmp) { … }`) lowers to one factory call, without the `IrLetExpression` sequence; 011h's sorted / concurrent factories use the same order (group H's request). |
| `KeyCollection` / `ValueCollection` in `dictionary.ts` over a `Dictionary` | In `dictionary-views.ts`, over `KeyValueSource<K, V>`; the module does not import `Dictionary` | 011h's `SortedDictionary` and `ImmutableDictionary` reuse the views (group H's request). |
| Interface descriptors test "`instanceof` any implementing class" | Nominal through the class literal's `interfaces` list (the `implementsInterface` walk), matched by collection kind; an interface descriptor imports no class | A 011h class joins `is IReadOnlyList<T>` by listing it (a user class implementing `IList<T>` does not: `MS0001` at the test, D31); `descriptors.ts` would otherwise import every class (group H's request). |
| JSON collection kinds name the runtime class in `system/json` | Each kind carries a `create` closure that the context writes (`SequenceCollectionDescriptor` / `KeyedCollectionDescriptor`), so `system/json` imports no collection class | A context that uses only `List` must not bundle every collection class (group H's request; NFR-008). |
| `HashSet` capacity constructors drop the argument (`Sets.cs`) | Every class has `withCapacity(capacity, …)`, which evaluates the argument once and throws on a negative one | .NET throws `ArgumentOutOfRangeException` (`Non-negative number required. (Parameter 'capacity')`); dropping the argument also drops its side effects (the reason `List.withCapacity` exists). |

## Modules (stage 011c)

Every module is under `targets/js/metano-runtime/src/system/collections/`, is side-effect free (NFR-006: 0 B on a side-effect-only import, FR-043) and has a size-budget row (NFR-008). The package `exports` entries `./system/collections` and `./system/collections/*` (with `source` / `types` / `import`) already exist since stage 011a (FR-011); no new entry is needed.

| Module | Exports | Barrel (`system/collections/index.ts`) |
|---|---|---|
| `list.ts` | `List` | yes |
| `dictionary.ts` | `Dictionary` | yes |
| `dictionary-views.ts` | `KeyCollection`, `ValueCollection`; type `KeyValueSource` (imports no `Dictionary`) | yes |
| `hash-set.ts` | `HashSet` (reworked in place) | yes |
| `queue.ts` | `Queue` | yes |
| `stack.ts` | `Stack` | yes |
| `interfaces.ts` | types only: `IReadOnlyCollection`, `IReadOnlyList`, `ICollection`, `IList`, `IReadOnlyDictionary`, `IDictionary`, `IReadOnlySet`, `ISet`, `Span` | yes (`export type`) |
| `default-comparer.ts` | `elementEquals`, `elementHashCode`, `defaultCompare`, `createComparer`, `defaultEqualityComparer`, `defaultComparer`, (011h) `defaultComparerFor`, `defaultEqualityComparerFor`; types `IEqualityComparer`, `IComparer`, `Comparison` | yes |
| `helpers.ts` | `fromEnd`, `isIterable`, `asArray`, `asJsArray`, `asSpan`, `asReadOnlySpan`, `asList` | yes |
| `descriptors.ts` | `listType`, `dictionaryType`, `hashSetType`, `queueType`, `stackType`, `iListType`, `iCollectionType`, `iReadOnlyListType`, `iReadOnlyCollectionType`, `iDictionaryType`, `iReadOnlyDictionaryType`, `iSetType`, `iReadOnlySetType` | yes |
| `throw-helpers.ts` | the cold throw functions (§ Cold throw helpers) | `throwModified` only |
| `checks.ts` | the ambient `__METANO_CHECKS__` declaration (§ Production checks) and a type `MetanoChecks` that carries it into every entry's declarations | type only |
| `hash-core.ts` | `HashCore` (internal): the keyed store shared by `Dictionary`, `HashSet`, (011h) `ConcurrentDictionary` and `immutable-storage.ts` (§ Equality and comparison) | no (internal; reached by 011h through the `./system/collections/*` wildcard) |
| `set-algebra.ts` | the `ISet` / `IReadOnlySet` algorithms over `SetLike<T>` (internal), written by R for `HashSet` and shared by 011h's sets | no (internal) |
| `list-helpers.ts`, `dictionary-helpers.ts`, `immutable-collection.ts` | legacy, unchanged (§ Legacy symbols) | yes, until removed |

**Generated code imports** every collection value and type from the barrel `metano-runtime/system/collections` (types with the per-name `type` qualifier: `import { List, throwModified, type Span } from "metano-runtime/system/collections"`), and the type-argument descriptors from `metano-runtime/system/reflection`. The root barrel `metano-runtime` keeps re-exporting the collections barrel; no exported name collides with another root export (checked in R1 by a test that imports every barrel).

**Dependency direction** (data-model.md § 4): `collections` imports `exceptions/*` (per class), `reflection` (types, `UnknownType`, `memoNodeFor`, `isAssignableFrom`) and `system/equality.ts` (`defaultEquals`, `valueHashCode`'s helpers); `reflection` never imports `collections`; `json` and `linq` import the collection classes they build.

## Storage ABI (FR-002, FR-008, FR-057)

Every class keeps its state in **non-enumerable, non-configurable, string-keyed own data properties** defined in the constructor through `Object.defineProperty`, always the same ones in the same order, so that every construction path (constructor, `of`, `from`, `withCapacity`, `adopt`, `slice`, JSON) yields one hidden class per class. They are declared in the class body with `declare` (no class-field initializer: under `useDefineForClassFields` an initializer would define an enumerable field before the constructor runs). No `#private`, no symbol keys, no `Proxy`, no closures over state.

| Class | Fields, in definition order | Read by generated code |
|---|---|---|
| `List<T>` | `_items: T[]` (writable `false`; the one backing array for life), `_version: number` (writable), `$typeArguments$List: readonly RuntimeType[] \| undefined` | **`_items` and `_version`** (lowered `foreach`, FR-057); public in the `.d.ts`, no `@internal` |
| `Dictionary<K, V>` | `_store` (the `hash-core` store), `_version`, `_keys` / `_values` (the cached views, `undefined` until first read), `$typeArguments$Dictionary` | none |
| `HashSet<T>` | `_store`, `_version`, `$typeArguments$HashSet` | none |
| `Queue<T>` | `_items` (the queued elements are `_items[_head..]`; the consumed prefix is dropped once it is at least half of the array, R2), `_head`, `_version`, `$typeArguments$Queue` | none |
| `Stack<T>` | `_items` (bottom to top), `_version`, `$typeArguments$Stack` | none |
| `KeyCollection<K>` / `ValueCollection<V>` | `_source` (a `KeyValueSource`) | none |

Only `List`'s `_items` / `_version` are ABI; the other fields are implementation details (still non-enumerable, so `Object.keys`, spread, `JSON.stringify`, `toEqual`, `structuredClone` and the structural `equals` of `system/equality` see nothing). `$typeArguments$<Class>` is always defined, `undefined` when no descriptors are passed (FR-008).

`count` is a prototype getter on every class (`List`: `this._items.length`, so a length change through `asJsArray()` keeps it right, FR-057).

## Production checks (FR-058, D26; mechanism amended by W4-33)

- **Identifier: `__METANO_CHECKS__`**, declared once, in `checks.ts`, as `declare global { const __METANO_CHECKS__: boolean | undefined; }`; `checks.ts` also exports `type MetanoChecks = boolean | undefined`, which `list.ts`, the collections barrel and the root barrel re-export, so the declaration reaches every entry point's `.d.ts` and the generated code of every dependent package type-checks under `tsgo --strict`.
- **Runtime: two dists.** The runtime's sources guard with the inline text `(typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__)`. `bun run build` runs `tsgo -b .` into `build/`, then `scripts/build-dists.ts` writes `dist/` and `dist-unchecked/`: each module that reads the guard is transformed by esbuild with `__METANO_CHECKS__` defined `true` / `false` and `minifySyntax` (the checked dist reads no global; the unchecked dist has no check code); every other file is copied unchanged into both. `package.json#exports` maps the **`metano-unchecked`** condition of every entry to `dist-unchecked/` (after `source` and `types`, before `import` / `default`).
- **Generated code: the define.** The lowered `foreach` reads the guard once into a function-local `const $c` (§ Lowered `foreach`, final); an app's bundler `define` (`__METANO_CHECKS__: false`) folds it. A fully unchecked app sets both the condition and the define (`docs/runtime-types.md`).
- **Folded in the runtime**: the bounds checks of `List.get`, `set`, `update`, `updateReturningOld`, `fromEnd`; every `_version` store of every class; every enumeration check (`List`, `Dictionary`, `KeyCollection`, `ValueCollection`, `HashSet`, `Queue`, `Stack` iterators, `List.forEach`). Never guarded: `insert`, `insertRange`, `removeAt`, `removeRange`, `getRange`, `slice`, `copyTo`, `withCapacity`, every `KeyNotFoundException`, `ArgumentNullException`, `ArgumentException`, `NotSupportedException`, empty `Queue` / `Stack`, the free helper `fromEnd(array, n)`.
- **Verified** (FR-043): the side-effect-only import of every module of both dists is 0 B under bun, esbuild and Rollup; the acceptance bundle of `test/tree-shaking/production-checks.test.ts` under bun, esbuild, Rollup (+ replace) and Vite; the packed-tarball smoke test imports both dists (`scripts/smoke-test-runtime-package.sh`).
- Superseded: the R0 text that had the runtime read the guard on every call (measured at 5–160 ns per call on the default build, R1) and that allowed `globalThis.__METANO_CHECKS__` as a switch.

## Cold throw helpers (`throw-helpers.ts`, FR-016)

Module-level functions returning `never`, each constructing the FR-013 class with .NET's exact message (probed), kept out of the hot members so those stay small and inlinable (research R17). Only `throwModified` is part of the C → R contract (the compiler emits it); the others are used by the runtime classes and by 011h's classes, which import them from `./throw-helpers.ts`.

| Function | Throws | Message (`(Parameter …)` is appended by the exception class) | Used by |
|---|---|---|---|
| `throwModified(): never` | `InvalidOperationException` | `Collection was modified; enumeration operation may not execute.` | lowered `foreach` (generated), every iterator except `Stack`'s, `List.forEach` |
| `throwEnumeratorModified(): never` | `InvalidOperationException` | `Collection was modified after the enumerator was instantiated.` | `Stack` iterator; 011h's sorted classes (same .NET text). Renamed from `throwStackModified` in R1 (W4-20); not emitted by the compiler |
| `throwOOR(actualValue?: number): never` | `ArgumentOutOfRangeException` (param `index`) | `Index was out of range. Must be non-negative and less than the size of the collection.` (+ `Actual value was <n>.` when given) | `List.get`, `set`, `update`, `updateReturningOld`, `fromEnd`, `removeAt`; 011h's `SortedList` index members pass the index, as .NET does |
| `throwIndexOutsideArray(): never` (011h) | `IndexOutOfRangeException` | `Index was outside the bounds of the array.` | `ImmutableArray.get`, native-array `fromEnd` |
| `throwInsertOOR(): never` | `ArgumentOutOfRangeException` (`index`) | `Index must be within the bounds of the List.` | `List.insert` |
| `throwIndexAtMostCount(): never` | `ArgumentOutOfRangeException` (`index`) | `Index was out of range. Must be non-negative and less than or equal to the size of the collection.` | `List.insertRange`, `indexOf(item, index)` |
| `throwNegative(paramName: string): never` | `ArgumentOutOfRangeException` (`paramName`) | `Non-negative number required.` | `getRange`, `removeRange`, `slice` (`index` / `count`), `withCapacity` (`capacity`) |
| `throwInvalidRange(): never` | `ArgumentException` | `Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.` | `getRange`, `removeRange`, `slice` |
| `throwArgumentNull(paramName: string): never` | `ArgumentNullException` (`paramName`) | `Value cannot be null.` | null `key` (every dictionary-shaped class), null `collection` / `match` / `converter` / `action` / `other` / `array` |
| `throwKeyNotFound(key: unknown): never` | `KeyNotFoundException` | ``The given key '<key>' was not present in the dictionary.`` (`<key>` by `messageValue`) | `Dictionary.get`, `update`, `updateReturningOld` |
| `throwDuplicateKey(key: unknown, paramName?: string): never` | `ArgumentException` | `An item with the same key has already been added. Key: <key>` | `Dictionary.add`, `of`, `from`; 011h: `SortedList.add` (`key` parameter), its copy constructor, `SortedDictionary.add` (the `[key, value]` pair text) |
| `throwFixedSize(): never` | `NotSupportedException` | `Collection was of a fixed size.` | fixed-size adopted `List`: `add`, `addRange`, `insert`, `insertRange`, `remove`, `removeAt`, `removeRange`, `removeAll` |
| `throwReadOnly(): never` | `NotSupportedException` | `Collection is read-only.` | fixed-size adopted `List.clear` (011h: `ReadOnlyList`) |
| `throwEmptyQueue(): never` | `InvalidOperationException` | `Queue empty.` | `Queue.dequeue`, `peek` |
| `throwEmptyStack(): never` | `InvalidOperationException` | `Stack empty.` | `Stack.pop`, `peek` |
| `throwCompareFailed(inner: unknown): never` | `InvalidOperationException`, `innerException` = the thrown value when it is an `Error` (no `toException`, which would pull its module into every `List` bundle) | `Failed to compare two elements in the array.` | `List.sort`, `binarySearch`: any exception the comparison throws, the default comparer's included |
| `throwNotComparable(): never` | `ArgumentException` | `At least one object must implement IComparable.` | `defaultCompare` |
| `throwArrayTooSmall(): never` | `ArgumentException` (`destinationArray`) | `Destination array was not long enough. Check the destination index, length, and the array's lower bounds.` | every `copyTo` but the sorted classes' own |
| `throwNegativeValue(paramName: string, value: number): never` (011h) | `ArgumentOutOfRangeException` (`paramName`, actual value) | `<name> ('<value>') must be a non-negative value.` | sorted classes' `copyTo` (`index`), `SortedList.withCapacity` (`capacity`) |
| `throwCollectionTooSmall(): never` (011h) | `ArgumentException` | `Destination array is not long enough to copy all the items in the collection. Check array index and length.` | `SortedSet` / `SortedDictionary` / `SortedList.copyTo` |
| `throwViewBoundsInverted(): never` (011h) | `ArgumentException` (`lowerValue`) | `Must be less than or equal to upperValue.` | `SortedSet.getViewBetween` |
| `throwArgumentOutOfRange(paramName: string): never` (011h) | `ArgumentOutOfRangeException` (`paramName`) | `Specified argument was out of the range of valid values.` | a `SortedSet` view's `add` (`item`), a nested view's bounds (`lowerValue` / `upperValue`) |
| `throwSortedListNested(): never` (011h) | `NotSupportedException` | `This operation is not supported on SortedList nested types because they require modifying the original SortedList.` | `SortedList.Keys` / `.Values` mutators |
| `throwKeyListWrite(): never` (011h) | `NotSupportedException` | `Mutating a key collection derived from a dictionary is not allowed.` | `SortedList.Keys[i] = …` |
| `throwConcurrencyLevel(): never` (011h) | `ArgumentOutOfRangeException` (`concurrencyLevel`) | `The concurrencyLevel argument must be positive, or -1 to indicate a default level.` | `ConcurrentDictionary.withCapacity` |
| `throwConcurrentDuplicateKey(): never` (011h) | `ArgumentException` | `The key already existed in the dictionary.` | `ConcurrentDictionary.add` (`IDictionary<K, V>.Add`) |
| `throwConcurrentSourceDuplicate(): never` (011h) | `ArgumentException` | `The source argument contains duplicate keys.` | `ConcurrentDictionary`'s copy constructor |

`requireKey(key)` (same module) returns `key`, or calls `throwArgumentNull("key")` for `null` / `undefined`: the one null-key check of every dictionary-shaped class (`Dictionary` and 011h's). The sorted classes' `copyTo` goes through `copy-to.ts`'s `copyIntoSorted` (`array`, `throwNegativeValue("index", …)`, `throwCollectionTooSmall`); `SortedListView.copyTo` through `copyInto` (.NET's `Array.Copy`).

`copyTo(array, -1)` throws `ArgumentOutOfRangeException` (`destinationIndex`) with `destinationIndex ('-1') must be greater than or equal to '0'.` and the actual value, as .NET's `List<T>.CopyTo` does (`throwNegativeDestinationIndex`). Every class shares that check, `throwArrayTooSmall` and `throwArgumentNull("destinationArray")`, so `HashSet`, `Queue`, `Stack` and the dictionary views keep `List`'s texts where .NET has its own (`HashSet`: `arrayIndex ('-1') must be a non-negative value. (Parameter 'arrayIndex')`; `Queue` / `Stack`: `Index was out of range. Must be non-negative and less than or equal to the size of the collection. (Parameter 'arrayIndex')`; the views: `Non-negative number required. (Parameter 'index')`; too small: `Destination array is not long enough …` for `HashSet` and the views, `Offset and length were out of bounds …` for `Queue` / `Stack`; null: parameter `array`). Same exception classes, different texts: a documented divergence (probed on .NET 2026-11-19). Values quoted in a message are formatted by `messageValue` (FR-016's caveat: JavaScript's text, so a record key quotes its `toString()`).

## Equality and comparison (`default-comparer.ts`, D18)

```ts
export type Comparison<T> = (a: T, b: T) => number;
export interface IEqualityComparer<T> { equals(a: T, b: T): boolean; getHashCode(value: T): number; }
export interface IComparer<T> { compare(a: T, b: T): number; }

export function elementEquals(a: unknown, b: unknown): boolean;   // EqualityComparer<T>.Default for collection elements and keys
export function elementHashCode(value: unknown): number;          // consistent with elementEquals
export function defaultCompare(a: unknown, b: unknown): number;   // Comparer<T>.Default.Compare
export const defaultEqualityComparer: IEqualityComparer<unknown>; // { equals: elementEquals, getHashCode: elementHashCode }
export const defaultComparer: IComparer<unknown>;                 // { compare: defaultCompare }
export function createComparer<T>(comparison: Comparison<T>): IComparer<T>; // Comparer<T>.Create(comparison): { compare: comparison }
```

- **`elementEquals(a, b)`** = `Array.isArray(a) || Array.isArray(b) ? a === b : defaultEquals(a, b)`, where `defaultEquals` is the existing `system/equality.ts` function: the same reference or both nullish; numbers by `===` or both `NaN` (SameValueZero, so `0` equals `-0`, as `double.Equals` does); a `Boxed` equals only a box of the same type and value (FR-051); a value with an `equals` method (records, structs, `Decimal`, Temporal values, user overrides) through it; a `Temporal.Duration` by length; a `Date` by time; **reference identity otherwise** (a class without `Equals`, a `Map`, a `Set`, a runtime collection, an array). It is never `system/equality.equals` (structural; research R14).
- **`elementHashCode(value)`**: `0` for `null` / `undefined`; an integer number by `value | 0`, any other number, a `string` and a `bigint` by a 31-multiplier hash of `String(value)` (so `0` and `-0` collide, as `elementEquals` requires); `boolean` by `1` / `0`; a symbol into one constant bucket; a function by identity; a `Boxed` by its raw value's hash; a value with `hashCode()` through it; a `Decimal` and a Temporal value (which declare `equals` but no `hashCode`) by the text hash of `String(value)` (decimal.js normalizes trailing zeros; Temporal's text includes the zone and calendar its `equals` compares); a `Duration` by its length in nanoseconds (by its text when it carries calendar units); a `Date` by its time; **any other value with `equals` but no `hashCode`** into one constant bucket (compared with `equals`: O(n) but correct); anything else (arrays and identity-equal objects) by an identity hash, a sequential number per object kept in a `WeakMap` created lazily on first use (no module-scope `new`, NFR-006). These hashes are internal to the collections; C#'s `GetHashCode()` keeps lowering to `valueHashCode`.
- The collections do not call `elementHashCode` for primitives or identity-keyed objects: `hash-core` keys those straight in a native `Map` / `Set` (O(1), SameValueZero = `elementEquals` for them) and indexes only the values whose equality is not identity (a value with `equals`, a box, a `Duration`, a `Date`) by `elementHashCode`. With a user `IEqualityComparer<T>` every key goes through the comparer. Enumeration order is insertion order in both paths.
- **`defaultCompare(a, b)`** (`Comparer<T>.Default`, shared by `List.sort`, `List.binarySearch` and 011h's sorted classes): `0` when `a === b`; `null` / `undefined` sorts before every value and equals itself; numbers as `double.CompareTo` (`NaN` before every number, `-0` equals `0`); `bigint` and `boolean` (`false < true`) by value; strings **ordinally** (UTF-16 code units, as `<`; D30); a `Boxed` by its raw value; a value with `compareTo(other)` (a lowered `IComparable<T>` / `IComparable`) through it; `Decimal` through `cmp`; a Temporal value through its constructor's `compare`; otherwise `throwNotComparable()`. Documented divergence: .NET's `Comparer<string>.Default` is culture-sensitive (`["b", "a", "B", "A"]` sorts `a, A, b, B` on .NET under ICU and `A, B, a, b` here), the order LINQ's `orderBy` already uses (`compareKeys`). `compareKeys` (LINQ) is not changed by 011c; folding it into `defaultCompare` is a follow-up (one rule per concept). `defaultCompare` itself throws the `ArgumentException`; `List.sort` / `binarySearch` (and 011h's `ImmutableList`) wrap it in `throwCompareFailed`, while the sorted classes surface it unwrapped, as .NET does.

### `hash-core.ts` (internal, frozen for 011h)

```ts
export class HashCore<K, V> {
  constructor(comparer?: IEqualityComparer<K> | null);
  readonly comparer: IEqualityComparer<K> | null;   // null = elementEquals / elementHashCode with the native Map fast path
  readonly size: number;
  has(key: K): boolean;
  get(key: K): V | undefined;                        // undefined only on a miss
  findKey(key: K): K | undefined;                    // the stored key equal to `key` (HashSet.tryGetValue, 011h TryGetKey)
  set(key: K, value: V): boolean;                    // true when inserted; an overwrite keeps the entry's position
  delete(key: K): boolean;
  clear(): void;
  clone(): HashCore<K, V>;                           // O(n): same comparer, same enumeration order
  [Symbol.iterator](): Iterator<[K, V]>;             // insertion order; never throws while the core changes (Map semantics: an entry deleted before it is reached is skipped, one added is yielded once)
}
```

No null-key validation and no version inside the core; the classes own both (the mutation-tolerant iterator is what `Dictionary`'s `remove` / `clear` exemption and `ConcurrentDictionary`'s non-throwing enumerator rely on). `HashSet<T>` stores its elements as `HashCore<T, T>`. A `hash-core.ts` instance lives in the owning class's `_store` field. **One lookup per operation (R2):** a key that can only equal itself (a primitive, a function, an object without value equality) with no user comparer goes straight to the native `Map`, one probe per member (`set` reports an insertion by comparing the `Map`'s size before and after); any other key resolves through the hash index first. The owners add one probe only where the API needs it: `Dictionary.get` / `tryGetValue` read `get` and call `has` only when the value read is `undefined` (a miss, or a stored `undefined`), and `add` / `tryAdd` test `has` before `set` (they must not overwrite). Key validation (`ArgumentNullException` for a `null` key) runs only in `Dictionary`, where .NET throws. The API above is unchanged.

### `set-algebra.ts` (internal, written by R)

```ts
export interface SetLike<T> extends Iterable<T> { readonly count: number; contains(item: T): boolean; }
// `distinct` builds a set with the receiver's comparer, so `other` is deduplicated by the receiver's rule
export function isSubsetOf<T>(set: SetLike<T>, other: Iterable<T>, distinct: (items: Iterable<T>) => SetLike<T>): boolean;
export function isSupersetOf<T>(set: SetLike<T>, other: Iterable<T>): boolean;
export function isProperSubsetOf<T>(set: SetLike<T>, other: Iterable<T>, distinct: (items: Iterable<T>) => SetLike<T>): boolean;
export function isProperSupersetOf<T>(set: SetLike<T>, other: Iterable<T>, distinct: (items: Iterable<T>) => SetLike<T>): boolean;
export function overlaps<T>(set: SetLike<T>, other: Iterable<T>): boolean;
export function setEquals<T>(set: SetLike<T>, other: Iterable<T>, distinct: (items: Iterable<T>) => SetLike<T>): boolean;
```

`HashSet`'s read-only set members delegate here; its mutating ones (`unionWith`, …) stay members (they touch the version). Each function throws `throwArgumentNull("other")` for a `null` `other`.

## Interfaces (`interfaces.ts`, FR-003)

```ts
// W4-13: each interface declares only what its C# interface declares (inherited members included),
// so a user C# class implementing IList<T> type-checks without synthesized members.
export interface IReadOnlyCollection<T> extends Iterable<T> {
  readonly count: number;
}
export interface IReadOnlyList<T> extends IReadOnlyCollection<T> {
  get(index: number): T;                                       // ArgumentOutOfRangeException
}
export interface ICollection<T> extends Iterable<T> {
  readonly count: number;
  readonly isReadOnly: boolean;
  add(item: T): void;
  clear(): void;
  contains(item: T): boolean;
  copyTo(array: T[], arrayIndex?: number): void;
  remove(item: T): boolean;
}
export interface IList<T> extends ICollection<T> {
  get(index: number): T;
  set(index: number, value: T): void;
  indexOf(item: T): number;
  insert(index: number, item: T): void;
  removeAt(index: number): void;
}
export interface IReadOnlyDictionary<K, V> extends IReadOnlyCollection<readonly [K, V]> {
  get(key: K): V;                                              // KeyNotFoundException; ArgumentNullException for null
  readonly keys: IReadOnlyCollection<K>;                       // C#: IEnumerable<K>
  readonly values: IReadOnlyCollection<V>;                     // C#: IEnumerable<V>
  containsKey(key: K): boolean;
  tryGetValue<D>(key: K, missingValue: D): readonly [found: true, value: V] | readonly [found: false, value: D];
}
export interface IDictionary<K, V> extends Iterable<readonly [K, V]> {
  readonly count: number;                                      // ICollection<KeyValuePair<K, V>>
  readonly isReadOnly: boolean;
  clear(): void;
  get(key: K): V;
  set(key: K, value: V): void;                                 // indexer write: insert or overwrite
  readonly keys: IReadOnlyCollection<K>;                       // C#: ICollection<K>; its mutators are not declared (they throw on .NET)
  readonly values: IReadOnlyCollection<V>;
  add(key: K, value: V): void;                                 // ArgumentException on a duplicate
  containsKey(key: K): boolean;
  remove(key: K): boolean;
  tryGetValue<D>(key: K, missingValue: D): readonly [found: true, value: V] | readonly [found: false, value: D];
}
export interface IReadOnlySet<T> extends IReadOnlyCollection<T> {
  contains(item: T): boolean;
  isSubsetOf(other: Iterable<T>): boolean;
  isSupersetOf(other: Iterable<T>): boolean;
  isProperSubsetOf(other: Iterable<T>): boolean;
  isProperSupersetOf(other: Iterable<T>): boolean;
  overlaps(other: Iterable<T>): boolean;
  setEquals(other: Iterable<T>): boolean;
}
export interface ISet<T> extends ICollection<T> {
  add(item: T): boolean;                                       // ISet<T>.Add returns bool; assignable to ICollection's void add
  unionWith(other: Iterable<T>): void;
  intersectWith(other: Iterable<T>): void;
  exceptWith(other: Iterable<T>): void;
  symmetricExceptWith(other: Iterable<T>): void;
  isSubsetOf(other: Iterable<T>): boolean;
  isSupersetOf(other: Iterable<T>): boolean;
  isProperSubsetOf(other: Iterable<T>): boolean;
  isProperSupersetOf(other: Iterable<T>): boolean;
  overlaps(other: Iterable<T>): boolean;
  setEquals(other: Iterable<T>): boolean;
}
// A view of a List's backing array (FR-056): index writes and the in-place members that keep the length;
// push / pop / shift / unshift / splice and a length assignment do not type-check (tsgo --strict, research R16).
export interface Span<T> extends ReadonlyArray<T> {
  [index: number]: T;
  sort(compareFn?: (a: T, b: T) => number): this;
  reverse(): this;
  fill(value: T, start?: number, end?: number): this;
  copyWithin(target: number, start: number, end?: number): this;
}
```

`fromEnd`, `update`, `updateReturningOld`, `getValueOrDefault`, and `indexOf` / `contains` on a read-only list are **class members only** (W4-13); an interface-typed receiver lowers through the interface members (§ Compiler contract, interface receivers). Which runtime class implements which interface follows .NET: `List` → `IList`, `ICollection`, `IReadOnlyList`, `IReadOnlyCollection`; `Dictionary` → `IDictionary`, `IReadOnlyDictionary` (and `IReadOnlyCollection<[K, V]>` structurally); `HashSet` → `ISet`, `IReadOnlySet`, `ICollection`, `IReadOnlyCollection`; `Queue`, `Stack`, `KeyCollection`, `ValueCollection` → `IReadOnlyCollection` only (.NET's `Queue<T>` / `Stack<T>` do not implement `ICollection<T>`; .NET's key / value collections do, with throwing mutators, which the runtime does not mirror: a C# `ICollection<K> c = dict.Keys` reports nothing in C# and fails `tsgo`, a rare shape listed as a follow-up). Arguments of type `Iterable<T>` accept any runtime collection, a native array, a `Set`, a generator or a LINQ pipe.

## Classes

Every class: `implements Iterable<T>` through `[Symbol.iterator]()` returning a small iterator **class** instance (no generator: 8–28× slower, research R17) that checks the version on every `next()`, the last one included; does not extend `Array` (`Array.isArray(x) === false`); exposes `count`; implements `toJSON()` and `toString()`; carries the lazy static `$runtimeType` getter of FR-018 (§ Type-argument descriptors). `toString()` returns .NET's `Type.ToString()` text built from the `$typeArguments$<Class>` slot (``System.Collections.Generic.List`1[System.Int32]``, ``System.Collections.Generic.Dictionary`2+KeyCollection[System.String,System.Int32]``, nested collection arguments in the same form); without a slot, or for an `UnknownType` argument, the argument prints as its C# type-parameter name (``List`1[T]``, ``Dictionary`2[TKey,TValue]``), an open question for FR-008 (§ Open points).

### Common signatures

Every factory and constructor takes one trailing optional `typeArguments?: readonly RuntimeType[] | null` (FR-008), passed by the compiler only when the compilation asks a runtime question about that collection kind. `items` / `entries` given to a **constructor** accept `null` / `undefined` (an empty collection, for hand-written TS); the **`from`** factories are the C# copy constructors and throw `ArgumentNullException` (`collection`) for `null`, as .NET does.

### `List<T>` (`list.ts`)

```ts
export class List<T> implements IList<T>, IReadOnlyList<T> {
  declare readonly _items: T[];
  declare _version: number;
  declare readonly $typeArguments$List: readonly RuntimeType[] | undefined;

  constructor(items?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null); // copies items in one pass
  static of<T>(items: T[], typeArguments?: readonly RuntimeType[] | null): List<T>;        // takes ownership of items (no copy)
  static from<T>(items: Iterable<T>, typeArguments?: readonly RuntimeType[] | null): List<T>; // copy; null → ArgumentNullException('collection')
  static withCapacity<T>(capacity: number, typeArguments?: readonly RuntimeType[] | null): List<T>; // negative → ArgumentOutOfRangeException('capacity')
  initAdd(items: readonly T[]): this;                         // initializer after a capacity / copy / comparer constructor: add semantics, chainable (no IrLet, FR-007)
  static adopt<T>(array: T[], options?: { readonly fixedSize?: boolean } | null, typeArguments?: readonly RuntimeType[] | null): List<T>; // the array becomes _items

  get count(): number;
  get isReadOnly(): boolean;                                   // false; true for a fixed-size adopted list
  get isFixedSize(): boolean;                                  // false; true for a fixed-size adopted list (non-generic IList.IsFixedSize)
  get(index: number): T;
  set(index: number, value: T): void;
  update(index: number, f: (current: T) => T): T;
  updateReturningOld(index: number, f: (current: T) => T): T;
  fromEnd(n: number): T;                                       // get(count - n)
  slice(start: number, end?: number): List<T>;                 // list[a..b]: getRange(start, (end ?? count) - start); absolute indices, no negative JS semantics
  getRange(index: number, count: number): List<T>;
  add(item: T): void;
  addRange(items: Iterable<T>): void;                          // AddRange(this) appends a snapshot, as .NET
  insert(index: number, item: T): void;
  insertRange(index: number, items: Iterable<T>): void;
  remove(item: T): boolean;
  removeAt(index: number): void;
  removeRange(index: number, count: number): void;
  removeAll(match: (item: T) => boolean): number;
  clear(): void;                                               // _items.length = 0
  contains(item: T): boolean;
  indexOf(item: T, index?: number): number;
  lastIndexOf(item: T): number;
  find(match: (item: T) => boolean): T | null;
  find<D>(match: (item: T) => boolean, defaultValue: D): T | D;
  findIndex(match: (item: T) => boolean): number;
  findLast(match: (item: T) => boolean): T | null;
  findLast<D>(match: (item: T) => boolean, defaultValue: D): T | D;
  findLastIndex(match: (item: T) => boolean): number;
  findAll(match: (item: T) => boolean): List<T>;
  exists(match: (item: T) => boolean): boolean;
  trueForAll(match: (item: T) => boolean): boolean;
  forEach(action: (item: T) => void): void;                    // checks the version after each call, as .NET
  convertAll<U>(converter: (item: T) => U): List<U>;
  binarySearch(item: T, comparison?: Comparison<T> | IComparer<T> | null): number; // ~insertionPoint when absent; C# passes an IComparer<T>
  sort(comparison?: Comparison<T> | IComparer<T> | null): void;  // told apart by typeof === "function"; in place; stable (.NET's introsort is not: equal elements may order differently, documented)
  reverse(): void;
  copyTo(array: T[], arrayIndex?: number): void;
  toArray(): T[];                                              // _items.slice(): detached copy (FR-055)
  asJsArray(): T[];                                            // _items itself (FR-056)
  asSpan(): Span<T>;                                           // _items itself
  asReadOnlySpan(): ReadonlyArray<T>;                          // _items itself
  toJSON(): T[];                                               // a detached copy
  toString(): string;
  [Symbol.iterator](): Iterator<T>;
}
```

- `get` / `set` / `update` / `updateReturningOld` / `fromEnd` test `(index >>> 0) >= this._items.length` (one unsigned compare, FR-057 divergence (4)) under the FR-058 guard and call `throwOOR()`; `removeAt` uses the same compare unguarded; `insert` accepts `0..count` (`throwInsertOOR`); `insertRange` and `indexOf(item, index)` accept `0..count` (`throwIndexAtMostCount`); `getRange` / `removeRange` / `slice` throw `throwNegative("index")` / `throwNegative("count")`, then `throwInvalidRange()`.
- `contains` / `indexOf` / `lastIndexOf` / `remove` use `elementEquals` with a `===` fast path for primitives (`NaN` is found, as .NET's `IndexOf(double.NaN)` finds it).
- `find` / `findLast` return `defaultValue` on a miss (the compiler passes C#'s `default(T)`: `0`, `false`, `null`, …); the one-argument overloads return `null` for hand-written TS. A `null` delegate throws `ArgumentNullException` (`match`, `converter`, `action`); a `null` collection argument `ArgumentNullException` (`collection`).
- `sort()` without a comparison uses `defaultCompare`; any exception the comparison throws (the default comparer's `ArgumentException` included) becomes `throwCompareFailed(e)`; a list of 0 or 1 element never calls the comparison (no throw, as .NET). `binarySearch` likewise.
- **Fixed-size adopted list** (FR-003): `List.adopt(arr, { fixedSize: true })` returns an instance of an internal subclass of `List` (not exported; same fields, built through `List`'s constructor path, its own static `$runtimeType` getter returning `List`'s) whose `add`, `addRange`, `insert`, `insertRange`, `remove`, `removeAt`, `removeRange`, `removeAll` call `throwFixedSize()` and `clear` calls `throwReadOnly()`; `set` / `update` / `sort` / `reverse` write through to the shared array and **do not bump `_version`** (a .NET array enumerator detects no write). `instanceof List` is `true` (documented divergence, spec Edge Cases). `List`'s own hot members carry no fixed-size flag test.
- **One backing array for life** (FR-057): `_items` is defined once (constructor or `adopt`) and every member mutates it in place. `asJsArray()`, `asSpan()`, `asReadOnlySpan()` and the free helpers return `_items` itself (`list.asSpan() === list.asJsArray()`).
- Runtime-built lists (`slice`, `getRange`, `findAll`, `convertAll`) copy the source's `$typeArguments$List` when present (`convertAll` passes none: its element type differs).
- Not implemented, `MS0001` at the call site (FR-001): `Capacity`, `EnsureCapacity`, `TrimExcess`, `AsReadOnly` (stage 011h, `asReadOnly(list)`), and the index / count overloads of `IndexOf(item, index, count)`, `LastIndexOf(item, index[, count])`, `FindIndex(start, …)`, `FindLastIndex(start, …)`, `Sort(index, count, comparer)`, `Reverse(index, count)`, `BinarySearch(index, count, item, comparer)`, `CopyTo(index, array, arrayIndex, count)`.

### `Dictionary<K, V>` (`dictionary.ts`), `KeyCollection<K>`, `ValueCollection<V>` (`dictionary-views.ts`)

```ts
export class Dictionary<K, V> implements IDictionary<K, V>, IReadOnlyDictionary<K, V> {
  declare readonly $typeArguments$Dictionary: readonly RuntimeType[] | undefined;

  constructor(entries?: Iterable<readonly [K, V]> | null, comparer?: IEqualityComparer<K> | null, typeArguments?: readonly RuntimeType[] | null); // add semantics
  static of<K, V>(entries: ReadonlyArray<readonly [K, V]>, comparer?: IEqualityComparer<K> | null, typeArguments?: readonly RuntimeType[] | null): Dictionary<K, V>;     // { { k, v } }: add, a duplicate throws
  static assign<K, V>(entries: ReadonlyArray<readonly [K, V]>, comparer?: IEqualityComparer<K> | null, typeArguments?: readonly RuntimeType[] | null): Dictionary<K, V>; // { [k] = v }: set, last wins
  static from<K, V>(source: Iterable<readonly [K, V]>, comparer?: IEqualityComparer<K> | null, typeArguments?: readonly RuntimeType[] | null): Dictionary<K, V>; // copy ctors; null → ArgumentNullException('collection'); duplicate → ArgumentException
  static withCapacity<K, V>(capacity: number, comparer?: IEqualityComparer<K> | null, typeArguments?: readonly RuntimeType[] | null): Dictionary<K, V>;
  initAdd(entries: ReadonlyArray<readonly [K, V]>): this;       // { { k, v } } after a capacity / copy / comparer constructor: add semantics
  initAssign(entries: ReadonlyArray<readonly [K, V]>): this;    // { [k] = v } after such a constructor: set semantics

  get count(): number;
  get keys(): KeyCollection<K>;                                // live view, cached (dict.keys === dict.keys, as .NET)
  get values(): ValueCollection<V>;
  get(key: K): V;
  set(key: K, value: V): void;
  update(key: K, f: (current: V) => V): V;
  updateReturningOld(key: K, f: (current: V) => V): V;
  add(key: K, value: V): void;
  tryAdd(key: K, value: V): boolean;
  remove(key: K): boolean;
  clear(): void;
  containsKey(key: K): boolean;
  containsValue(value: V): boolean;                            // elementEquals
  tryGetValue<D>(key: K, missingValue: D): readonly [found: true, value: V] | readonly [found: false, value: D];
  getValueOrDefault(key: K): V | null;
  getValueOrDefault<D>(key: K, defaultValue: D): V | D;
  toMap(): Map<K, V>;                                          // interop copy (hand-written TS)
  toJSON(): Record<string, V>;                                 // String(key) → value
  toString(): string;
  [Symbol.iterator](): Iterator<readonly [K, V]>;              // a fresh [key, value] tuple per step
}
// dictionary-views.ts (imports no Dictionary)
export interface KeyValueSource<K, V> extends Iterable<readonly [K, V]> { readonly count: number; containsKey(key: K): boolean; }
export class KeyCollection<K> implements IReadOnlyCollection<K> {
  constructor(source: KeyValueSource<K, unknown>);             // ArgumentNullException('dictionary') for null
  get count(): number; contains(key: K): boolean; copyTo(array: K[], arrayIndex?: number): void;
  toArray(): K[]; toJSON(): K[]; toString(): string; [Symbol.iterator](): Iterator<K>;
}
export class ValueCollection<V> implements IReadOnlyCollection<V> {
  constructor(source: KeyValueSource<unknown, V>);
  get count(): number; contains(value: V): boolean; copyTo(array: V[], arrayIndex?: number): void; // contains: elementEquals
  toArray(): V[]; toJSON(): V[]; toString(): string; [Symbol.iterator](): Iterator<V>;
}
```

- Keys and values key through `elementEquals` / `elementHashCode` (or the comparer) over `hash-core` (§ Equality). A record key with `equals` / `hashCode` addresses one entry; two instances of a class without them are two keys.
- `null` / `undefined` keys throw `throwArgumentNull("key")` from `get`, `set`, `update`, `updateReturningOld`, `add`, `tryAdd`, `remove`, `containsKey`, `tryGetValue`, `getValueOrDefault`; `get` / `update` on a missing key throw `throwKeyNotFound(key)`; `add` / `of` / `from` / the constructor throw `throwDuplicateKey(key)`.
- Enumeration order is insertion order, also after removals (.NET may refill a freed slot: `{1,2,3}`, `Remove(2)`, `[4] = 4` enumerates `1, 4, 3` on .NET and `1, 3, 4` here; documented divergence, OD-18). Entries removed during an enumeration and not yet visited are skipped, as .NET does.
- `keys` / `values` are live (FR-012): the views read through the source (`count`, `containsKey`, its iterator), so their enumeration follows the source's version rule (`Dictionary`'s here, 011h's for `SortedDictionary` / `ImmutableDictionary`).
- Not implemented, `MS0001`: `Comparer`, `EnsureCapacity`, `TrimExcess`, `Remove(key, out value)`, `GetAlternateLookup`.

### `HashSet<T>` (`hash-set.ts`, reworked)

```ts
export class HashSet<T> implements ISet<T>, IReadOnlySet<T> {
  declare readonly $typeArguments$HashSet: readonly RuntimeType[] | undefined;

  constructor(items?: Iterable<T> | null, comparer?: IEqualityComparer<T> | null, typeArguments?: readonly RuntimeType[] | null);
  static of<T>(items: readonly T[], comparer?: IEqualityComparer<T> | null, typeArguments?: readonly RuntimeType[] | null): HashSet<T>;  // initializers / collection expressions; duplicates collapse
  static from<T>(items: Iterable<T>, comparer?: IEqualityComparer<T> | null, typeArguments?: readonly RuntimeType[] | null): HashSet<T>; // null → ArgumentNullException('collection')
  static withCapacity<T>(capacity: number, comparer?: IEqualityComparer<T> | null, typeArguments?: readonly RuntimeType[] | null): HashSet<T>;
  initAdd(items: readonly T[]): this;                         // initializer after a capacity / copy / comparer constructor: add semantics, chainable (no IrLet, FR-007)

  get count(): number;
  get isReadOnly(): boolean;                                   // false
  add(item: T): boolean;                                       // was `this` (2.x); see § Legacy symbols
  remove(item: T): boolean;
  contains(item: T): boolean;
  clear(): void;
  tryGetValue<D>(equalValue: T, missingValue: D): readonly [found: true, value: T] | readonly [found: false, value: D];
  removeWhere(match: (item: T) => boolean): number;
  unionWith(other: Iterable<T>): void; intersectWith(other: Iterable<T>): void;
  exceptWith(other: Iterable<T>): void; symmetricExceptWith(other: Iterable<T>): void;
  isSubsetOf(other: Iterable<T>): boolean; isSupersetOf(other: Iterable<T>): boolean;
  isProperSubsetOf(other: Iterable<T>): boolean; isProperSupersetOf(other: Iterable<T>): boolean;
  overlaps(other: Iterable<T>): boolean; setEquals(other: Iterable<T>): boolean;
  copyTo(array: T[], arrayIndex?: number): void;
  toArray(): T[];                                              // detached copy, enumeration order (FR-055)
  toJSON(): T[];
  toString(): string;
  [Symbol.iterator](): Iterator<T>;
}
```

- Elements key through `elementEquals` / `elementHashCode` or the comparer; `null` is a valid element. A `null` `other` throws `throwArgumentNull("other")`. The set algebra compares `other`'s elements through this set's comparer, as .NET does. No view (FR-056). `system/equality.equals` is **not** taught about `HashSet` or `Dictionary` (D18).
- Not implemented, `MS0001`: `Comparer`, `EnsureCapacity`, `TrimExcess`, `CreateSetComparer`.

### `Queue<T>` (`queue.ts`)

```ts
export class Queue<T> implements IReadOnlyCollection<T> {
  declare readonly $typeArguments$Queue: readonly RuntimeType[] | undefined;
  constructor(items?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null);
  static from<T>(items: Iterable<T>, typeArguments?: readonly RuntimeType[] | null): Queue<T>;    // enqueues in order; null → ArgumentNullException('collection')
  static withCapacity<T>(capacity: number, typeArguments?: readonly RuntimeType[] | null): Queue<T>;
  get count(): number;
  enqueue(item: T): void;
  dequeue(): T;                                                // amortized O(1) (array + head index, R2); throwEmptyQueue
  peek(): T;                                                   // throwEmptyQueue
  tryDequeue<D>(missingValue: D): readonly [found: true, value: T] | readonly [found: false, value: D];
  tryPeek<D>(missingValue: D): readonly [found: true, value: T] | readonly [found: false, value: D];
  contains(item: T): boolean;  clear(): void;  copyTo(array: T[], arrayIndex?: number): void;
  toArray(): T[];                                              // detached copy, front to back
  toJSON(): T[];  toString(): string;
  [Symbol.iterator](): Iterator<T>;                            // front to back
}
```

Not implemented, `MS0001`: `EnsureCapacity`, `TrimExcess`.

### `Stack<T>` (`stack.ts`)

```ts
export class Stack<T> implements IReadOnlyCollection<T> {
  declare readonly $typeArguments$Stack: readonly RuntimeType[] | undefined;
  constructor(items?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null);
  static from<T>(items: Iterable<T>, typeArguments?: readonly RuntimeType[] | null): Stack<T>;    // pushes in order: new Stack(new[]{1,2,3}) enumerates 3,2,1
  static withCapacity<T>(capacity: number, typeArguments?: readonly RuntimeType[] | null): Stack<T>;
  get count(): number;
  push(item: T): void;
  pop(): T;                                                    // throwEmptyStack
  peek(): T;                                                   // throwEmptyStack
  tryPop<D>(missingValue: D): readonly [found: true, value: T] | readonly [found: false, value: D];
  tryPeek<D>(missingValue: D): readonly [found: true, value: T] | readonly [found: false, value: D];
  contains(item: T): boolean;  clear(): void;  copyTo(array: T[], arrayIndex?: number): void; // copyTo writes top first
  toArray(): T[];                                              // detached copy, top first
  toJSON(): T[];                                               // top first
  toString(): string;
  [Symbol.iterator](): Iterator<T>;                            // top first (C# semantics); throwEnumeratorModified
}
```

Not implemented, `MS0001`: `EnsureCapacity`, `TrimExcess`.

### Enumeration invalidation (`_version`, spec Edge Cases, probed on .NET 10)

A member that **bumps** `_version` makes a running enumeration (an iterator, a lowered `foreach`, `List.forEach`) throw on its next step; every store is under the FR-058 guard.

| Class | Bumps | Does not bump |
|---|---|---|
| `List` | `add`, `insert`, `set`, `update`, `updateReturningOld`, `removeAt`, `clear` (always, even when empty), `sort`, `reverse` (always); `addRange` / `insertRange` / `removeRange` / `removeAll` / `remove` only when they change something | reads; `addRange` / `insertRange` of nothing, `removeRange(i, 0)`, `removeAll` that removes nothing, `remove` of an absent item; every write of a fixed-size adopted list |
| `Dictionary` (and its views) | insertion of a new key: `add`, `set` / `tryAdd` of a new key | `set` / `update` / `updateReturningOld` of an existing key, `tryAdd` of an existing key, `remove`, `clear` (.NET Core 3.0+) |
| `HashSet` | `add` of a new item; `unionWith` / `symmetricExceptWith` when they add | `add` of a present item, `remove`, `clear`, `exceptWith`, `intersectWith`, `removeWhere` |
| `Queue` | `enqueue`, `dequeue`, a successful `tryDequeue`, `clear` (always) | `peek`, `tryPeek`, reads |
| `Stack` | `push`, `pop`, a successful `tryPop`, `clear` (always) | `peek`, `tryPeek`, reads |

A `List` enumeration also records `_items.length` at its start and treats a difference as a modification (FR-057), so a length change through `asJsArray()` is detected; an element write through a view bumps nothing.

## Helpers (`helpers.ts`)

```ts
export function fromEnd<T>(array: readonly T[], n: number): T;            // arr[^n] on T[] / JsArray<T>; IndexOutOfRangeException ("Index was outside the bounds of the array."), always checked
export function isIterable(value: unknown): value is Iterable<unknown>;    // Symbol.iterator present (strings included)
export function asArray(value: unknown): unknown[] | null;                 // a native array; the backing array of a fixed-size adopted List (isFixedSize); else null
export function asJsArray<T>(list: List<T> | null | undefined): T[];       // AsJsArray(); list._items; ArgumentNullException('list') for null (FR-056)
export function asSpan<T>(list: List<T> | null | undefined): Span<T>;      // AsSpan() / CollectionsMarshal.AsSpan(list); list._items; a fresh [] for null
export function asReadOnlySpan<T>(list: List<T> | null | undefined): ReadonlyArray<T>; // AsReadOnlySpan(); list._items; a fresh [] for null
export function asList<T>(value: unknown, targetType: string): IList<T> | null; // (IList<T>)v from a type that may hold a native array; see below
```

The C# calls lower to these free helpers (each tree-shakes on its own); the `List` members of the same names are the same views for hand-written TS.

**`asList`** (amended in C2 review rounds 1 and 2, supervisor decisions). An explicit cast `(IList<T>)v`, `(ICollection<T>)v`, `(IReadOnlyList<T>)v` or `(IReadOnlyCollection<T>)v` whose static source type is neither an array nor a stage 011c collection (or a type implementing a list interface itself), such as `object` or `IEnumerable<T>`, lowers to `asList(v, targetType)`, typed as the cast's target (`asList<T>(v, targetType) as IReadOnlyList<T>` where TypeScript needs it); `targetType` is the cast target's .NET type name (`Type.ToString()`, ``System.Collections.Generic.IList`1[System.Char]``), which the compiler writes. The source may hold a native array at run time, which C# would view as a fixed-size list:

- `null` / `undefined` → `null`;
- a native array → `List.adopt(v, { fixedSize: true })` (shares the array's storage, as the implicit conversion of FR-003);
- a primitive (a `string`, `number`, `boolean` or `bigint`), which is never a list → throws `InvalidCastException` with .NET's message, built as `castTo` builds it: ``Unable to cast object of type 'System.String' to type 'System.Collections.Generic.IList`1[System.Char]'.``;
- any other value → returned as-is, without a test: a user class implementing `IList<T>` has no collection descriptor in 3.0 (W4-17), so the helper cannot tell a valid list from an invalid one.

Documented divergence: an object that is not a list is not rejected at the cast (.NET throws `InvalidCastException` there); it fails later, at its first use as a list (the same as before 3.0). The `as` form (`v as IList<T>`) keeps `MS0001` (W4-17). `asList` is exported from the collections barrel, and from the root barrel through it.

## Descriptor factories (`descriptors.ts`, FR-017)

```ts
export function listType<T>(element: RuntimeType<T>): CollectionType<List<T>>;
export function dictionaryType<K, V>(key: RuntimeType<K>, value: RuntimeType<V>): CollectionType<Dictionary<K, V>>;
export function hashSetType<T>(element: RuntimeType<T>): CollectionType<HashSet<T>>;
export function queueType<T>(element: RuntimeType<T>): CollectionType<Queue<T>>;
export function stackType<T>(element: RuntimeType<T>): CollectionType<Stack<T>>;
export function iListType<T>(element: RuntimeType<T>): CollectionType<IList<T>>;               // invariant
export function iCollectionType<T>(element: RuntimeType<T>): CollectionType<ICollection<T>>;   // invariant
export function iReadOnlyListType<T>(element: RuntimeType<T>): CollectionType<IReadOnlyList<T>>;             // covariant
export function iReadOnlyCollectionType<T>(element: RuntimeType<T>): CollectionType<IReadOnlyCollection<T>>; // covariant
export function iDictionaryType<K, V>(key: RuntimeType<K>, value: RuntimeType<V>): CollectionType<IDictionary<K, V>>;             // invariant
export function iReadOnlyDictionaryType<K, V>(key: RuntimeType<K>, value: RuntimeType<V>): CollectionType<IReadOnlyDictionary<K, V>>; // invariant (as .NET)
export function iSetType<T>(element: RuntimeType<T>): CollectionType<ISet<T>>;                 // invariant
export function iReadOnlySetType<T>(element: RuntimeType<T>): CollectionType<IReadOnlySet<T>>; // invariant
```

- **Open literals (FR-018; amended in R1 by W4-24 (f)).** Each class's static `$runtimeType` getter returns a memoized literal that imports nothing from `descriptors.ts` or from `system/reflection` (types only): `{ kind: "collection", collection: "List", name: "List", fullName: "System.Collections.Generic.List", namespace: "System.Collections.Generic", ctor: List, typeArguments: [], variance: ["invariant"], interfaces: [{ collection: "IList", arguments: [0] }, { collection: "ICollection", arguments: [0] }, { collection: "IReadOnlyList", arguments: [0] }, { collection: "IReadOnlyCollection", arguments: [0] }], test: (v) => v instanceof List, closeFor: (instance) => closeCollectionFor(List.$runtimeType, instance) }` (the open definition; `typeArguments: []`). `closeFor` is the one non-data member: it closes the literal over the `$typeArguments$<Class>` slot an instance carries (the open literal itself when there is none), through `system/collections/close-collection.ts` (internal; memoized on the identity of the open literal and of each argument, `system/identity-memo.ts`). It is what lets `getRuntimeType` close a collection without importing it: a program that asks `GetType()` but builds no collection carries none of the closing code (the 011b row `isType + getRuntimeType + one class` stays within 1792 B), and `List` alone pulls nothing from `system/reflection` (a bundle test asserts both). No registration runs at load (FR-043). `interfaces` is data: one entry per implemented interface of § Interfaces' table, naming it by `collection` kind and mapping its arguments to indices of the instance's slot (`null` = wildcard, e.g. `Dictionary` as `IReadOnlyCollection<KeyValuePair<K, V>>`). `descriptors.ts` holds the open interface literals (`IList`, …) as module constants of the same data shape (no `ctor`, no `interfaces`). So a bundle that uses `List` pulls neither `descriptors.ts` nor reflection code; it does carry the closing code (`closeFor` is in the getter a used class keeps, research R13), which the NFR-008 rows measure.
- **Specialization.** `system/reflection` gains `specializeCollection(open, typeArguments)` (`specialize-collection.ts`). A class kind closes through its own `closeFor` (given a carrier object holding the slot), so `specializeCollection(List.$runtimeType, [e])`, `listType(e)` and `getRuntimeType` of an instance built with `[e]` are one object; an interface kind is memoized on the identity of the open literal and of each argument (`memoNodeFor`), returning `{ ...open, name: "List<String>", fullName: "System.Collections.Generic.List<System.String>", typeArguments, test }` (the `specialize` naming convention). Its `test`: for a class kind (all invariant), `v instanceof open.ctor` and, per argument, identity against `v.$typeArguments$<collection>`; for an interface kind, nominal through the class literal's `interfaces` (from `v.constructor` up, the nearest constructor with an own `$runtimeType` of kind `collection` whose `interfaces` name `open.collection`), then each argument through the entry's index mapping; an absent slot, a `null` mapping or `UnknownType` on either side matches anything. An interface descriptor imports no class; a 011h class joins the test by listing the interface in its literal. A user class implementing a collection interface has a `class` literal (not `collection`), so the test is `false` for it: a documented divergence and follow-up (§ Documented divergences).
- **Factories.** `listType(e) = specializeCollection(List.$runtimeType, [e])`, the interface factories `specializeCollection(<open interface literal>, args)`. Memoization is the reflection memo, so `listType(StringType) === listType(StringType)` and `Type ==` is `===`; the memo root is a module-level `let` filled on first call (no module-scope `new`).
- `CollectionType` amendment (contracts/runtime-types.md, D30): optional `interfaces: ReadonlyArray<{ readonly collection: CollectionKindName; readonly arguments: ReadonlyArray<number> | null }>` on class kinds. 011h's classes follow the same rules.
- 011h adds `immutableListType`, `readOnlyListType`, `immutableHashSetType`, `immutableDictionaryType`, `sortedSetType`, `sortedDictionaryType`, `sortedListType`, `concurrentDictionaryType` and `iImmutableListType`, `iImmutableSetType`, `iImmutableDictionaryType` to `descriptors.ts` in the same shape.

## Type-argument descriptors (FR-008) and `GetType()`

**J fix round 1 (#335).** `List.$runtimeType` is declared as `RuntimeType<List<unknown>>`,
while its value remains the same collection literal. This lets a reached C# subclass
override the getter with its own `ClassType` without TS2417/TS4114. The descriptor
factories narrow the open descriptor to `kind: "collection"` before specializing it.
A class descriptor's `base` thunk names class descriptors only; collection-interface
tests walk the constructor chain to the nearest collection literal instead.
Reference nullable annotations are erased from generic argument descriptors;
`Nullable<T>` for a value type keeps its `nullableType` descriptor.

The slot `$typeArguments$<Class>` (`List`, `Dictionary`, `HashSet`, `Queue`, `Stack`) is defined once by the class's constructor from the trailing `typeArguments` argument (or `undefined`); a C# subclass that closes the arguments (`class IntList : List<int>`) passes them to `super` and defines no slot. Runtime-built collections copy the source's slot (§ `List`). Each class's static getter `$runtimeType` returns its open data literal (§ Descriptor factories). `getRuntimeType(v)` (stage 011b) gains one rule: when the constructor's own descriptor has a `closeFor` member, it returns `descriptor.closeFor(v)`: the descriptor closed over the instance's slot, or the open literal (whose `test` accepts every instance: the wildcard of FR-008) when there is none. `getRuntimeType` imports nothing for it (reflection never imports `collections`). So `new List<int>()` built with `[Int32Type]` answers `GetType() == typeof(List<int>)` as `true`.

## JSON collection kinds (runtime half of FR-028, W4-5)

`system/json/types.ts` gains two generic descriptor shapes; `system/json/converters.ts` reads and writes them with two generic cases (array form, object form) and **imports no collection class**: the context writes a `create` closure that builds the class (with the FR-008 type arguments when the compilation asks, which is group J's). 011h widens the `kind` unions with its kinds.

```ts
export type SequenceCollectionDescriptor = {      // JSON array
  readonly kind: "list" | "queue" | "stack" | "hashSet";
  readonly element: TypeDescriptor;
  readonly create: (items: unknown[]) => Iterable<unknown>;
};
export type KeyedCollectionDescriptor = {         // JSON object; keys parsed by dictionary-keys.ts (#479)
  readonly kind: "dictionary";
  readonly key: TypeDescriptor;
  readonly value: TypeDescriptor;
  readonly create: (entries: Array<readonly [unknown, unknown]>) => Iterable<readonly [unknown, unknown]>;
};
```

Read: the element (or key and value) converters run first, then `create` receives the values in document order. Write: iteration (`for..of`) of any iterable, a dictionary entry as `String(keyConverter.serialize(k))` → value (the `map` key rule).

| Kind | `create` the context writes | Behaviour (matches .NET 10, probed) |
|---|---|---|
| `list` | `(items) => List.of(items)` | array in list order |
| `hashSet` | `(items) => HashSet.from(items)` | duplicates collapse (`[1,1,2]` → count 2) |
| `queue` | `(items) => Queue.from(items)` | written front to back; read enqueued in order (`peek()` is the first element) |
| `stack` | `(items) => Stack.from(items)` | written top first (`[3,2,1]` for pushes 1, 2, 3); read pushed in array order, so `[1,2,3]` reads with `peek() === 3` (a round trip reverses the stack on both sides) |
| `dictionary` | `(entries) => Dictionary.assign(entries)` | a repeated key keeps the **last** value (`{"1":1,"01":2}` as `Dictionary<int,int>` reads one entry `1 → 2`) |

**Kind per C# type** (System.Text.Json's rule, for C's context choice): `List<T>`, `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` → `list` (an interface-typed member reads into a `List`, as STJ creates a `List<T>`); `Dictionary<K, V>`, `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>` → `dictionary` (reads into a `Dictionary`); `HashSet<T>`, `ISet<T>`, `IReadOnlySet<T>` → `hashSet` (reads into a `HashSet`); `Queue<T>` → `queue`; `Stack<T>` → `stack`; `T[]`, `JsArray<T>` and `IEnumerable<T>` → `array` (STJ reads an `IEnumerable<T>` member into a `List<T>`; the TS value is the `Iterable<T>` the type maps to, so a native array satisfies it, unchanged from 2.x).

Transition (W4-6): `hashSet` keeps accepting today's descriptor without `create` (read through `new HashSet(items)`, the import `converters.ts` has today) until C's flip makes the context write `create`; C then makes `create` required and drops that import. `array` stays for `T[]` / `JsArray<T>`; `map` (`MapDescriptor`) stays until C's flip, which removes it. Element / key / value conversion, the null-element rule (#471), the container-depth rule and custom converters are today's.

## Lowered `foreach` over `List<T>` (FR-057, normative emission; final)

`foreach (var x in <receiver>)` whose receiver's static type is `List<T>` (or a class deriving from it) lowers to exactly this shape (`$l` … `$i` follow the printer's temporary naming rules; nested loops get distinct names). **This block is final** (W4-27, maintainer decision): the guard is read once into the function-local `$c`, which bun and esbuild fold under `define: { __METANO_CHECKS__: "false" }` (measured in R1) and which costs one global read per loop, not per element. The guard of the runtime members (`get`, `set`, the version stores) is still pending the checks-mechanism decision; it does not change this emission.

```ts
{
  const $c = typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__;
  const $l = <receiver>;
  const $a = $l._items;
  const $n = $a.length;
  const $v = $c ? $l._version : 0;
  for (let $i = 0; ; $i++) {
    if ($c && ($l._version !== $v || $a.length !== $n)) throwModified();
    if ($i === $n) break;
    const x = $a[$i];
    /* body */
  }
}
```

`$c` is a `const` local to the enclosing function (never module scope, FR-058) and MUST be the block's first declaration: esbuild substitutes a folded `const` only while no statement with a possible side effect precedes it in its scope (measured in R1: with `$c` after `$l._items`, esbuild keeps `c=!1` and the guarded `_version` reads; bun folds both orders). Reading the flag before the receiver changes no observable order (`typeof` and a read of the flag have no effect). The `$v` capture is guarded too, so that with the checks defined `false` no `_version` read survives (FR-058's acceptance: a property read is not removable dead code). A `null` receiver throws at `$l._items` (a `TypeError`, `NullReferenceException` through `toException`). `break`, `return` and `throw` leave with no further check; `continue` reaches the next step's check.

## Compiler contract (C → R): C# member → emitted TypeScript

`r` is the receiver, `T` / `K` / `V` the C# type arguments rendered as TS types, `$targs` the trailing `typeArguments` argument (an array literal of descriptors, `[Int32Type]`), emitted **only** under FR-008, and `default(X)` the value C# would store (`0`, `false`, `null`, `0n`, …), which the compiler already computes for `out` / default slots. Every mapping is a plain member rename (FR-007: no template with two `$this`), a factory, or a free helper; a receiver with side effects is evaluated once because it is the single receiver of the call. A row marked *direct form* follows FR-010 (receiver and index reusable, or bound once to a temporary in statement position).

### `List<T>` receivers (and classes deriving from it)

| C# | Emitted TS |
|---|---|
| `new List<T>()` | `new List<T>()` (`new List<T>(null, $targs)` under FR-008) |
| `new List<T>(capacity)` | `List.withCapacity<T>(capacity)` / `(capacity, $targs)` |
| `new List<T>(source)` | `List.from(source)` / `(source, $targs)` |
| `new List<T>(capacity) { a, b }` / `new List<T>(source) { a, b }` | `List.withCapacity<T>(capacity).initAdd([a, b])` / `List.from(source).initAdd([a, b])` (`$targs` on the factory) |
| `new List<T> { a, b }`, `List<T> xs = [a, b, ..c]` | `List.of([a, b])`, `List.of([a, b, ...c])` / `(…, $targs)` |
| `r.Count` | `r.count` |
| `r[i]` (read) | `r.get(i)` |
| `r[i] = v` | `r.set(i, v)` |
| `r[i] op= v`, `r[i]++` / `--` in statement position | *direct form* `r.set(i, r.get(i) op v)` |
| `r[i] op= v`, `r[i]++` / `--` whose value is used (expression position) | `r.update(i, (v$) => v$ op v)` / `r.updateReturningOld(i, (v$) => v$ + 1)`, or the comma shapes of § Indexer compound assignments |
| `r[i] ??= v` (statement or expression position) | the `??=` shape of § Indexer compound assignments (never `update`, which always writes) |
| `r[^n]` (read) | `r.fromEnd(n)` |
| `r[^n] = v` | `r.set(r.count - n, v)` with `r` bound once when not reusable |
| `r[a..b]`, `r[a..]`, `r.Slice(start, length)` | `r.slice(a, b)`, `r.slice(a)`, `r.getRange(start, length)`; a `^n` bound renders `r.count - n` (receiver bound once) |
| `r.Add(x)` / `AddRange(xs)` / `Insert(i, x)` / `InsertRange(i, xs)` | `r.add(x)` / `r.addRange(xs)` / `r.insert(i, x)` / `r.insertRange(i, xs)` |
| `r.Remove(x)` / `RemoveAt(i)` / `RemoveRange(i, n)` / `RemoveAll(p)` / `Clear()` | `r.remove(x)` / `r.removeAt(i)` / `r.removeRange(i, n)` / `r.removeAll(p)` / `r.clear()` |
| `r.Contains(x)` / `IndexOf(x)` / `IndexOf(x, i)` / `LastIndexOf(x)` | `r.contains(x)` / `r.indexOf(x)` / `r.indexOf(x, i)` / `r.lastIndexOf(x)` |
| `r.Find(p)` / `FindLast(p)` | `r.find(p, default(T))` / `r.findLast(p, default(T))` |
| `r.FindIndex(p)` / `FindLastIndex(p)` / `FindAll(p)` / `Exists(p)` / `TrueForAll(p)` / `ForEach(a)` / `ConvertAll(f)` | `r.findIndex(p)` / `r.findLastIndex(p)` / `r.findAll(p)` / `r.exists(p)` / `r.trueForAll(p)` / `r.forEach(a)` / `r.convertAll(f)` |
| `r.Sort()` / `Sort(comparison)` / `Sort(comparer)` | `r.sort()` / `r.sort(comparison)` / `r.sort(comparer)` |
| `r.BinarySearch(x)` / `BinarySearch(x, comparer)` | `r.binarySearch(x)` / `r.binarySearch(x, comparer)` |
| `r.Reverse()` (the `List<T>` instance method) | `r.reverse()` |
| `r.GetRange(i, n)` | `r.getRange(i, n)` |
| `r.CopyTo(arr)` / `CopyTo(arr, i)` | `r.copyTo(arr)` / `r.copyTo(arr, i)` |
| `r.IsReadOnly` (via `ICollection<T>`) | `r.isReadOnly` |
| `r.ToArray()`, `r.ToJsArray()` | `r.toArray()` |
| `r.AsJsArray()` / `r.AsSpan()` / `CollectionsMarshal.AsSpan(r)` / `r.AsReadOnlySpan()` | `asJsArray(r)` / `asSpan(r)` / `asSpan(r)` / `asReadOnlySpan(r)` |
| `JsArray.From(r)` | `Array.from(r)` |
| `jsArray.AsList()` | `List.adopt(jsArray)` |
| `r.GetEnumerator()` | `r[Symbol.iterator]()` |
| `e.MoveNext()` / `e.Current` / `e.Reset()` on an enumerator | `MS0001`, as in 2.x (`Enumerators.cs`: only iterating the result is supported) |
| `r.ToString()`, `$"{r}"` | `r.toString()` |
| `foreach (var x in r)`, `r` statically `List<T>` | the lowered loop (§ Lowered `foreach`) + `throwModified` import |
| `foreach (var (a, b) in r)` / a positional pattern, `r` statically `List<T>` | the lowered loop with the element binding `const [a, b] = $a[$i];` (a discard prints a hole: `foreach (var (_, b) in r)` → `const [, b] = $a[$i];`) (a record deconstruction applies the existing `Deconstruct` lowering to `$a[$i]`) |
| `foreach (var x in r)`, `r` an interface | `for (const x of r)` |
| array → `IList<T>` / `ICollection<T>` / `IReadOnlyList<T>` / `IReadOnlyCollection<T>` (implicit or explicit) | `List.adopt(arr, { fixedSize: true })` / `(arr, { fixedSize: true }, $targs)` |
| `(T[])r`, `r is T[]`, `r as T[]` | through `asArray(r)` (`asArray(r) !== null`, …; the `Array` category, FR-021) |
| array / `List<T>` → `IEnumerable<T>` | nothing |
| `Capacity`, `EnsureCapacity`, `TrimExcess`, the index / count overloads listed under `List<T>` | `MS0001` |

### `Dictionary<K, V>` receivers, `KeyCollection`, `ValueCollection`

| C# | Emitted TS |
|---|---|
| `new Dictionary<K, V>()` / `(capacity)` | `new Dictionary<K, V>()` / `Dictionary.withCapacity<K, V>(capacity)` |
| `new Dictionary<K, V>(comparer)` / `(capacity, comparer)` | `new Dictionary<K, V>(null, comparer)` / `Dictionary.withCapacity<K, V>(capacity, comparer)` |
| `new Dictionary<K, V>(source)` / `(source, comparer)` (`IDictionary` or `IEnumerable<KeyValuePair>`) | `Dictionary.from(source)` / `Dictionary.from(source, comparer)` |
| `new Dictionary<K, V> { { k, v } }` / `{ [k] = v }` | `Dictionary.of([[k, v]])` / `Dictionary.assign([[k, v]])` |
| `new Dictionary<K, V>(comparer) { { k, v } }` / `{ [k] = v }` | `Dictionary.of([[k, v]], comparer)` / `Dictionary.assign([[k, v]], comparer)` |
| `new Dictionary<K, V>(capacity[, comparer]) { … }` / `new Dictionary<K, V>(source[, comparer]) { … }` | `Dictionary.withCapacity<K, V>(capacity[, comparer]).initAdd([[k, v]])` (`initAssign` for `{ [k] = v }`) / `Dictionary.from(source[, comparer]).initAdd(…)` |
| `$targs` under FR-008 | appended as the last argument of each form above, with `null` for a skipped comparer (`new Dictionary<K, V>(null, null, $targs)`, `Dictionary.of([[k, v]], null, $targs)`) |
| `r.Count` / `r.Keys` / `r.Values` | `r.count` / `r.keys` / `r.values` |
| `r[k]` / `r[k] = v` | `r.get(k)` / `r.set(k, v)` |
| `r[k] op= v`, `r[k]++` in statement position | *direct form* `r.set(k, r.get(k) op v)` |
| `r[k] op= v`, `r[k]++` whose value is used (expression position) | `r.update(k, (v$) => v$ op v)` / `r.updateReturningOld(k, …)`, or the comma shapes of § Indexer compound assignments |
| `r[k] ??= v` | the `??=` shape of § Indexer compound assignments |
| `r.Add(k, v)` / `TryAdd(k, v)` / `Remove(k)` / `Clear()` | `r.add(k, v)` / `r.tryAdd(k, v)` / `r.remove(k)` / `r.clear()` |
| `r.ContainsKey(k)` / `ContainsValue(v)` | `r.containsKey(k)` / `r.containsValue(v)` |
| `r.TryGetValue(k, out var v)` | `const [found, v] = r.tryGetValue(k, default(V))` (the `IrOutArgumentCall` lowering, unchanged) |
| `r.GetValueOrDefault(k)` / `(k, d)` | `r.getValueOrDefault(k, default(V))` / `r.getValueOrDefault(k, d)` |
| `r.Keys.Count` / `r.Values.Count` / `r.Keys.Contains(k)` | `r.count` / `r.count` / `r.containsKey(k)` (`DictionaryKeysContainsRewriter`, FR-012) |
| `r.Values.Contains(v)` (LINQ) | the LINQ mapping (`ValueCollection.contains` is for hand-written TS) |
| `keys.CopyTo(arr, i)` / `values.CopyTo(arr, i)` | `keys.copyTo(arr, i)` / `values.copyTo(arr, i)` |
| `foreach (var (k, v) in r)`, `kv.Key` / `kv.Value` | `for (const [k, v] of r)`, `kv[0]` / `kv[1]` (unchanged) |
| `r.GetEnumerator()` / `r.ToString()` | `r[Symbol.iterator]()` / `r.toString()` |
| `Comparer`, `EnsureCapacity`, `TrimExcess`, `Remove(k, out v)` | `MS0001` |

### `HashSet<T>` receivers

| C# | Emitted TS |
|---|---|
| `new HashSet<T>()` / `(capacity)` / `(comparer)` / `(capacity, comparer)` | `new HashSet<T>()` / `HashSet.withCapacity<T>(capacity)` / `new HashSet<T>(null, comparer)` / `HashSet.withCapacity<T>(capacity, comparer)` |
| `new HashSet<T>(source)` / `(source, comparer)` | `HashSet.from(source)` / `HashSet.from(source, comparer)` |
| `new HashSet<T>(capacity[, comparer]) { a }` / `new HashSet<T>(source[, comparer]) { a }` | `HashSet.withCapacity<T>(capacity[, comparer]).initAdd([a])` / `HashSet.from(source[, comparer]).initAdd([a])` |
| `new HashSet<T> { a, b }`, `HashSet<T> s = [a, b]` / `new HashSet<T>(comparer) { a, b }` | `HashSet.of([a, b])` / `HashSet.of([a, b], comparer)` |
| `$targs` under FR-008 | the last argument (`new HashSet<T>(null, null, $targs)`) |
| `r.Count` / `Add(x)` / `Remove(x)` / `Contains(x)` / `Clear()` | `r.count` / `r.add(x)` / `r.remove(x)` / `r.contains(x)` / `r.clear()` |
| `r.TryGetValue(x, out var actual)` | `const [found, actual] = r.tryGetValue(x, default(T))` |
| `r.RemoveWhere(p)`, `UnionWith`, `IntersectWith`, `ExceptWith`, `SymmetricExceptWith`, `IsSubsetOf`, `IsSupersetOf`, `IsProperSubsetOf`, `IsProperSupersetOf`, `Overlaps`, `SetEquals`, `CopyTo` | the camelCase member of the same name |
| `r.GetEnumerator()` / `r.ToString()` / `foreach` | `r[Symbol.iterator]()` / `r.toString()` / `for..of` |
| `Comparer`, `EnsureCapacity`, `TrimExcess`, `CreateSetComparer` | `MS0001` |

### `Queue<T>`, `Stack<T>`

| C# | Emitted TS |
|---|---|
| `new Queue<T>()` / `(capacity)` / `(source)` | `new Queue<T>()` / `Queue.withCapacity<T>(capacity)` / `Queue.from(source)`; `$targs` last under FR-008 |
| `q.Count` / `Enqueue(x)` / `Dequeue()` / `Peek()` / `Contains(x)` / `Clear()` / `ToArray()` / `CopyTo(arr, i)` | `q.count` / `q.enqueue(x)` / `q.dequeue()` / `q.peek()` / `q.contains(x)` / `q.clear()` / `q.toArray()` / `q.copyTo(arr, i)` |
| `q.TryDequeue(out var x)` / `TryPeek(out var x)` | `const [ok, x] = q.tryDequeue(default(T))` / `q.tryPeek(default(T))` |
| `new Stack<T>()` / `(capacity)` / `(source)` | `new Stack<T>()` / `Stack.withCapacity<T>(capacity)` / `Stack.from(source)`; `$targs` last under FR-008 |
| `s.Count` / `Push(x)` / `Pop()` / `Peek()` / `Contains(x)` / `Clear()` / `ToArray()` / `CopyTo(arr, i)` | `s.count` / `s.push(x)` / `s.pop()` / `s.peek()` / `s.contains(x)` / `s.clear()` / `s.toArray()` / `s.copyTo(arr, i)` |
| `s.TryPop(out var x)` / `TryPeek(out var x)` | `const [ok, x] = s.tryPop(default(T))` / `s.tryPeek(default(T))` |
| `GetEnumerator()` / `ToString()` / `foreach` (both) | `[Symbol.iterator]()` / `toString()` / `for..of` (`Stack` top first, so `Stack.GetEnumerator` is now mapped, FR-007) |
| `EnsureCapacity`, `TrimExcess` | `MS0001` |

### Indexer compound assignments (FR-010, normative shapes)

For a class or interface receiver, list or dictionary. In **statement position** with a reusable receiver and index the direct form stays `r.set(i, r.get(i) op v)`. Everywhere else (a non-reusable receiver or index, or a used value) the bridge declares `let $r, $i, $o, $t;` (only the ones needed, distinct names per site) at the head of the **nearest enclosing statement list of the same function** (an expression-bodied arrow becomes a block body) and assigns them **inline**, in a comma expression at the original position, so C#'s evaluation order (receiver, index, read, right-hand side, write) and every short-circuit (`&&`, `||`, `?:`, `??`, a lambda body, a skipped argument) are kept. A reusable receiver or index is used directly instead of a temporary.

| C# | Shape (value of the expression last) |
|---|---|
| `r[i]++` / `r[i]--` (value used) | `($r = R(), $i = I(), $o = $r.get($i), $r.set($i, $o + 1), $o)` |
| `++r[i]` / `--r[i]` / `r[i] op= v` (value used) | `($r = R(), $i = I(), $r.set($i, $o = $r.get($i) op v), $o)` |
| `r[i] op= v` / `r[i]++` in statement position, non-reusable parts | `$r = R(), $i = I(), $r.set($i, $r.get($i) op v);` |
| `r[i] ??= v` (any position) | `($r = R(), $i = I(), $r.get($i) ?? ($r.set($i, $t = v), $t))`: `v` is evaluated and written only when the current value is `null` / `undefined`, as C#, so a running enumeration sees no write (and no `_version` bump) otherwise |

The same shapes apply with a key (`$k`) to `Dictionary` / `IDictionary`; a missing key throws `KeyNotFoundException` at the read, as the C# read does. For a **class** receiver, `update` / `updateReturningOld` remain an allowed lowering of `op=` and `++` / `--` in expression position: they are an ordinary call at the original position, so the receiver and index are evaluated in place and in order, `f` runs after the read, and nothing runs when the branch is skipped (order and short-circuit kept); they always write, which is what `op=` and `++` do. They are **not** used for `??=`, which must not write when the value is non-null, nor when the right-hand side contains `await` or `yield` (a closure cannot hold them; the comma shape is used). **Placement without an enclosing statement list:** for a field or static initializer and for constructor `base(...)` / `this(...)` arguments, the temporaries are declared in the smallest enclosing function body that evaluates the expression (the constructor body for an instance initializer, emitted before the initializer runs; a static initializer moves into the class's static block); if none exists the site reports `MS0001`. Increment constants and overflow follow the existing increment lowering of the element type (`1n` and the wrapping / checked rules for `long`, `ulong`, `BigInteger`, the int rules for 32-bit types).

### Interface receivers (`IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>`, `ISet<T>`, `IReadOnlySet<T>`; W4-13)

Only the interface members of § Interfaces exist on the receiver, so the class-only conveniences lower through them, with the temporaries and shapes of § Indexer compound assignments (never an IIFE, FR-009).

| C# | Emitted TS |
|---|---|
| `r.Count`, `r[i]`, `r[i] = v`, `r[k]`, `r[k] = v`, and every member of the C# interface | the camelCase member (`r.count`, `r.get(i)`, `r.set(i, v)`, …) |
| `r[^n]` | `r.get(r.count - n)` (`r` bound once when not reusable) |
| `r[^n] = v` | `r.set(r.count - n, v)` (likewise) |
| `r[i] op= v`, `r[i]++`, `r[i] ??= v`, `r[k] op= v`, … | the shapes of § Indexer compound assignments (no `update` member on an interface) |
| `r.TryGetValue(k, out var v)` | `const [found, v] = r.tryGetValue(k, default(V))` |
| `r.GetValueOrDefault(k)` / `(k, d)` (`IReadOnlyDictionary` extension) | `r.tryGetValue(k, default(V))[1]` / `r.tryGetValue(k, d)[1]` |
| `r.Contains(x)` on `IReadOnlyList<T>` / `IReadOnlyCollection<T>` | the LINQ `contains` terminal (`Enumerable.Contains`). It compares with `===` today (`system/linq/terminals.ts`), not `elementEquals`, so a record element is not found by value: a pre-existing divergence, listed as a follow-up for group C (route LINQ `contains` through `elementEquals`) |
| a C# class implementing one of these interfaces | `implements` the TS interface; it declares exactly the C# members, no synthesized ones |

### `params` collections (C# 13)

| C# | Emitted TS |
|---|---|
| declaration `void F(params List<T> xs)` / `params IEnumerable<T> xs` / `params HashSet<T> xs` | an ordinary parameter of the mapped type (`xs: List<T>` / `Iterable<T>` / `HashSet<T>`), not a rest parameter |
| call `F(a, b)` with `params List<T>` / `IList<T>` / `ICollection<T>` | `F(List.of([a, b]))` (C# builds a `List<T>`) |
| call with `params IEnumerable<T>` / `IReadOnlyList<T>` / `IReadOnlyCollection<T>` | `F([a, b])` for `IEnumerable<T>`; `F(List.adopt([a, b], { fixedSize: true }))` for the read-only interfaces (C# builds a read-only array; documented divergence: the TS value accepts an indexer write) |
| call with `params HashSet<T>` | `F(HashSet.of([a, b]))` |
| call passing a collection explicitly (`F(xs)`) | `F(xs)` |
| `params T[]` / `params ReadOnlySpan<T>` | unchanged |

### Comparers, helpers and type questions

| C# | Emitted TS |
|---|---|
| `EqualityComparer<T>.Default` / `Comparer<T>.Default` (as a value) | `defaultEqualityComparer` / `defaultComparer` |
| `Comparer<T>.Create(comparison)` | `createComparer(comparison)` |
| `EqualityComparer<T>.Default.Equals(a, b)` | unchanged: `defaultEquals(a, b)` of `system/equality` (#674; not the collection rule) |
| `Comparer<T>.Default.Compare(a, b)` | `defaultCompare(a, b)` |
| a user `IEqualityComparer<T>` / `IComparer<T>` implementation | a class with `equals` / `getHashCode` / `compare` members (the interfaces of `default-comparer.ts`) |
| `arr[^n]` on `T[]` / `JsArray<T>` | `fromEnd(arr, n)` |
| `typeof(List<string>)`, `is` / `as` / pattern against a collection type (011b's collection category) | `listType(StringType)` (…) through `isType` / `asType` / `castTo` |

## Legacy symbols kept until group C's flip (W4-6)

The 2.x compiler emits these; the runtime keeps them, unchanged in behaviour, until C's flip stops emitting them, and C deletes them in that hand-in (H deletes `ImmutableCollection` in 011h). Runtime-internal callers (`system/linq`, `system/json`) move to the new members in R1.

| Symbol | Module | Kept as | Removed by |
|---|---|---|---|
| `listRemove` | `list-helpers.ts` | is | C (flip) |
| `immutableInsert`, `immutableRemoveAt`, `immutableRemove` | `list-helpers.ts` | is (used by `ImmutableCollections.cs`) | H (011h compiler half) |
| `addEntry` (free function over a native `Map`) | `dictionary-helpers.ts` | is | C (flip) |
| `tryGetValue`, `getValueOrDefault` (free functions over a native `Map`) | `dictionary-helpers.ts` | is (the 011h receivers still lower through them over a native `Map` until 011h; W4-31) | H |
| `ImmutableCollection` | `immutable-collection.ts` | is | H |
| `HashSet.size`, `has`, `delete`, `values()`, `forEach` | `hash-set.ts` | members of the reworked class, `@deprecated`, delegating to `count` / `contains` / `remove` / the iterator | C (flip) |
| `HashSet` constructor `(items?)` | `hash-set.ts` | source-compatible: `items` stays the first parameter | — |
| `MapDescriptor` / JSON kind `map` | `system/json` | is | C (flip: the context emits `dictionary`) |

One 2.x behaviour changes in R1 although C has not flipped: **`HashSet.add` returns `boolean`** instead of `this`. In 2.x a C# `if (set.Add(x))` was always true (the truthy `this`, a silent wrong value that the `boolean` fixes), and no generated or hand-written code in the repository chains `.add(…).add(…)` (searched); a hand-written consumer that chains breaks (migration notes).

## LINQ terminals (group C)

`toList()` → a new `List<T>`; `toArray()` → `T[]`; `toDictionary(...)` → `Dictionary`; `toHashSet(...)` → `HashSet`, in the term shape of `system/linq/terminals.ts`. Every operator keeps accepting `Iterable<T>` and stays a standalone function (ADR-0012). Group C writes them over the classes above; R does not add them.

## Documented divergences decided in R0 review (W4-13)

- **`List.sort` is stable**; .NET's introsort is not, so elements that compare equal may end in a different order (a stable order is one of the orders .NET may produce, never a wrong sort).
- **`Dictionary` enumerates in insertion order after a removal**; .NET reuses the freed slot, so an entry added after a removal may enumerate in the removed entry's position (`{1,2,3}`, `Remove(2)`, `[4] = 4`: .NET `1, 4, 3`, here `1, 3, 4`; OD-18, § `Dictionary<K, V>`).
- **`copyTo` messages** of `HashSet`, `Queue`, `Stack` and the dictionary views are `List`'s (§ Cold throw helpers).

- **`toString()` without a slot** prints the type-parameter names (``List`1[T]``); `ToString()` is not a reachability trigger.
- **`Comparer<string>.Default`** is ordinal (as LINQ's `orderBy`); .NET's is culture-sensitive.
- **`Dictionary.from(null)`** names `collection` for both copy constructors; .NET names `dictionary` for the `IDictionary` overload.
- **User implementers of the collection interfaces** (W4-17, D31): `x is IList<int>` (and `as` and patterns) reports `MS0001` and lowers to the throwing placeholder only where a transpiled or referenced-package type implementing the interface can be the operand (D31 states the conversion rule); otherwise the test lowers normally. An overload dispatcher's branch keeps `instanceof` over the runtime classes, so a user implementer does not match it (the behaviour before 3.0). A user-class entry form that would make both nominal is a follow-up.
- **`params IReadOnlyList<T>` / `IReadOnlyCollection<T>` / `IEnumerable<T>`**: .NET passes a synthesized read-only array (`<>z__ReadOnlyArray`1`; an empty call passes `T[]`), so through `IList<T>` an indexer write, `Add` and `Clear` throw `NotSupportedException` (`Specified method is not supported.`) and `IsReadOnly` is `true`; `xs is IList<T>` is `true` for all three. TS passes `List.adopt([…], { fixedSize: true })` for the two read-only interfaces (an indexer write succeeds, `add` throws `Collection was of a fixed size.`, `clear` `Collection is read-only.`) and a native array for `IEnumerable<T>` (`xs is IList<T>` is `false`). Probed on .NET; follow-up: pass 011h's `ReadOnlyList` once it exists.
- **`initAdd` / `initAssign` and `Dictionary.of`** evaluate every element of the initializer before the first add (the array literal is built first), so a duplicate key is reported after the later elements' side effects have run; C# interleaves each element's evaluation with its `Add`.
- **`ICollection<K> c = dict.Keys`** fails `tsgo` (the views implement `IReadOnlyCollection` only); follow-up.
- **`default(T)` for a user struct** passed to `tryGetValue` / `tryDequeue` / `tryPeek` / `tryPop` / `find` / `findLast` / `getValueOrDefault` is evaluated at every call (a struct lowered to a class allocates a zeroed instance each time, hit or miss).

## 011h classes

The sections below are stage 011h's (group H), unchanged by R0; they build on the 011c API above (`hash-core.ts`, `defaultCompare`, `elementEquals` / `elementHashCode`, the interfaces, the throw helpers).

### Stage 011h classes (FR-054): shared rules

`ReadOnlyList`, the immutable classes and their builders, the sorted classes and `ConcurrentDictionary` follow every rule of § Classes (no `#private`; non-enumerable string-keyed storage defined through `Object.defineProperty` in the constructor, always the same fields in the same order, the per-class slot `$typeArguments$<Class>` last and `undefined` without descriptors; `count`; `toJSON()`; `[Symbol.iterator]`; the lazy `$runtimeType` literal getter with its `interfaces` data and `closeFor` member (W4-32); `toString()` through `collectionTypeName` (``System.Collections.Generic.SortedSet`1[System.Int32]``); `Array.isArray(x) === false`). Enumerators are iterator objects (no generators, no closures; research R17). The exceptions are the classes of `system/exceptions/*`; the throw helpers and messages below are pinned by the 011h runtime tests and by conformance rows against .NET 10 (the messages quoted here were read from .NET 10.0 on 2026-11-19).

**Modules** (each under `metano-runtime/system/collections/`, re-exported by the `system/collections` barrel unless marked internal; internal modules are reachable only through the `./system/collections/*` wildcard and are not public API):

| Module | Exports | Imports from 011c (R0) |
|---|---|---|
| `read-only-list.ts` | `ReadOnlyList`, `asReadOnly` | interfaces, throw helpers |
| `immutable-interfaces.ts` | `IImmutableList`, `IImmutableSet`, `IImmutableDictionary` (types only, 0 B) | interfaces |
| `immutable-list.ts` | `ImmutableList` (over `ImmutableListCore`) | `ImmutableListCore` |
| `immutable-list-builder.ts` | `ImmutableListBuilder`, `toImmutableList` (over `ImmutableListBuilderCore`) | `ImmutableListBuilderCore`, `ImmutableList` |
| `immutable-list-core.ts` (internal) | `ImmutableListCore` (the abstract base of `ImmutableList` and `ImmutableArray`: storage, tip-append, every shared member, and the protected result factory `derive(items, count)`) | `ownsTip`, `comparisonOf`, `elementEquals`, `binarySearch`, throw helpers |
| `immutable-array.ts` | `ImmutableArray` (a sibling of `ImmutableList` over `ImmutableListCore`, W4-66) | `ImmutableListCore`, throw helpers |
| `immutable-list-builder-core.ts` (internal) | `ImmutableListBuilderCore` (the abstract base of the two list-shaped builders) | `List`, `builder-iterator.ts` |
| `immutable-array-builder.ts` | `ImmutableArrayBuilder`, `toImmutableArray` (a sibling of `ImmutableListBuilder` over `ImmutableListBuilderCore`) | `ImmutableListBuilderCore`, `ImmutableArray` |
| `immutable-hash-set.ts` | `ImmutableHashSet` | `immutable-storage.ts`, `set-algebra.ts`, `defaultEqualityComparerFor` |
| `immutable-hash-set-builder.ts` | `ImmutableHashSetBuilder`, `toImmutableHashSet` | `HashSet`, `ImmutableHashSet`, `builder-iterator.ts` |
| `immutable-dictionary.ts` | `ImmutableDictionary` | `immutable-storage.ts`, `KeyCollection` / `ValueCollection`, `defaultEqualityComparerFor` |
| `immutable-dictionary-builder.ts` | `ImmutableDictionaryBuilder`, `toImmutableDictionary` | `Dictionary`, `ImmutableDictionary`, `builder-iterator.ts` |
| `immutable-storage.ts` (internal) | `ownsTip` (W4-11's one ownership rule), `ImmutableHashStorage` (the append-only keyed store of the two hashed immutables: an entry array plus a `HashCore` from key to position), `normalizedComparer` | `hash-core` |
| `builder-iterator.ts` (internal) | `BuilderIterator` (the three builders' enumerator, checked against the builder's own version) | `throwModified` |
| `sorted-set.ts` | `SortedSet` | `comparisonOf`, `defaultComparer`, `set-algebra.ts`, `copyIntoSorted`, `closeCollectionFor`, `collectionTypeName`, throw helpers |
| `sorted-dictionary.ts` | `SortedDictionary` | `comparisonOf`, `defaultComparer`, `elementEquals`, `KeyCollection` / `ValueCollection`, `copyIntoSorted`, `closeCollectionFor`, `collectionTypeName`, throw helpers (`requireKey`) |
| `sorted-list.ts` | `SortedList`, `SortedListView` | `comparisonOf`, `defaultComparer`, `elementEquals`, `binarySearch`, `copyInto` / `copyIntoSorted`, `closeCollectionFor`, `collectionTypeName`, throw helpers (`requireKey`) |
| `red-black-tree.ts` (internal) | `RedBlackTree`, `successor`, `predecessor` (shared by `SortedSet` and `SortedDictionary`; a pure ordered map over a `Comparison<T>`: no exceptions, no version) | — |
| `sorted-array.ts` (internal) | `binarySearch` (.NET's `Array.BinarySearch`, over R's `Comparison<T>`): the one .NET-order search, used by `SortedList` and `List.binarySearch` (W4-15) | — |
| `concurrent-dictionary.ts` | `ConcurrentDictionary` | `hash-core`, `ReadOnlyList` |

`ImmutableList` never imports its builder (the builder module imports `ImmutableList`), so the graph is acyclic and `ImmutableList` alone pulls no `List`.

**Equality and order.** Hashed classes key through `hash-core` with `elementEquals` / `elementHashCode` or the `IEqualityComparer<T>` argument (D18), never through `system/equality`; a value with `equals` but no `hashCode` (`Decimal`, the temporal types) gets a value hash or a constant bucket from `elementHashCode`, never an identity hash, so equal values meet. Sorted classes order and identify elements by the comparer alone (`compare(a, b) === 0` is "equal", as .NET), defaulting to `defaultCompare` (`Comparer<T>.Default`). **Misses (D30).** A `try*` / `find*` member and `getValueOrDefault` take the C# `default(T)` of the result from the caller (the compiler passes it: `0`, `false`, `null`, …) and return it on a miss, so the tuple form is `readonly [boolean, T]` and no `null` stands in for a value type. `SortedSet.Min` / `Max` follow the same rule through `minOr(defaultValue)` / `maxOr(defaultValue)`. `null` elements and keys follow .NET: allowed in `SortedSet` and the immutable sets, `ArgumentNullException` (`Value cannot be null. (Parameter 'key')`) for a `null` key of every dictionary-shaped class.

**Enumeration rules.** `SortedSet`, `SortedDictionary` and `SortedList` keep a version counter (every store under FR-058's guard); which members bump it is per class and probed (each class's section: most single-element writes bump even as a no-op; `remove` on an empty `SortedSet` / `SortedDictionary` and `SortedList.remove` of an absent key do not), and the next step of a live enumerator throws `InvalidOperationException` with the sorted classes' own .NET message, `Collection was modified after the enumerator was instantiated.` (`throwEnumeratorModified`, the text .NET's `Stack<T>` shares; not `List`'s). `SortedDictionary.remove` / `clear` invalidate (unlike `Dictionary`). The immutable classes have nothing to invalidate. `ReadOnlyList` enumerates through its source (a `List` source throws on modification, an array does not, as .NET). `ConcurrentDictionary` never throws (below). Builders enumerate through their inner mutable class and inherit its rule.

**Ownership rule (tip-append sharing, FR-054, research R18, decision W4-11).** One rule, `ownsTip(store, count)` in `immutable-storage.ts`, used by `ImmutableList` and by `ImmutableHashStorage.append` (the hashed classes): a version may append in place only when its length equals the shared storage's length (it is the newest version of that storage). Every other mutator (`remove`, `setItem`, `clear`, `insert`, `sort`, …), and an `add` on an older version, copies the version's visible part into new storage. Shared storage is append-only: nothing is removed from it or overwritten in place, so each version reads exactly what it was built with. An old version keeps the whole shared store alive (a memory cost, not a semantic one). Builders copy at `ToImmutable` and never share their storage with an immutable version, so mutating a builder after `ToImmutable` changes no immutable value. A seeded model-based test keeps many live versions, branches from old ones and compares every live version with a plain-copy reference after every step (`test/system/collections/model/`).

**Mutable-interface conversions of immutables.** In .NET, `ImmutableList<T>` also implements `IList<T>` (and the immutable sets / dictionary `ISet<T>` / `IDictionary<K,V>`) with mutators that throw `NotSupportedException` (`Specified method is not supported.`). The runtime classes cannot carry both meanings of `add`, so their TS shape implements only `IReadOnly*` and `IImmutable*`, while their class literal's `interfaces` list (§ Class literals of the 011h classes) names the mutable interfaces as .NET does, so the descriptor test `o is IList<T>` answers `true` as in .NET (W4-66). A binding form would then open a silent hole (`if (o is IList<int> l) l.Add(5)` throws `NotSupportedException` in .NET; in TS `add` would return a new list and drop it), so the immutable kinds are implementers **whose shape does not implement the mutable interface** (marked in the compiler's `RuntimeCollections` table): the compiler reports `MS0001` at a static conversion of an immutable type to `ICollection<T>`, `IList<T>`, `ISet<T>` or `IDictionary<K,V>`, and J's W4-65 rule treats these kinds as it treats user implementers, so an `is` / `as` / pattern / cast to one of those four interfaces reports `MS0001` whenever an immutable kind can be the operand (its static type can hold one). Overload dispatchers keep excluding the immutables from the mutable-interface rows (documented: a dispatcher never routes an immutable value to an `IList<T>` overload).

### What 011h takes from 011c (R0 frozen, R1 integrated)

011h defines no second comparer, hash table, throw helper, copy check, interface or descriptor shape (one rule per concept); it uses 011c's names: `default-comparer.ts` (`elementEquals`, `elementHashCode`, `defaultCompare`, `defaultComparer`, `defaultEqualityComparer`, `createComparer`, `comparisonOf`; types `Comparison`, `ComparisonArgument`, `IComparer`, `IEqualityComparer`), `hash-core.ts` (`HashCore`), `set-algebra.ts` (`SetLike`, `Distinct` and the six predicates), `dictionary-views.ts` (`KeyCollection`, `ValueCollection` over `KeyValueSource`), `interfaces.ts` (W4-13: only C#'s members), `throw-helpers.ts` (§ Cold throw helpers, with 011h's rows and `requireKey`), `copy-to.ts` (`copyInto`, and 011h's `copyIntoSorted`), `collection-type-name.ts` (`toString()`), `close-collection.ts` (the `closeFor` member of every class literal, W4-32), the storage ABI (inline `Object.defineProperty` fields, `$typeArguments$<Class>`), and the FR-058 guard text, which `scripts/build-dists.ts` folds in every module that reads it (`dist/` checked, `dist-unchecked/` without checks; iterators read `_version` only under the guard, as 011c's do). `List.binarySearch` delegates to `sorted-array.ts`'s `binarySearch`, the one .NET-order search (W4-15).

### `ReadOnlyList<T>` (`ReadOnlyCollection<T>`; `read-only-list.ts`)

```ts
export class ReadOnlyList<T> implements IList<T>, IReadOnlyList<T> {
  constructor(source: IList<T> | readonly T[], typeArguments?: readonly RuntimeType[] | null); // new ReadOnlyCollection<T>(list): ArgumentNullException (list)
  static fromArray<T>(array: readonly T[], typeArguments?): ReadOnlyList<T>;  // Array.AsReadOnly(array): ArgumentNullException (array)
  static empty<T>(): ReadOnlyList<T>;                 // ReadOnlyCollection<T>.Empty; one instance, built on first read
  static get $runtimeType(): CollectionType<ReadOnlyList<unknown>>;
  readonly count: number;                             // live: the source's count / length
  readonly isReadOnly: boolean;                       // true
  get(index: number): T;  fromEnd(n: number): T;      // ArgumentOutOfRangeException (Parameter 'index'), also over an array source (.NET's array IList<T>)
  indexOf(item: T): number;  contains(item: T): boolean;  copyTo(array: T[], arrayIndex?: number): void;   // the source's (an array source: elementEquals, copyInto)
  set / update / updateReturningOld / add / insert / remove / removeAt / clear: never   // NotSupportedException (`Collection is read-only.`, throwReadOnly)
  toJSON(): T[];  toString(): string;
  [Symbol.iterator](): Iterator<T>;                   // the source's own enumerator
}
export function asReadOnly<T>(list: List<T>): ReadOnlyList<T>;  // list.AsReadOnly(); a null list throws NullReferenceException, as the .NET member call does
```

Storage, in order: `_source`, `$typeArguments$ReadOnlyList`. A live view: reads go to the source (an array source is read as `a[i]` after the unsigned bounds compare), so a later `list.Add` is visible; enumeration is the source's (a `List` source throws on modification, an array does not, as .NET). No version of its own.

### `ImmutableList<T>` (`ImmutableList<T>`, `ImmutableArray<T>`, OD-17; `immutable-list.ts`)

```ts
export class ImmutableList<T> implements IImmutableList<T>, IReadOnlyList<T> {
  constructor(items: T[], count: number, typeArguments: readonly RuntimeType[] | undefined); // internal: built only by the factories and mutators (public only so the class can be the literal's `ctor`)
  static empty<T>(): ImmutableList<T>;                // ImmutableList<T>.Empty / ImmutableArray<T>.Empty / Create(); one lazily memoized instance
  static of<T>(items: T[], typeArguments?: readonly RuntimeType[] | null): ImmutableList<T>;  // Create(params T[]), collection expressions; takes ownership of the array (the compiler passes a fresh literal), as List.of
  static from<T>(items: Iterable<T>, typeArguments?: readonly RuntimeType[]): ImmutableList<T>; // CreateRange, ToImmutableList(), ToImmutableArray(); returns `items` itself when it is an ImmutableList (as .NET)
  readonly count: number;  readonly isEmpty: boolean;
  get(index: number): T;  fromEnd(n: number): T;      // ArgumentOutOfRangeException (`Specified argument was out of the range of valid values. (Parameter 'index')`)
  slice(start: number, end?: number): ImmutableList<T>;  getRange(index: number, count: number): ImmutableList<T>;
  indexOf(item: T, index?: number, count?: number, comparer?: IEqualityComparer<T> | null): number;
  lastIndexOf(item: T, index?: number, count?: number, comparer?: IEqualityComparer<T> | null): number;  contains(item: T): boolean;
  find(match: (item: T) => boolean, defaultValue: T): T;  findIndex(match): number;  findLast(match, defaultValue: T): T;  findLastIndex(match): number;
  findAll(match): ImmutableList<T>;  exists(match): boolean;  trueForAll(match): boolean;  forEach(action: (item: T) => void): void;
  convertAll<U>(converter: (item: T) => U): ImmutableList<U>;
  binarySearch(item: T, comparison?: ((x: T, y: T) => number) | IComparer<T> | null): number;
  add(item: T): ImmutableList<T>;  addRange(items: Iterable<T>): ImmutableList<T>;
  insert(index: number, item: T): ImmutableList<T>;  insertRange(index: number, items: Iterable<T>): ImmutableList<T>;
  remove(item: T, comparer?: IEqualityComparer<T> | null): ImmutableList<T>;  removeAll(match: (item: T) => boolean): ImmutableList<T>;
  removeAt(index: number): ImmutableList<T>;  removeRange(index: number, count: number): ImmutableList<T>;
  removeItems(items: Iterable<T>, comparer?: IEqualityComparer<T> | null): ImmutableList<T>;  // RemoveRange(IEnumerable<T>)
  replace(oldValue: T, newValue: T, comparer?: IEqualityComparer<T> | null): ImmutableList<T>;  // ArgumentException when absent
  setItem(index: number, value: T): ImmutableList<T>;  clear(): ImmutableList<T>;
  sort(comparison?: ((x: T, y: T) => number) | IComparer<T> | null): ImmutableList<T>;  reverse(): ImmutableList<T>;
  copyTo(array: T[], arrayIndex?: number): void;  // ArgumentNullException (array); a negative index or a short array: ArgumentOutOfRangeException (arrayIndex), .NET's default text
  toArray(): T[];  toJSON(): T[];
  [Symbol.iterator](): Iterator<T>;
}
```

Storage: `_items`, `_count`, `$typeArguments$ImmutableList`. **Tip-append copy-on-write** (FR-054, research R18): the backing array may be shared by several versions and be longer than a version's `_count`; each version reads only its first `_count` elements. `add` / `addRange` on the newest version of an array (`_items.length === _count`) push in place and return a new instance over the same array (amortized O(1)); every other mutator, and `add` on an older version, copies its visible elements (O(n)). A version never sees an element added after it. Mutators return the receiver exactly where .NET does (probed on .NET 10): `remove` / `removeItems` / `removeAll` that remove nothing, `removeRange(i, 0)`, `reverse()` of at most one element; `addRange` / `insertRange` of an empty source, `setItem` of an equal value and `sort()` return a **new** list, as .NET's; `clear()` returns `empty()` (the same instance as `ImmutableList<T>.Empty`). `slice` / `getRange` / mutator results copy the `$typeArguments$ImmutableList` slot. `ToBuilder()` lowers to `new ImmutableListBuilder(list)`, so `ImmutableList` imports no builder.

### `ImmutableArray<T>` (`immutable-array.ts`; amendment of OD-17 and W4-66, 011h compiler half)

```ts
export class ImmutableArray<T> extends ImmutableListCore<T, ImmutableArray<T>> {   // a SIBLING of ImmutableList<T>, never its subclass
  static empty<T>(): ImmutableArray<T>;  static of<T>(items: T[], typeArguments?): ImmutableArray<T>;  static from<T>(items: Iterable<T>, typeArguments?): ImmutableArray<T>;
  static fromRange<T>(items: readonly T[], start: number, length: number, typeArguments?): ImmutableArray<T>;  // Create(T[], int, int): validates as .NET, then copies
  // members whose behaviour differs from ImmutableList's, each probed on .NET 10 (2026-11-19):
  get(index: number): T;  fromEnd(n: number): T;    // IndexOutOfRangeException (`Index was outside the bounds of the array.`), a negative index included
  copyTo(array: T[], arrayIndex?: number): void;    // ArgumentNullException (destinationArray); negative index: ArgumentOutOfRangeException
                                                    // `destinationIndex ('-1') must be greater than or equal to '0'. (Parameter 'destinationIndex')` + `Actual value was -1.`;
                                                    // short array: ArgumentException `Destination array was not long enough. Check the destination index, length, and the array's lower bounds. (Parameter 'destinationArray')`
  binarySearch(item: T, comparison?): number;       // a comparer failure wrapped: InvalidOperationException `Failed to compare two elements in the array.` (throwCompareFailed)
  sort(comparison?): ImmutableArray<T>;             // the receiver when already sorted
  indexOf(item: T, index?: number, count?: number, comparer?): number;  // parameter name `startIndex`
  lastIndexOf(item: T, index?: number, count?: number, comparer?): number; // parameter names `startIndex`, `count`
  getRange(index: number, count: number): ImmutableArray<T>; // Slice: parameter names `start`, `length`
  removeRange(index: number, count: number): ImmutableArray<T>; // parameter names `index`, `length`
  addRange(items: Iterable<T>): ImmutableArray<T>;  // the receiver for an empty source
  toString(): string;                               // ``System.Collections.Immutable.ImmutableArray`1[T]``
}
```

OD-17 stays one implementation: the internal `ImmutableListCore<T, Self>` (`immutable-list-core.ts`, not re-exported by the barrel) holds the storage (`_items`, `_count`, then the class's own `$typeArguments$ImmutableList` / `$typeArguments$ImmutableArray` slot), tip-append and every shared member, and builds each mutator result through its protected `derive(items, count)`, which each class implements, so every result of an `ImmutableArray` is an `ImmutableArray` and every result of an `ImmutableList` an `ImmutableList`. The two classes are siblings (W4-66): `instanceof ImmutableList` is `false` on an `ImmutableArray`, as `is ImmutableList<T>` in .NET, and a dispatcher over both does not depend on branch order. The probes showed the two types differ observably (exception types and texts above, `ToString()`, the builder below), which the original OD-17 note did not know. `ImmutableArray.from` copies (it returns `items` itself only when `items` is an `ImmutableArray`); `fromRange` throws `ArgumentNullException` (`items`) and, for a `start` / `length` outside the array, `ArgumentOutOfRangeException` with .NET's parameter names and texts (probed by the conformance rows), and evaluates each argument once. `IsDefault` / `IsDefaultOrEmpty` / `ItemRef` / `AsSpan` / `AsMemory` stay `MS0001`.

### `ImmutableHashSet<T>` (`immutable-hash-set.ts`)

```ts
export class ImmutableHashSet<T> implements IImmutableSet<T>, IReadOnlySet<T> {
  constructor(…);  // internal, as ImmutableList's
  static empty<T>(): ImmutableHashSet<T>;
  static of<T>(items: readonly T[], comparer?: IEqualityComparer<T> | null, typeArguments?): ImmutableHashSet<T>;  // Create(...)
  static from<T>(items: Iterable<T>, comparer?: IEqualityComparer<T> | null, typeArguments?): ImmutableHashSet<T>; // CreateRange, ToImmutableHashSet(); `items` itself when it is an ImmutableHashSet with that comparer
  readonly count: number;  readonly isEmpty: boolean;  readonly keyComparer: IEqualityComparer<T>;
  contains(item: T): boolean;  tryGetValue(equalValue: T, defaultValue: T): readonly [boolean, T];
  add(item: T): ImmutableHashSet<T>;  remove(item: T): ImmutableHashSet<T>;  clear(): ImmutableHashSet<T>;
  union(other: Iterable<T>): ImmutableHashSet<T>;  intersect(other: Iterable<T>): ImmutableHashSet<T>;
  except(other: Iterable<T>): ImmutableHashSet<T>;  symmetricExcept(other: Iterable<T>): ImmutableHashSet<T>;
  withComparer(comparer: IEqualityComparer<T> | null): ImmutableHashSet<T>;
  isSubsetOf(other: Iterable<T>): boolean;  isSupersetOf(other): boolean;  isProperSubsetOf(other): boolean;
  isProperSupersetOf(other): boolean;  overlaps(other): boolean;  setEquals(other): boolean;
  toArray(): T[];  toJSON(): T[];
  [Symbol.iterator](): Iterator<T>;
}
```

Storage: `_storage` (the shared `ImmutableHashStorage<T, true>`), `_count`, `$typeArguments$ImmutableHashSet`, under the **ownership rule** of § Stage 011h classes (W4-11): the storage keeps an insertion-ordered entry array that is never removed from or overwritten in place, plus a `HashCore` from each key to its entry's index; a version owns the first `_count` entries, and a lookup is a hit only when the index it finds is below `_count`. `add` of a new element appends in place only when the version's `_count` equals the entry array's length; every other change, and an `add` on an older version, copies the version's entries into new storage (O(n)). The receiver comes back exactly where .NET returns it (probed on .NET 10): `add` of a present element, `remove` of an absent one, `union` / `except` that change nothing, `withComparer` of the same comparer, `clear()` of an empty set; `intersect` that keeps every element and `symmetricExcept` with an empty sequence return a **new** set; `clear()` of a non-empty set returns the empty set with its comparer (`empty()` for the default). Enumeration is the version's entries in insertion order (.NET's order is its hash order; both are unspecified, and conformance rows compare sorted).

### `ImmutableDictionary<K, V>` (`immutable-dictionary.ts`)

```ts
export class ImmutableDictionary<K, V> implements IImmutableDictionary<K, V> {
  constructor(…);  // internal, as ImmutableList's
  static empty<K, V>(): ImmutableDictionary<K, V>;
  static create<K, V>(keyComparer?: IEqualityComparer<K> | null, valueComparer?: IEqualityComparer<V> | null, typeArguments?): ImmutableDictionary<K, V>;
  static from<K, V>(entries: Iterable<readonly [K, V]>, keyComparer?, valueComparer?, typeArguments?): ImmutableDictionary<K, V>;  // CreateRange, ToImmutableDictionary(); add semantics
  static fromSelectors<S, K, V>(source: Iterable<S>, keySelector: (item: S) => K, valueSelector?: ((item: S) => V) | null,
    keyComparer?: IEqualityComparer<K> | null, valueComparer?: IEqualityComparer<V> | null): ImmutableDictionary<K, V>;  // ToImmutableDictionary(source, keySelector[, elementSelector][, comparers])
  readonly count: number;  readonly isEmpty: boolean;
  readonly keyComparer: IEqualityComparer<K>;  readonly valueComparer: IEqualityComparer<V>;
  readonly keys: KeyCollection<K>;  readonly values: ValueCollection<V>;
  get(key: K): V;                                     // KeyNotFoundException
  containsKey(key: K): boolean;  containsValue(value: V): boolean;  contains(pair: readonly [K, V]): boolean;
  tryGetValue(key: K, defaultValue: V): readonly [boolean, V];  tryGetKey(equalKey: K, defaultValue: K): readonly [boolean, K];
  getValueOrDefault(key: K, defaultValue: V): V;
  add(key: K, value: V): ImmutableDictionary<K, V>;   // the receiver when the key holds an equal value
  addRange(pairs: Iterable<readonly [K, V]>): ImmutableDictionary<K, V>;
  setItem(key: K, value: V): ImmutableDictionary<K, V>;  setItems(pairs: Iterable<readonly [K, V]>): ImmutableDictionary<K, V>;  // last wins
  remove(key: K): ImmutableDictionary<K, V>;  removeRange(keys: Iterable<K>): ImmutableDictionary<K, V>;  clear(): ImmutableDictionary<K, V>;
  withComparers(keyComparer: IEqualityComparer<K> | null, valueComparer?: IEqualityComparer<V> | null): ImmutableDictionary<K, V>;
  toJSON(): Record<string, V>;
  [Symbol.iterator](): Iterator<readonly [K, V]>;
}
```

Storage: `_storage` (the shared `ImmutableHashStorage<K, V>`), `_count`, `_valueComparer`, `$typeArguments$ImmutableDictionary`; the ownership rule of `ImmutableHashSet`. `setItem` / `setItems` on an existing key with a different value copy: a shared entry is never overwritten. `add` / `from` / `addRange` with a present key and a different value (by `valueComparer`) throw `ArgumentException` (`An element with the same key but a different value already exists. Key: '<key>'`); The receiver comes back exactly where .NET returns it (probed on .NET 10): `add` / `setItem` of an equal value, `addRange` / `setItems` whose every pair is already held with an equal value, `remove` / `removeRange` of absent keys, `withComparers` of the same comparers; `clear()` returns the empty dictionary with its comparers (`empty()` for the defaults). `null` keys throw `ArgumentNullException` (Parameter 'key').

### Builders (`immutable-*-builder.ts`)

```ts
export class ImmutableListBuilder<T> implements IList<T> {
  constructor(source?: Iterable<T> | null, typeArguments?: readonly RuntimeType[]); // ToBuilder() / CreateBuilder<T>(); copies; an ImmutableList source also lends its type arguments
  // the List surface it delegates to its inner List (`_list`): count, get, set, update, updateReturningOld, fromEnd, add, addRange,
  // insert, insertRange, remove, removeAt, removeAll, clear, contains, indexOf, lastIndexOf, sort, reverse, copyTo, toJSON, [Symbol.iterator]
}
export function toImmutableList<T>(builder: ImmutableListBuilder<T>): ImmutableList<T>;  // ToImmutable(); copies

export class ImmutableHashSetBuilder<T> implements ISet<T> {
  constructor(source?: Iterable<T> | null, comparer?: IEqualityComparer<T> | null, typeArguments?); // an ImmutableHashSet source lends its comparer and type arguments
  // delegates to its inner HashSet (`_set`): count, keyComparer, add, remove, contains, tryGetValue, clear, the ISet / IReadOnlySet members, copyTo, toJSON, [Symbol.iterator]
}
export function toImmutableHashSet<T>(builder: ImmutableHashSetBuilder<T>): ImmutableHashSet<T>;

export class ImmutableDictionaryBuilder<K, V> implements IDictionary<K, V> {
  constructor(source?: Iterable<readonly [K, V]> | null, keyComparer?: IEqualityComparer<K> | null, valueComparer?: IEqualityComparer<V> | null, typeArguments?);
  // delegates to its inner Dictionary (`_dictionary`): count, keyComparer, valueComparer, keys, values, get, set, update, updateReturningOld,
  // add, remove, clear, containsKey, containsValue, tryGetValue, getValueOrDefault, toJSON, [Symbol.iterator]
}
export function toImmutableDictionary<K, V>(builder: ImmutableDictionaryBuilder<K, V>): ImmutableDictionary<K, V>;
```

Each builder is composition (`builder instanceof List` is `false`, as `builder is List<T>` in .NET); `ImmutableArray<T>.Builder` is `ImmutableListBuilder<T>`. A builder does **not** inherit the rules of the class it holds (probed on .NET 10): it keeps its own `_version`, bumped as .NET 10 bumps it (probed 2026-11-19, correcting the part-2 text): by every mutating call, a no-op included (dictionary builder `remove` of an absent key, `set` of the same value, `add(k, equalValue)`, `clear`; set builder `add` of a present element, `remove` of an absent one, `unionWith([])`, `exceptWith` of absent elements; list builder `removeAll` that removes nothing, `set` of the same value, `addRange([])`, `sort`, `reverse`, `clear` of an empty builder), except the list builder's `remove` of an absent element, which does not bump; and its enumerator (one internal `BuilderIterator` shared by the three builders) throws `throwModified`'s `Collection was modified; enumeration operation may not execute.` on the next step; and it checks its own indexes, throwing `ArgumentOutOfRangeException` (`index`) with .NET's default text (`Specified argument was out of the range of valid values.`), not `List`'s. `toImmutableList` / `toImmutableHashSet` / `toImmutableDictionary` copy the builder's elements into a new immutable value (W4-11 condition 4: a later builder mutation never reaches it), and `ToBuilder()` copies the immutable value into the builder. The dictionary builder's `add(k, v)` of a present key is a no-op when the held value equals `v` by the value comparer, and otherwise throws `ArgumentException` (`An element with the same key but a different value already exists. Key: '1'`), as `ImmutableDictionary.add`. **`ImmutableArrayBuilder<T>`** (`ImmutableArray<T>.Builder`, `immutable-array-builder.ts`) is a sibling of `ImmutableListBuilder<T>` over the internal `ImmutableListBuilderCore<T>` (W4-66; `instanceof ImmutableListBuilder` is `false` on it), with .NET's differences: no version check at all (its enumerator reads `get(i)` while `i < count`, so a `foreach` that writes or adds never throws, as .NET), an `IndexOutOfRangeException` indexer (`Index was outside the bounds of the array.`; `removeAt` / `insert` keep the `ArgumentOutOfRangeException`), the ``ImmutableArray`1+Builder[T]`` text, and `toImmutableArray(builder)` returning an `ImmutableArray`. Builder members not listed (`ItemRef`, `MoveToImmutable`, `Capacity`, …) report `MS0001`.

### `IImmutableList<T>`, `IImmutableSet<T>`, `IImmutableDictionary<K, V>` (`immutable-interfaces.ts`)

```ts
// Only the members C# declares on each interface (W4-13); the extension-method forms fill the optional arguments.
export interface IImmutableList<T> extends IReadOnlyList<T> {
  add(value: T): IImmutableList<T>;  addRange(items: Iterable<T>): IImmutableList<T>;  clear(): IImmutableList<T>;
  insert(index: number, element: T): IImmutableList<T>;  insertRange(index: number, items: Iterable<T>): IImmutableList<T>;
  remove(value: T, comparer?: IEqualityComparer<T> | null): IImmutableList<T>;  removeAll(match: (item: T) => boolean): IImmutableList<T>;
  removeAt(index: number): IImmutableList<T>;  removeRange(index: number, count: number): IImmutableList<T>;
  removeItems(items: Iterable<T>, comparer?: IEqualityComparer<T> | null): IImmutableList<T>;
  replace(oldValue: T, newValue: T, comparer?: IEqualityComparer<T> | null): IImmutableList<T>;  setItem(index: number, value: T): IImmutableList<T>;
  indexOf(item: T, index?: number, count?: number, comparer?: IEqualityComparer<T> | null): number;
  lastIndexOf(item: T, index?: number, count?: number, comparer?: IEqualityComparer<T> | null): number;
}
export interface IImmutableSet<T> extends IReadOnlyCollection<T> {   // C#: IReadOnlyCollection<T>, not IReadOnlySet<T>
  contains(value: T): boolean;  isSubsetOf(other: Iterable<T>): boolean;  isSupersetOf(other: Iterable<T>): boolean;
  isProperSubsetOf(other: Iterable<T>): boolean;  isProperSupersetOf(other: Iterable<T>): boolean;
  overlaps(other: Iterable<T>): boolean;  setEquals(other: Iterable<T>): boolean;
  add(value: T): IImmutableSet<T>;  remove(value: T): IImmutableSet<T>;  clear(): IImmutableSet<T>;
  union(other: Iterable<T>): IImmutableSet<T>;  intersect(other: Iterable<T>): IImmutableSet<T>;
  except(other: Iterable<T>): IImmutableSet<T>;  symmetricExcept(other: Iterable<T>): IImmutableSet<T>;
  tryGetValue(equalValue: T, defaultValue: T): readonly [boolean, T];
}
export interface IImmutableDictionary<K, V> extends IReadOnlyDictionary<K, V> {
  add(key: K, value: V): IImmutableDictionary<K, V>;  addRange(pairs: Iterable<readonly [K, V]>): IImmutableDictionary<K, V>;
  setItem(key: K, value: V): IImmutableDictionary<K, V>;  setItems(pairs: Iterable<readonly [K, V]>): IImmutableDictionary<K, V>;
  remove(key: K): IImmutableDictionary<K, V>;  removeRange(keys: Iterable<K>): IImmutableDictionary<K, V>;  clear(): IImmutableDictionary<K, V>;
  contains(pair: readonly [K, V]): boolean;  tryGetKey(equalKey: K, defaultValue: K): readonly [boolean, K];
}
```

`IImmutableList<T>.IndexOf` / `LastIndexOf` (C#: `(item, index, count, comparer)`) and their extension overloads lower to the one member with optional arguments; `ImmutableList` implements it.

### `SortedSet<T>` (`sorted-set.ts`)

```ts
export class SortedSet<T> implements ISet<T>, IReadOnlySet<T> {
  constructor(items?: Iterable<T> | null, comparer?: IComparer<T> | null, typeArguments?: readonly RuntimeType[] | null); // null items = empty
  static from<T>(items: Iterable<T> | null | undefined, comparer?: IComparer<T> | null, typeArguments?): SortedSet<T>;   // the C# copy constructor: ArgumentNullException (collection)
  static of<T>(items: T[], comparer?: IComparer<T> | null, typeArguments?): SortedSet<T>;  // initializers / expressions; duplicates ignored
  static get $runtimeType(): CollectionType<SortedSet<unknown>>;
  readonly count: number;  readonly isReadOnly: false;
  readonly comparer: IComparer<T>;                    // the argument, or defaultComparer
  minOr<D>(defaultValue: D): T | D;  maxOr<D>(defaultValue: D): T | D;  // Min / Max: default(T) when empty (D30); null for a reference T
  add(item: T): boolean;  remove(item: T): boolean;  contains(item: T): boolean;  clear(): void;
  removeWhere(match: (item: T) => boolean): number;
  tryGetValue(equalValue: T, defaultValue: T): readonly [boolean, T];
  reverse(): Iterable<T>;                             // descending; version-checked
  getViewBetween(lowerValue: T, upperValue: T): SortedSet<T>;
  unionWith(other: Iterable<T>): void;  intersectWith(other): void;  exceptWith(other): void;  symmetricExceptWith(other): void;
  isSubsetOf(other: Iterable<T>): boolean;  isSupersetOf(other): boolean;  isProperSubsetOf(other): boolean;
  isProperSupersetOf(other): boolean;  overlaps(other): boolean;  setEquals(other): boolean;  // set-algebra.ts
  copyTo(array: T[], arrayIndex?: number): void;  toArray(): T[];  toJSON(): T[];
  [Symbol.iterator](): Iterator<T>;                   // ascending
}
```

Storage, in order: `_tree` (`RedBlackTree<T, undefined>`), `_owner` (the set whose `_version` counts the tree's changes: itself, or a view's root), `_version`, `_comparer`, `_bounds` (`{ bounded: false }`, or a view's `{ bounded: true, lower, upper }`), `$typeArguments$SortedSet`. `getViewBetween` returns a `SortedSet` built by the constructor and then pointed at the same tree and owner (one hidden class for sets and views; `instanceof SortedSet`, as .NET's view `is SortedSet<T>`): live, its `count` walks the range (O(k), as .NET). Checks, in .NET's order: on a view, a `lowerValue` / `upperValue` outside the view's own range throws `ArgumentOutOfRangeException` (`Specified argument was out of the range of valid values. (Parameter 'lowerValue')` / `'upperValue'`); then `lowerValue` above `upperValue` throws `ArgumentException` (`Must be less than or equal to upperValue. (Parameter 'lowerValue')`). A view's `add` outside its range throws `ArgumentOutOfRangeException` (`… (Parameter 'item')`); `contains` / `remove` outside it answer `false`; `clear` / `removeWhere` / the set operations touch only the range. An element the comparer cannot order surfaces `defaultCompare`'s `ArgumentException` unwrapped. `copyTo`: `ArgumentNullException` (`array`), a negative index `ArgumentOutOfRangeException` (`index ('-1') must be a non-negative value. (Parameter 'index')` + `Actual value was -1.`), a short array `ArgumentException` (`Destination array is not long enough to copy all the items in the collection. Check array index and length.`). Version (probed on .NET 10, corrected after the part-1 review): `add` always bumps (a present element included); `remove` bumps unless the tree is empty (an absent element included; .NET's `DoRemove` returns first on an empty tree); a set's `clear` always bumps, a view's only when its range holds elements; `unionWith`, `exceptWith`, `symmetricExceptWith` and `removeWhere` bump only when they change the set; `intersectWith` bumps whenever the set is non-empty (it rebuilds), except with itself, which changes nothing; a view and its set share the version.

### `SortedDictionary<K, V>` (`sorted-dictionary.ts`)

```ts
export class SortedDictionary<K, V> implements IDictionary<K, V>, IReadOnlyDictionary<K, V> {
  constructor(entries?: Iterable<readonly [K, V]> | null, comparer?: IComparer<K> | null, typeArguments?: readonly RuntimeType[] | null); // add semantics
  static from<K, V>(source: Iterable<readonly [K, V]> | null | undefined, comparer?, typeArguments?): SortedDictionary<K, V>; // ctor(IDictionary[, IComparer]): ArgumentNullException (dictionary)
  static of<K, V>(entries: ReadonlyArray<readonly [K, V]>, comparer?: IComparer<K> | null, typeArguments?): SortedDictionary<K, V>;      // { { k, v } }: add semantics
  static assign<K, V>(entries: ReadonlyArray<readonly [K, V]>, comparer?: IComparer<K> | null, typeArguments?): SortedDictionary<K, V>;  // { [k] = v }, JSON: last wins
  static get $runtimeType(): CollectionType<SortedDictionary<unknown, unknown>>;
  readonly count: number;  readonly isReadOnly: false;  readonly comparer: IComparer<K>;
  readonly keys: KeyCollection<K>;  readonly values: ValueCollection<V>;   // live views (dictionary-views.ts), in key order
  get(key: K): V;  set(key: K, value: V): void;  update(key: K, f: (current: V) => V): V;  updateReturningOld(key: K, f: (current: V) => V): V;
  add(key: K, value: V): void;  remove(key: K): boolean;  clear(): void;
  containsKey(key: K): boolean;  containsValue(value: V): boolean;   // values by elementEquals
  tryGetValue<D>(key: K, missingValue: D): readonly [found: true, value: V] | readonly [found: false, value: D];
  getValueOrDefault<D>(key: K, defaultValue: D): V | D;
  copyTo(array: (readonly [K, V])[], arrayIndex?: number): void;
  toJSON(): Record<string, V>;
  [Symbol.iterator](): Iterator<readonly [K, V]>;     // key order
}
```

Storage, in order: `_tree` (`RedBlackTree<K, V>`), `_version`, `_comparer`, `_keys` / `_values` (the cached views, `undefined` until first read), `$typeArguments$SortedDictionary`. `get` / `update` throw `throwKeyNotFound`'s `KeyNotFoundException`; a `null` key throws `ArgumentNullException` (`key`) from every keyed member; `add` of a present key throws `ArgumentException` with .NET's `SortedDictionary` text, which formats the pair: `An item with the same key has already been added. Key: [<key>, <value>]`; `copyTo` validates as `SortedSet`'s. Version (probed): `add`, `set` (a new or an existing key), `remove` (an absent key included, unless the dictionary is empty) and `clear` bump; `remove` and `clear` invalidate, unlike `Dictionary`; the views follow the dictionary's rule and print their own .NET type text through the source's `viewTypeName` hook (``SortedDictionary`2+KeyCollection[TKey,TValue]``; `dictionary-views.ts`'s `KeyValueSource.viewTypeName`, absent for `Dictionary`).

### `SortedList<K, V>` and `SortedListView<T>` (`sorted-list.ts`)

```ts
export class SortedList<K, V> implements IDictionary<K, V>, IReadOnlyDictionary<K, V> {
  constructor(source?: Iterable<readonly [K, V]> | null, comparer?: IComparer<K> | null, typeArguments?: readonly RuntimeType[] | null); // the copy rule below
  static from<K, V>(source: Iterable<readonly [K, V]> | null | undefined, comparer?, typeArguments?): SortedList<K, V>;  // ctor(IDictionary[, IComparer]): ArgumentNullException (dictionary)
  static withCapacity<K, V>(capacity: number, comparer?: IComparer<K> | null, typeArguments?): SortedList<K, V>;  // validated, then dropped
  static of<K, V>(entries, comparer?, typeArguments?): SortedList<K, V>;  static assign<K, V>(entries, comparer?, typeArguments?): SortedList<K, V>;
  static get $runtimeType(): CollectionType<SortedList<unknown, unknown>>;
  readonly count: number;  readonly isReadOnly: false;  readonly comparer: IComparer<K>;
  readonly keys: SortedListView<K>;  readonly values: SortedListView<V>;   // cached, live
  get, set, update, updateReturningOld, add, remove, clear, containsKey, tryGetValue, getValueOrDefault, copyTo   // as SortedDictionary
  containsValue(value: V): boolean;
  indexOfKey(key: K): number;  indexOfValue(value: V): number;      // -1 when absent; values by elementEquals
  getKeyAtIndex(index: number): K;  getValueAtIndex(index: number): V;  setValueAtIndex(index: number, value: V): void;
  removeAt(index: number): void;
  toJSON(): Record<string, V>;
  [Symbol.iterator](): Iterator<readonly [K, V]>;
}
export class SortedListView<T> implements IList<T>, IReadOnlyList<T> {  // SortedList.Keys / .Values (.NET's KeyList / ValueList)
  readonly count: number;  readonly isReadOnly: true;
  get(index: number): T;  fromEnd(n: number): T;  indexOf(item: T): number;  contains(item: T): boolean;  copyTo(array: T[], arrayIndex?: number): void;
  set / add / insert / remove / removeAt / clear: never     // NotSupportedException
  toJSON(): T[];  [Symbol.iterator](): Iterator<T>;         // version-checked against the list
}
```

Storage, in order: `_keys`, `_values` (parallel sorted arrays), `_version`, `_comparer`, `_compare`, `_keyList` / `_valueList` (the cached views), `$typeArguments$SortedList`; lookups go through `sorted-array.ts`'s `binarySearch` (.NET's `Array.BinarySearch`), and a comparer failure is wrapped as `Array.BinarySearch` / `Array.Sort` wrap it (`throwCompareFailed`: `InvalidOperationException`, `Failed to compare two elements in the array.`, the original as inner; `SortedSet` / `SortedDictionary` surface it unwrapped, as .NET). The index members (`getKeyAtIndex`, `getValueAtIndex`, `setValueAtIndex`, `removeAt`, the views' `get`) throw `ArgumentOutOfRangeException` with `List`'s text plus the actual value (`… (Parameter 'index')` + `Actual value was <n>.`, unlike `List`'s `throwOOR`); a `null` key throws `ArgumentNullException` (`key`), `indexOfKey` included; `add` of a present key throws `ArgumentException` (`An item with the same key has already been added. Key: <key> (Parameter 'key')`); the copy constructor sorts its source by the comparer and rejects two keys the comparer calls equal with the same text without the parameter (`… Key: <key>`, the later key in sorted order); a negative capacity throws `ArgumentOutOfRangeException` (`capacity ('<n>') must be a non-negative value. (Parameter 'capacity')` + `Actual value was <n>.`). The views: the key indexer write throws `NotSupportedException` (`Mutating a key collection derived from a dictionary is not allowed.`), every other mutator (the value indexer write included) `NotSupportedException` (`This operation is not supported on SortedList nested types because they require modifying the original SortedList.`); a view's `copyTo` is `Array.Copy`'s (`throwArrayTooSmall`'s message). Version (probed): `add`, `set` (a new or an existing key), `removeAt`, `setValueAtIndex` and `clear` bump; `remove` bumps only when it finds the key. `Capacity` and `TrimExcess` report `MS0001` (as `List`'s).

### `ConcurrentDictionary<K, V>` (`concurrent-dictionary.ts`)

```ts
export class ConcurrentDictionary<K, V> implements IDictionary<K, V>, IReadOnlyDictionary<K, V> {
  constructor(entries?: Iterable<readonly [K, V]> | null, comparer?: IEqualityComparer<K> | null, typeArguments?: readonly RuntimeType[] | null);
  static from<K, V>(entries: Iterable<readonly [K, V]> | null | undefined, comparer?, typeArguments?): ConcurrentDictionary<K, V>; // ctor(collection[, comparer]): ArgumentNullException (collection); a repeated key: ArgumentException (`The source argument contains duplicate keys.`)
  static withCapacity<K, V>(concurrencyLevel: number, capacity: number, comparer?, typeArguments?): ConcurrentDictionary<K, V>; // validated, then dropped
  static assign<K, V>(entries: ReadonlyArray<readonly [K, V]>, comparer?, typeArguments?): ConcurrentDictionary<K, V>; // { [k] = v }, JSON (no public Add, so no { { k, v } } form)
  static get $runtimeType(): CollectionType<ConcurrentDictionary<unknown, unknown>>;
  readonly count: number;  readonly isEmpty: boolean;  readonly isReadOnly: boolean;
  readonly keys: ReadOnlyList<K>;  readonly values: ReadOnlyList<V>;  // snapshots, as .NET's ReadOnlyCollection<T>
  get(key: K): V;  set(key: K, value: V): void;  update(key: K, f: (current: V) => V): V;  updateReturningOld(key: K, f: (current: V) => V): V;
  add(key: K, value: V): void;                        // IDictionary.Add: ArgumentException `The key already existed in the dictionary.`
  remove(key: K): boolean;  clear(): void;  containsKey(key: K): boolean;
  tryGetValue<D>(key: K, missingValue: D): readonly [found: true, value: V] | readonly [found: false, value: D];
  getValueOrDefault<D>(key: K, defaultValue: D): V | D;
  tryAdd(key: K, value: V): boolean;
  tryUpdate(key: K, newValue: V, comparisonValue: V): boolean;         // comparisonValue by elementEquals
  tryRemove<D>(key: K, missingValue: D): readonly [found: true, value: V] | readonly [found: false, value: D];
  getOrAdd(key: K, value: V): V;
  getOrAddWith<A = undefined>(key: K, valueFactory: (key: K, factoryArgument: A) => V, factoryArgument?: A): V;
  addOrUpdate(key: K, addValue: V, updateValueFactory: (key: K, current: V) => V): V;
  addOrUpdateWith<A = undefined>(key: K, addValueFactory: (key: K, factoryArgument: A) => V,
    updateValueFactory: (key: K, current: V, factoryArgument: A) => V, factoryArgument?: A): V;
  toArray(): [K, V][];                                // snapshot
  toJSON(): Record<string, V>;  toString(): string;
  [Symbol.iterator](): Iterator<readonly [K, V]>;     // the live store; never throws on modification
}
```

Storage, in order: `_store` (`HashCore<K, V>`), `$typeArguments$ConcurrentDictionary`; no `_version`. Keys through `hash-core` (`elementEquals` / `elementHashCode` or the comparer); a `null` key throws `ArgumentNullException` (`key`, `requireKey`) before any factory is checked; a `null` factory `ArgumentNullException` (`valueFactory` / `addValueFactory` / `updateValueFactory`). `withCapacity`: a `concurrencyLevel` below 1 other than -1 throws `ArgumentOutOfRangeException` (`The concurrencyLevel argument must be positive, or -1 to indicate a default level. (Parameter 'concurrencyLevel')`); a negative `capacity` `throwNegativeValue("capacity", …)`. The value and factory forms of `GetOrAdd` / `AddOrUpdate` are distinct members chosen by the C# overload, never by `typeof` at run time, because `V` may itself be a function type. As .NET: a factory runs only when needed; a value `getOrAddWith`'s factory stored under the same key wins; `addOrUpdate` / `addOrUpdateWith` store an update only if the current value is still the one read (else they run again) and an add only if the key is still absent. The enumerator walks the live store and never throws: an entry removed before it is reached is not yielded, an added one may be (as .NET). Enumeration order is insertion order (.NET's is its bucket order; conformance rows compare sorted). `TryRemove(KeyValuePair<K,V>)`, `GetOrAdd` / `AddOrUpdate` overloads beyond these and `ICollection<KeyValuePair<K,V>>.CopyTo` report `MS0001`.

### JSON kinds (FR-028, runtime side)

The nine 011h kinds widen the `kind` unions of R0's `SequenceCollectionDescriptor` / `KeyedCollectionDescriptor` (§ JSON collection kinds), so the converter keeps its two generic cases and imports no collection class:

```ts
export type SequenceCollectionDescriptor = {      // JSON array
  readonly kind: "list" | "queue" | "stack" | "hashSet" | "immutableList" | "immutableArray" | "readOnlyList" | "immutableHashSet" | "sortedSet";
  readonly element: TypeDescriptor;
  readonly create: (items: unknown[]) => Iterable<unknown>;
};
export type KeyedCollectionDescriptor = {         // JSON object; keys parsed by dictionary-keys.ts
  readonly kind: "dictionary" | "immutableDictionary" | "concurrentDictionary" | "sortedDictionary" | "sortedList";
  readonly key: TypeDescriptor;
  readonly value: TypeDescriptor;
  readonly create: (entries: Array<readonly [unknown, unknown]>) => Iterable<readonly [unknown, unknown]>;
};
```

Read: the element (or key and value) converters run first, then `create` receives the values in document order. Write: iteration (`for..of`), so the sorted kinds write in key order, as .NET does. The context writes `create` as a closure, with the FR-008 type arguments when the compilation asks: `immutableList` → `(items) => ImmutableList.from(items)`, `immutableArray` → `ImmutableArray.from(items)`, `immutableHashSet` → `ImmutableHashSet.from(items)`, `sortedSet` → `new SortedSet(items)` (duplicates ignored), `immutableDictionary` → `ImmutableDictionary.empty().setItems(entries)`, `concurrentDictionary` / `sortedDictionary` / `sortedList` → `<Class>.assign(entries)`: a repeated key keeps its **last** value, which .NET 10's System.Text.Json does for every one of these kinds (read 2026-11-19). `readOnlyList` serializes, and its `create` throws `NotSupportedException`, because System.Text.Json does not deserialize `ReadOnlyCollection<T>` (no usable constructor; read from .NET 10).

### C# member → emitted TS (011h)

Instance members not listed map by name to the camelCase member above with no template (FR-007: `Count` → `count`, `IsEmpty` → `isEmpty`, `Contains` → `contains`, `KeyComparer` → `keyComparer`, `IndexOfKey` → `indexOfKey`, …); indexers emit `get` / `set` and FR-010's update forms; `r[^n]` → `fromEnd(n)`; `ImmutableArray<T>` `a[a..b]` → `a.slice(a, b)`. Members without a runtime member report `MS0001` at the call site (FR-001).

| C# | Emitted TS |
|---|---|
| `list.AsReadOnly()` | `asReadOnly(list)` |
| `new ReadOnlyCollection<T>(src)`, `Array.AsReadOnly(arr)` | `new ReadOnlyList(src)` / `ReadOnlyList.fromArray(arr)` |
| `ReadOnlyCollection<T>.Empty` | `ReadOnlyList.empty()` |
| `ImmutableList<T>.Empty`, `ImmutableList.Create<T>()` / `ImmutableArray<T>.Empty`, `ImmutableArray.Create<T>()` | `ImmutableList.empty()` / `ImmutableArray.empty()` |
| `ImmutableList.Create(a, b)`, `ImmutableList<int> x = [a, b]` / the `ImmutableArray` forms | `ImmutableList.of([a, b])` / `ImmutableArray.of([a, b])` (a fresh literal only: an expanded `params` call, `ArgumentKind` `ParamArray` / `ParamCollection`, or a collection expression) |
| every other `Create` / `params` call: an existing array or span in normal form (`ImmutableList.Create(arr)`, `ImmutableArray.Create(arr)`) | `ImmutableList.from(arr)` / `ImmutableArray.from(arr)`: never `of`, which takes ownership and would alias the caller's array. The rule reads `IArgumentOperation.ArgumentKind`: `ParamArray` / `ParamCollection` (the expanded form) is a fresh literal and may use `of`; any other argument kind uses `from` |
| `ImmutableArray.Create(arr, start, length)` | `ImmutableArray.fromRange(arr, start, length)` (validates as .NET; each argument evaluated once) |
| `ImmutableList.CreateRange(src)`, `src.ToImmutableList()` / `ImmutableArray.CreateRange(src)`, `src.ToImmutableArray()` | `ImmutableList.from(src)` / `ImmutableArray.from(src)` |
| `ImmutableArray<T>.Length` | `count` |
| `im.RemoveRange(items)` / `im.RemoveRange(index, count)` | `im.removeItems(items)` / `im.removeRange(index, count)` |
| `im.Sort()` / `im.Sort(comparison)` / `im.Sort(comparer)` | `im.sort()` / `im.sort(comparison)` / `im.sort(comparer)` |
| `im.ToBuilder()`, `ImmutableList.CreateBuilder<T>()` | `new ImmutableListBuilder(im)` / `new ImmutableListBuilder()` |
| `builder.ToImmutable()` (list builder / array builder) | `toImmutableList(builder)` / `toImmutableArray(builder)` |
| `ImmutableArray.CreateBuilder<T>()`, `arr.ToBuilder()` | `new ImmutableArrayBuilder()` / `new ImmutableArrayBuilder(arr)` |
| `ImmutableHashSet.Create(a, b)` / `Create(comparer, a, b)` | `ImmutableHashSet.of([a, b])` / `ImmutableHashSet.of([a, b], comparer)` |
| `ImmutableHashSet.CreateRange([comparer, ]src)`, `src.ToImmutableHashSet([comparer])` | `ImmutableHashSet.from(src[, comparer])` (C#'s leading comparer moves after the source: the two arguments evaluate in the TS order, documented) |
| `ImmutableHashSet<T>.Empty`, `ImmutableHashSet.Create<T>()` | `ImmutableHashSet.empty()` |
| `set.ToBuilder()`, `ImmutableHashSet.CreateBuilder<T>([comparer])`; `builder.ToImmutable()` | `new ImmutableHashSetBuilder(set)`, `new ImmutableHashSetBuilder(null[, comparer])`; `toImmutableHashSet(builder)` |
| `ImmutableDictionary<K,V>.Empty`; `ImmutableDictionary.Create<K,V>([kc[, vc]])` | `ImmutableDictionary.empty()`; `ImmutableDictionary.create([kc[, vc]])` |
| `ImmutableDictionary.CreateRange([kc, [vc, ]]pairs)`, `pairs.ToImmutableDictionary([kc[, vc]])` | `ImmutableDictionary.from(pairs[, kc[, vc]])` (the comparers move after the pairs, as for `ImmutableHashSet`) |
| `src.ToImmutableDictionary(keySel[, elemSel][, kc[, vc]])` | `ImmutableDictionary.fromSelectors(src, keySel, elemSel ?? null[, kc[, vc]])` |
| `dict.ToBuilder()`, `ImmutableDictionary.CreateBuilder<K,V>([kc[, vc]])`; `builder.ToImmutable()` | `new ImmutableDictionaryBuilder(dict)`, `new ImmutableDictionaryBuilder(null[, kc[, vc]])`; `toImmutableDictionary(builder)` |
| `new SortedSet<T>()`, `new SortedSet<T>(cmp)`, `new SortedSet<T>(src[, cmp])`, `new SortedSet<T>(cmp) { a, b }`, `SortedSet<T> s = [a, b]` | `new SortedSet()`, `new SortedSet(null, cmp)`, `SortedSet.from(src[, cmp])`, `SortedSet.of([a, b], cmp)`, `SortedSet.of([a, b])` |
| `s.Min` / `s.Max` | `s.minOr(<default(T)>)` / `s.maxOr(<default(T)>)` |
| `s.Reverse()`, `s.GetViewBetween(lo, hi)`, `s.RemoveWhere(f)` | `s.reverse()`, `s.getViewBetween(lo, hi)`, `s.removeWhere(f)` |
| `new SortedDictionary<K,V>([cmp])`, `new SortedDictionary<K,V>(dict[, cmp])`, `{ { k, v } }`, `{ [k] = v }` | `new SortedDictionary(null[, cmp])`, `SortedDictionary.from(dict[, cmp])`, `SortedDictionary.of([[k, v]][, cmp])`, `SortedDictionary.assign([[k, v]][, cmp])` |
| `new SortedList<K,V>(capacity[, cmp])` / other constructors and initializers | `SortedList.withCapacity(capacity[, cmp])` / as `SortedDictionary` |
| `sl.Keys[i]`, `sl.Values[i]`, `sl.GetKeyAtIndex(i)`, `sl.IndexOfKey(k)` | `sl.keys.get(i)`, `sl.values.get(i)`, `sl.getKeyAtIndex(i)`, `sl.indexOfKey(k)` |
| `new ConcurrentDictionary<K,V>([cmp])`, `new ConcurrentDictionary<K,V>(src[, cmp])`, `(level, capacity[, cmp])`, `{ [k] = v }` | `new ConcurrentDictionary(null[, cmp])`, `ConcurrentDictionary.from(src[, cmp])`, `ConcurrentDictionary.withCapacity(level, capacity[, cmp])`, `ConcurrentDictionary.assign([[k, v]][, cmp])` |
| `cd.GetOrAdd(k, v)` / `GetOrAdd(k, f)` / `GetOrAdd(k, f, arg)` | `cd.getOrAdd(k, v)` / `cd.getOrAddWith(k, f)` / `cd.getOrAddWith(k, f, arg)` |
| `cd.AddOrUpdate(k, v, u)` / `AddOrUpdate(k, f, u)` / `AddOrUpdate(k, f, u, arg)` | `cd.addOrUpdate(k, v, u)` / `cd.addOrUpdateWith(k, f, u)` / `cd.addOrUpdateWith(k, f, u, arg)` |
| `cd.TryRemove(k, out var v)`, `cd.TryGetValue(k, out var v)`, every 011h `Try*` with an `out` | the `[found, value]` tuple lowering of 011c's `TryGetValue`, passing `default(T)` (D30) |
| `Comparer<T>.Default`, `Comparer<T>.Create(f)`, `EqualityComparer<T>.Default` | `defaultComparer`, `createComparer(f)`, `defaultEqualityComparer` |
| a conversion of an immutable type to `ICollection<T>` / `IList<T>` / `ISet<T>` / `IDictionary<K,V>` | `MS0001` |
| `ImmutableArray<T>.IsDefault` / `IsDefaultOrEmpty` / `ItemRef` / `AsSpan` / `AsMemory`, `ToImmutableSorted*`, `ImmutableSortedSet<T>`, `ImmutableQueue<T>`, `ImmutableStack<T>` | `MS0001` (not in FR-001) |

An omitted source argument emits `null`; the copy constructors (a C# source argument) lower to `from`, which throws `ArgumentNullException` for a `null` source as .NET does.

**Type tests (D31) and dispatchers (W4-18, extended by 011h).** `is` / `as` / patterns against a collection type are descriptor tests (D31, group J): `isType` reads each class literal's `interfaces` list, so the lists below are normative. Overload dispatchers' collection parameters render `instanceof` over the classes of the `RuntimeCollections` implementer table, which must agree with the literals' `interfaces` lists (J's drift test compares the two), except that the dispatcher rows leave out the kinds whose shape does not implement the interface (the immutables in the `IList` / `ICollection` / `ISet` / `IDictionary` rows). Dispatcher table (from .NET 10 reflection, 2026-11-19):

| Interface | Runtime classes (dispatchers) |
|---|---|
| `IList<T>` | `List`, `ReadOnlyList`, `SortedListView`, `ImmutableListBuilder`, `ImmutableArrayBuilder` |
| `IReadOnlyList<T>` | `List`, `ReadOnlyList`, `ImmutableList`, `ImmutableArray`, `ImmutableListBuilder`, `ImmutableArrayBuilder` |
| `ICollection<T>` | `List`, `HashSet`, `ReadOnlyList`, `SortedSet`, `SortedListView`, `ImmutableListBuilder`, `ImmutableArrayBuilder`, `ImmutableHashSetBuilder` |
| `IReadOnlyCollection<T>` | the 011c rows, plus `ReadOnlyList`, `ImmutableList`, `ImmutableArray`, `ImmutableHashSet`, `ImmutableDictionary`, the four builders, `SortedSet`, `SortedDictionary`, `SortedList`, `ConcurrentDictionary` |
| `IDictionary<K,V>` | `Dictionary`, `SortedDictionary`, `SortedList`, `ConcurrentDictionary`, `ImmutableDictionaryBuilder` |
| `IReadOnlyDictionary<K,V>` | the `IDictionary` row plus `ImmutableDictionary` |
| `ISet<T>` | `HashSet`, `SortedSet`, `ImmutableHashSetBuilder` |
| `IReadOnlySet<T>` | `HashSet`, `SortedSet`, `ImmutableHashSet` |
| `IImmutableList<T>` / `IImmutableSet<T>` / `IImmutableDictionary<K,V>` | `ImmutableList`, `ImmutableArray` / `ImmutableHashSet` / `ImmutableDictionary` |

**Class literals of the 011h classes (`interfaces`, normative for D31).** Each entry is `{ collection, arguments }`: `arguments` maps the interface's type parameters to the class's (`[0]` = the class's first argument; `null` when the interface's argument is not one of the class's, e.g. `ICollection<KeyValuePair<K, V>>`), as the 011c literals. The order follows .NET's declaration; the lists are exactly .NET 10's generic collection interfaces (reflection, 2026-11-19):

| Class literal | `interfaces` |
|---|---|
| `ReadOnlyList` | `IList [0]`, `ICollection [0]`, `IReadOnlyList [0]`, `IReadOnlyCollection [0]` |
| `ImmutableList`, `ImmutableArray` | `IImmutableList [0]`, `IReadOnlyList [0]`, `IReadOnlyCollection [0]`, `IList [0]`, `ICollection [0]` |
| `ImmutableHashSet` | `IImmutableSet [0]`, `IReadOnlySet [0]`, `IReadOnlyCollection [0]`, `ISet [0]`, `ICollection [0]` |
| `ImmutableDictionary` | `IImmutableDictionary [0, 1]`, `IReadOnlyDictionary [0, 1]`, `IDictionary [0, 1]`, `ICollection null`, `IReadOnlyCollection null` |
| `ImmutableListBuilder`, `ImmutableArrayBuilder` | `IList [0]`, `ICollection [0]`, `IReadOnlyList [0]`, `IReadOnlyCollection [0]` |
| `ImmutableHashSetBuilder` | `ISet [0]`, `ICollection [0]`, `IReadOnlyCollection [0]` (no `IReadOnlySet`, as .NET) |
| `ImmutableDictionaryBuilder` | `IDictionary [0, 1]`, `IReadOnlyDictionary [0, 1]`, `ICollection null`, `IReadOnlyCollection null` |
| `SortedSet` | `ISet [0]`, `ICollection [0]`, `IReadOnlySet [0]`, `IReadOnlyCollection [0]` |
| `SortedDictionary`, `SortedList`, `ConcurrentDictionary` | `IDictionary [0, 1]`, `IReadOnlyDictionary [0, 1]`, `ICollection null`, `IReadOnlyCollection null` |
| `SortedListView` (`KeyList` / `ValueList`) | `IList [0]`, `ICollection [0]` (no `IReadOnly*`, as .NET) |

The builders, `ImmutableArray` and `SortedListView` gain a class literal (the lazy `$runtimeType` getter with `closeFor`, as every collection's) in this stage; `SortedDictionary`'s views reuse `KeyCollection` / `ValueCollection`.

### Budgets (NFR-001, NFR-008; measured by 011h)

- `SortedSet` / `SortedDictionary`: the time per `add` / `remove` / `contains` (`containsKey` / `get`) at 1e5 elements ≤ 2.5× the time at 1e3; `SortedList`: the same for `get` / `containsKey` / `indexOfKey`. Absolute times recorded per release in `docs/runtime-types.md`.
- `ImmutableList` / `ImmutableHashSet` / `ImmutableDictionary`: building 1e4 elements by repeated `Add` (`x = x.Add(i)`) ≤ 50 ms, normalized as NFR-001, on Node.js and Bun (research R18). Absolute times of `get` / `contains`, `setItem` and `remove` recorded.
- `ConcurrentDictionary` with string and number keys: the `Dictionary` row of NFR-001 (≤ 2.0 ns over `Map`), through the same `hash-core`.
- Sizes: § Performance and size budgets (`ReadOnlyList` ≤ 0.8 KB is a proposal that the bundled exception classes may not fit: the `NotSupportedException` and `ArgumentOutOfRangeException` chains are about 0.6 KB together; 011h measures and reports before changing it). Each 011h module is side-effect free (0 B under a side-effect-only import, NFR-007), with a size row and a `mustNotContain` row (an entry using `List` alone contains none of the 011h messages).

## Performance and size budgets (NFR-001, NFR-008)

| Operation / entry | Budget (NFR-001: normalized overhead per operation on the reference machine, hot-helper shape, default build, larger of Node.js and Bun) | Rationale |
|---|---|---|
| `List.get` / `set` | ≤ 1.0 ns each over `a[i]` / `a[i] = v`; on bundled Node.js data-driven `get` ≤ 1.75 ns (W4-34: about 1 ns absolute, the abstraction's cost the maintainer accepted on 2026-11-16) and `set` ≤ 1.85 ns (W4-37: run-to-run noise around the bounds compare and the `_version` store FR-057 requires) | bounds compare and, for `set`, the `_version` store; R17 measured 0.45 (sequential) / 0.74 (data-driven) / 0.82 ns for the `defineProperty` + unsigned-compare variant |
| `list[i] op= v`, direct form / closure fallback | ≤ 1.5 ns (≤ 1.6 ns on Node.js, W4-37) / ≤ 3.0 ns | 0.86 ns measured / closure form estimated |
| `get` at a polymorphic site | ≤ 2.0 ns | 1.08 ns measured (Node) |
| `List.add` | ≤ 0.5 ns over `push` | delegates to `push`, bumps `_version`; 0.05 ns measured |
| lowered `foreach` over `List` | ≤ 1.25 ns per element on Node.js, ≤ 0.5 ns on Bun, over `for..of` | maintainer decision W4-27: the measured 1.04 ns per element on Node (Bun 0.00 ns) with the guard read once per loop is the .NET-mandated version and length compare itself (FR-057), not guard overhead |
| `Dictionary` / `ConcurrentDictionary` / `HashSet` primitive keys | ≤ 2.0 ns over the named `Map` / `Set` calls | estimated, confirmed in 011c (over budget in R1, W4-35: the class implementation, two `Map` lookups and key validation per call, not the checks mechanism; met by hand-in R2's single-lookup hash core before 011g) |
| `Queue` / `Stack` | ≤ 1.5 ns over `push` / `a[head++]` / `pop` / `at(-1)` | estimated, confirmed in 011c (`Queue` over budget on Node.js in R1: hand-in R2's, W4-37) |
| record keys, sorted and immutable classes | recorded per release; `SortedSet` / `SortedDictionary`: time per op at 1e5 ≤ 2.5× at 1e3; `SortedList`: reads only; immutable: 1e4 repeated `Add` ≤ 50 ms | no native equivalent |
| `Queue.dequeue` | amortized O(1), interleaved with `enqueue` too | an array and a head index whose consumed prefix is dropped once it is half the array (R2; 2.x's `shift` was O(n)) |
| Checks off (FR-058) | in the bun / esbuild bundle, no store to or read of `_version` outside the constructor, and `get` / `set` do not call the out-of-range throw function | inline `typeof __METANO_CHECKS__` guard, default on |
| `List` alone, minified + gz | ≤ 4586 B | NFR-008, raised by W4-25 (d) to the measured size + 10% (bun 4169 B / esbuild 4113 B; research R13's 1.67 KB had no FR-013 exceptions): FR-016's exception classes (about 0.97 KB) plus `elementEquals` / `defaultEquals` (about 0.5 KB), shared when several collections are used; slimming the exceptions is a follow-up |
| `Dictionary` / `HashSet` / `Queue` / `Stack` alone | ≤ 4506 / 4283 / 3128 / 2947 B | NFR-008, W4-25 (d): measured + 10%, same reason |
| `List` + `Dictionary` + `HashSet` together | ≤ 7112 B | NFR-008, W4-25 (d): measured bun 6465 B / esbuild 6357 B + 10%; the shared cost counted once |
| `ReadOnlyList` / `ImmutableList` / `ImmutableHashSet` / `ImmutableDictionary` / `SortedSet` / `SortedDictionary` / `SortedList` / `ConcurrentDictionary` alone | ≤ 0.8 / 2.0 / 2.0 / 2.5 / 2.5 / 3.0 / 2.5 / 3.5 KB | NFR-008, proposed |

## D31 additions (W4-65)

- **LINQ terminals**: `toList(typeArguments?)`, `toDictionary(keySelector, valueSelector?, keyQueryable?, valueQueryable?, typeArguments?)` and `toHashSet(typeArguments?)` take a trailing optional `typeArguments`; the compiler passes it under FR-008.
- **Covariant argument match** (`argumentsMatch`): a covariant position matches when the actual argument is identical, or a reference type (not primitive, enum, `isValueType`, nullable) assignable to the tested one; `ObjectType` as target accepts only reference-type actuals.
- **Drift test**: the compiler's `RuntimeCollections` implementer table and each runtime class literal's `interfaces` list must name the same interfaces for the same classes; a test compares them.
