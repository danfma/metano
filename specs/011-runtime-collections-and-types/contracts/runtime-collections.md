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
| Interface descriptors test "`instanceof` any implementing class" | Nominal through the class literal's `interfaces` list (the `implementsInterface` walk), matched by collection kind; an interface descriptor imports no class | A 011h class (or a user class implementing `IList<T>`) joins `is IReadOnlyList<T>` by listing it; `descriptors.ts` would otherwise import every class (group H's request). |
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
| `default-comparer.ts` | `elementEquals`, `elementHashCode`, `defaultCompare`, `createComparer`, `defaultEqualityComparer`, `defaultComparer`; types `IEqualityComparer`, `IComparer`, `Comparison` | yes |
| `helpers.ts` | `fromEnd`, `isIterable`, `asArray`, `asJsArray`, `asSpan`, `asReadOnlySpan` | yes |
| `descriptors.ts` | `listType`, `dictionaryType`, `hashSetType`, `queueType`, `stackType`, `iListType`, `iCollectionType`, `iReadOnlyListType`, `iReadOnlyCollectionType`, `iDictionaryType`, `iReadOnlyDictionaryType`, `iSetType`, `iReadOnlySetType` | yes |
| `throw-helpers.ts` | the cold throw functions (§ Cold throw helpers) | `throwModified` only |
| `checks.ts` | the ambient `__METANO_CHECKS__` declaration (§ Production checks) and a type `MetanoChecks` that carries it into every entry's declarations | type only |
| `hash-core.ts` | `HashCore` (internal): the keyed store shared by `Dictionary`, `HashSet`, (011h) `ConcurrentDictionary` and `immutable-hash-storage.ts` (§ Equality and comparison) | no (internal; reached by 011h through the `./system/collections/*` wildcard) |
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
| `Queue<T>` | `_items` (ring buffer), `_head`, `_size`, `_version`, `$typeArguments$Queue` | none |
| `Stack<T>` | `_items` (bottom to top), `_version`, `$typeArguments$Stack` | none |
| `KeyCollection<K>` / `ValueCollection<V>` | `_source` (a `KeyValueSource`) | none |

Only `List`'s `_items` / `_version` are ABI; the other fields are implementation details (still non-enumerable, so `Object.keys`, spread, `JSON.stringify`, `toEqual`, `structuredClone` and the structural `equals` of `system/equality` see nothing). `$typeArguments$<Class>` is always defined, `undefined` when no descriptors are passed (FR-008).

`count` is a prototype getter on every class (`List`: `this._items.length`, so a length change through `asJsArray()` keeps it right, FR-057).

## Production checks (FR-058, D26)

- **Identifier: `__METANO_CHECKS__`** (confirmed; FR-058's "name to be confirmed in 011c"). Declared once, in `checks.ts`, as `declare global { const __METANO_CHECKS__: boolean | undefined; }`; `checks.ts` also exports `type MetanoChecks = boolean | undefined`, which `list.ts`, the collections barrel and the root barrel re-export as a type, so the declaration reaches every entry point's `.d.ts` and the generated code of every dependent package type-checks under `tsgo --strict` (R1 adds a consumer fixture that proves it). No module derives a module-scope constant from it.
- **Guard text** (normative; the compiler emits it verbatim, the runtime uses the same text): `(typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__)`. With `define: { __METANO_CHECKS__: "false" }` bun and esbuild fold it to `false` and drop the guarded code (FR-043 acceptance, R1).
- **Guarded in the runtime**: the bounds checks of `List.get`, `set`, `update`, `updateReturningOld`, `fromEnd`; every `_version` store of every class; every enumeration check (`List`, `Dictionary`, `KeyCollection`, `ValueCollection`, `HashSet`, `Queue`, `Stack` iterators, `List.forEach`). Never guarded: `insert`, `insertRange`, `removeAt`, `removeRange`, `getRange`, `slice`, `copyTo`, `withCapacity`, every `KeyNotFoundException`, `ArgumentNullException`, `ArgumentException`, `NotSupportedException`, empty `Queue` / `Stack`, the free helper `fromEnd(array, n)`.
- **Guarded in generated code**: the two check sites of the lowered `foreach` (§ Lowered `foreach`).

## Cold throw helpers (`throw-helpers.ts`, FR-016)

Module-level functions returning `never`, each constructing the FR-013 class with .NET's exact message (probed), kept out of the hot members so those stay small and inlinable (research R17). Only `throwModified` is part of the C → R contract (the compiler emits it); the others are used by the runtime classes and by 011h's classes, which import them from `./throw-helpers.ts`.

| Function | Throws | Message (`(Parameter …)` is appended by the exception class) | Used by |
|---|---|---|---|
| `throwModified(): never` | `InvalidOperationException` | `Collection was modified; enumeration operation may not execute.` | lowered `foreach` (generated), every iterator except `Stack`'s, `List.forEach` |
| `throwEnumeratorModified(): never` | `InvalidOperationException` | `Collection was modified after the enumerator was instantiated.` | `Stack` iterator; 011h's sorted classes (same .NET text). Renamed from `throwStackModified` in R1 (W4-20); not emitted by the compiler |
| `throwOOR(): never` | `ArgumentOutOfRangeException` (param `index`) | `Index was out of range. Must be non-negative and less than the size of the collection.` | `List.get`, `set`, `update`, `updateReturningOld`, `fromEnd`, `removeAt` |
| `throwInsertOOR(): never` | `ArgumentOutOfRangeException` (`index`) | `Index must be within the bounds of the List.` | `List.insert` |
| `throwIndexAtMostCount(): never` | `ArgumentOutOfRangeException` (`index`) | `Index was out of range. Must be non-negative and less than or equal to the size of the collection.` | `List.insertRange`, `indexOf(item, index)` |
| `throwNegative(paramName: string): never` | `ArgumentOutOfRangeException` (`paramName`) | `Non-negative number required.` | `getRange`, `removeRange`, `slice` (`index` / `count`), `withCapacity` (`capacity`) |
| `throwInvalidRange(): never` | `ArgumentException` | `Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.` | `getRange`, `removeRange`, `slice` |
| `throwArgumentNull(paramName: string): never` | `ArgumentNullException` (`paramName`) | `Value cannot be null.` | null `key` (every dictionary-shaped class), null `collection` / `match` / `converter` / `action` / `other` / `array` |
| `throwKeyNotFound(key: unknown): never` | `KeyNotFoundException` | ``The given key '<key>' was not present in the dictionary.`` (`<key>` by `messageValue`) | `Dictionary.get`, `update`, `updateReturningOld` |
| `throwDuplicateKey(key: unknown): never` | `ArgumentException` | `An item with the same key has already been added. Key: <key>` | `Dictionary.add`, `of`, `from` |
| `throwFixedSize(): never` | `NotSupportedException` | `Collection was of a fixed size.` | fixed-size adopted `List`: `add`, `addRange`, `insert`, `insertRange`, `remove`, `removeAt`, `removeRange`, `removeAll` |
| `throwReadOnly(): never` | `NotSupportedException` | `Collection is read-only.` | fixed-size adopted `List.clear` (011h: `ReadOnlyList`) |
| `throwEmptyQueue(): never` | `InvalidOperationException` | `Queue empty.` | `Queue.dequeue`, `peek` |
| `throwEmptyStack(): never` | `InvalidOperationException` | `Stack empty.` | `Stack.pop`, `peek` |
| `throwCompareFailed(inner: unknown): never` | `InvalidOperationException`, `innerException` = the thrown value when it is an `Error` (no `toException`, which would pull its module into every `List` bundle) | `Failed to compare two elements in the array.` | `List.sort`, `binarySearch`: any exception the comparison throws, the default comparer's included |
| `throwNotComparable(): never` | `ArgumentException` | `At least one object must implement IComparable.` | `defaultCompare` |
| `throwArrayTooSmall(): never` | `ArgumentException` (`destinationArray`) | `Destination array was not long enough. Check the destination index, length, and the array's lower bounds.` | every `copyTo` |

`copyTo(array, -1)` throws `ArgumentOutOfRangeException` (`destinationIndex`) with `destinationIndex ('-1') must be greater than or equal to '0'.` and the actual value, as .NET; it is built inline in `copyTo` (a cold branch). Values quoted in a message are formatted by `messageValue` (FR-016's caveat: JavaScript's text, so a record key quotes its `toString()`).

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
- **`elementHashCode(value)`**: `0` for `null` / `undefined`; primitives by value (`HashCode.combine`; a `bigint` by `hashInt64`); a `Boxed` by its raw value; a value with `hashCode()` through it; a `Duration` / `Date` by length / time; a `Decimal` (decimal.js, which has `equals` but no `hashCode`) by the hash of its normalized text (`toString()`, decimal.js drops trailing zeros; `-0` hashes as `0`), so `1.0m` and `1m` collide as `equals` requires; a Temporal value (`PlainDate`, `PlainTime`, `PlainDateTime`, `ZonedDateTime`, `Instant`, …: `equals`, no `hashCode`) by the hash of `toString()`, which includes the time zone and calendar that its `equals` compares; **any other value with `equals` but no `hashCode`** by a constant (one bucket, compared with `equals`: O(n) but correct, as today's `hash-set.ts`); anything else (arrays and identity-equal objects) by an identity hash: a sequential number per object kept in a `WeakMap` created lazily on first use (no module-scope `new`, NFR-006). R1 tests `HashSet<decimal>.contains(1.0m)` after `add(1m)`, `Dictionary<DateOnly, V>.get` with a fresh equal `PlainDate`, and a user class with `equals` only.
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

No null-key validation and no version inside the core; the classes own both (the mutation-tolerant iterator is what `Dictionary`'s `remove` / `clear` exemption and `ConcurrentDictionary`'s non-throwing enumerator rely on). `HashSet<T>` stores its elements as `HashCore<T, T>`. A `hash-core.ts` instance lives in the owning class's `_store` field.

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
  dequeue(): T;                                                // O(1) (ring buffer); throwEmptyQueue
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
```

The C# calls lower to these free helpers (each tree-shakes on its own); the `List` members of the same names are the same views for hand-written TS.

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

## Lowered `foreach` over `List<T>` (FR-057, normative emission)

`foreach (var x in <receiver>)` whose receiver's static type is `List<T>` (or a class deriving from it) lowers to exactly this shape (`$l` … `$i` follow the printer's temporary naming rules; nested loops get distinct names):

```ts
{
  const $l = <receiver>;
  const $a = $l._items;
  const $n = $a.length;
  const $v = (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) ? $l._version : 0;
  for (let $i = 0; ; $i++) {
    if ((typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) && ($l._version !== $v || $a.length !== $n)) throwModified();
    if ($i === $n) break;
    const x = $a[$i];
    /* body */
  }
}
```

The `$v` capture is guarded too, so that with the checks defined `false` no `_version` read survives (FR-058's acceptance: a property read is not removable dead code). A `null` receiver throws at `$l._items` (a `TypeError`, `NullReferenceException` through `toException`). `break`, `return` and `throw` leave with no further check; `continue` reaches the next step's check.

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
| `addEntry`, `tryGetValue`, `getValueOrDefault` (free functions over a native `Map`) | `dictionary-helpers.ts` | is | C (flip) |
| `ImmutableCollection` | `immutable-collection.ts` | is | H |
| `HashSet.size`, `has`, `delete`, `values()`, `forEach` | `hash-set.ts` | members of the reworked class, `@deprecated`, delegating to `count` / `contains` / `remove` / the iterator | C (flip) |
| `HashSet` constructor `(items?)` | `hash-set.ts` | source-compatible: `items` stays the first parameter | — |
| `MapDescriptor` / JSON kind `map` | `system/json` | is | C (flip: the context emits `dictionary`) |

One 2.x behaviour changes in R1 although C has not flipped: **`HashSet.add` returns `boolean`** instead of `this`. In 2.x a C# `if (set.Add(x))` was always true (the truthy `this`, a silent wrong value that the `boolean` fixes), and no generated or hand-written code in the repository chains `.add(…).add(…)` (searched); a hand-written consumer that chains breaks (migration notes).

## LINQ terminals (group C)

`toList()` → a new `List<T>`; `toArray()` → `T[]`; `toDictionary(...)` → `Dictionary`; `toHashSet(...)` → `HashSet`, in the term shape of `system/linq/terminals.ts`. Every operator keeps accepting `Iterable<T>` and stays a standalone function (ADR-0012). Group C writes them over the classes above; R does not add them.

## Documented divergences decided in R0 review (W4-13)

- **`toString()` without a slot** prints the type-parameter names (``List`1[T]``); `ToString()` is not a reachability trigger.
- **`Comparer<string>.Default`** is ordinal (as LINQ's `orderBy`); .NET's is culture-sensitive.
- **`Dictionary.from(null)`** names `collection` for both copy constructors; .NET names `dictionary` for the `IDictionary` overload.
- **User implementers of the collection interfaces**: `x is IList<int>` (and every collection-interface test) on an instance of a user class that implements the interface is `false` in TS, `true` in .NET; follow-up (a user-class entry form for the collection interfaces).
- **`params IReadOnlyList<T>` / `IReadOnlyCollection<T>` / `IEnumerable<T>`**: .NET passes a synthesized read-only array (`<>z__ReadOnlyArray`1`; an empty call passes `T[]`), so through `IList<T>` an indexer write, `Add` and `Clear` throw `NotSupportedException` (`Specified method is not supported.`) and `IsReadOnly` is `true`; `xs is IList<T>` is `true` for all three. TS passes `List.adopt([…], { fixedSize: true })` for the two read-only interfaces (an indexer write succeeds, `add` throws `Collection was of a fixed size.`, `clear` `Collection is read-only.`) and a native array for `IEnumerable<T>` (`xs is IList<T>` is `false`). Probed on .NET; follow-up: pass 011h's `ReadOnlyList` once it exists.
- **`initAdd` / `initAssign` and `Dictionary.of`** evaluate every element of the initializer before the first add (the array literal is built first), so a duplicate key is reported after the later elements' side effects have run; C# interleaves each element's evaluation with its `Add`.
- **`ICollection<K> c = dict.Keys`** fails `tsgo` (the views implement `IReadOnlyCollection` only); follow-up.
- **`default(T)` for a user struct** passed to `tryGetValue` / `tryDequeue` / `tryPeek` / `tryPop` / `find` / `findLast` / `getValueOrDefault` is evaluated at every call (a struct lowered to a class allocates a zeroed instance each time, hit or miss).

## 011h classes

The sections below are stage 011h's (group H), unchanged by R0; they build on the 011c API above (`hash-core.ts`, `defaultCompare`, `elementEquals` / `elementHashCode`, the interfaces, the throw helpers).

### `ReadOnlyList<T>` (`ReadOnlyCollection<T>`; stage 011h)

Its own class (`instanceof List` is `false`): a live view over an `IList<T>` or a native array, `IReadOnlyList<T>` members, `count` live, `toJSON`; its `IList<T>` / `ICollection<T>` mutators throw `NotSupportedException` with .NET's message (`Collection is read-only.`, pinned by the 011h runtime tests). Built by `new ReadOnlyList(source)` (`Array.AsReadOnly(arr)`, `new ReadOnlyCollection<T>(source)`) and by the free helper `asReadOnly(list)` (`list.AsReadOnly()`), both in `read-only-list.ts`; `List` has no `asReadOnly` member, so a bundle using only `List` pulls no `ReadOnlyList`.

### `ImmutableList<T>` (`ImmutableList<T>`, `ImmutableArray<T>`, OD-17; stage 011h)

`IReadOnlyList<T>` and `IImmutableList<T>`: `add`, `addRange`, `insert`, `remove`, `removeAt`, `removeAll`, `setItem`, `clear`, `sort` return a **new** `ImmutableList<T>` (copy-on-write over one array, O(n) per mutator; .NET's tree gives O(log n), a documented divergence revisitable by benchmark); `static empty<T>()` (memoized, lazily) and `static of(...)` / `from(...)`; `toJSON`. Builders (`ToBuilder()`, `CreateBuilder()`) are `ImmutableListBuilder<T>` (likewise `ImmutableHashSetBuilder<T>`, `ImmutableDictionaryBuilder<K,V>`): small classes that hold a `List` / `HashSet` / `Dictionary` (composition; `instanceof List` is `false`), each in its own module; `ToImmutable()` lowers to the free helper `toImmutableList(builder)` (`toImmutableHashSet`, `toImmutableDictionary`), which copies. Repeated `Add` is O(n²); FR-054's 1e4-element budget bounds it.

### `ImmutableHashSet<T>`, `ImmutableDictionary<K, V>` (stage 011h)

The `HashSet` / `Dictionary` surfaces of reads plus `IImmutableSet<T>` / `IImmutableDictionary<K,V>` mutators (`add`, `remove`, `setItem`, `union`, `except`, …) that return a **new** instance after an O(n) copy of the storage; same `elementEquals` / `elementHashCode` keying (D18, D30), same comparers; builders as for `ImmutableList`.

### `SortedSet<T>`, `SortedDictionary<K, V>` (stage 011h)

A red-black tree ordered by the comparer (the `Comparer<T>.Default` semantics of `List.sort()`, or an `IComparer<T>` / comparison argument): O(log n) `add` / `remove` / `contains` / `containsKey` / `get`, in-order enumeration with the `_version` rule, `min` / `max` / `reverse()` / `getViewBetween(lower, upper)` for the set, `keys` / `values` in key order for the dictionary. `SortedSet.add` returns `boolean`; `SortedDictionary.get` throws `KeyNotFoundException` like `Dictionary`.

### `SortedList<K, V>` (stage 011h)

Two parallel sorted arrays with binary search: O(log n) `get` / `containsKey` / `indexOfKey`, O(n) `add` / `remove`, O(1) `keys.get(i)` / `values.get(i)` / `getKeyAtIndex(i)` / `getValueAtIndex(i)`, `capacity` ignored. Enumeration in key order with the `_version` rule.

### `ConcurrentDictionary<K, V>` (stage 011h)

`class ConcurrentDictionary<K, V> implements IDictionary<K, V>`, a **sibling** of `Dictionary` (both compose the internal `hash-core.ts`; `instanceof Dictionary` is `false`, as `is Dictionary<K,V>` is in .NET), with the dictionary surface plus `getOrAdd(key, valueOrFactory)`, `addOrUpdate(key, addValueOrFactory, updateFactory)`, `tryAdd`, `tryUpdate(key, newValue, comparisonValue)`, `tryRemove(key): readonly [true, V] | readonly [false, null]`, `toArray()` (a `[K, V][]` snapshot), `isEmpty`. Each call runs to completion without yielding, so it is atomic (JavaScript shares no object between threads; FR-054). Its enumerator does **not** throw on modification: like .NET's, it is safe while the dictionary changes and is not a moment-in-time snapshot. `keys` / `values` return **snapshots** (new read-only collections), as .NET's `Keys` / `Values` do, not live views.


## Performance and size budgets (NFR-001, NFR-008)

| Operation / entry | Budget (NFR-001: normalized overhead per operation on the reference machine, hot-helper shape, default build, larger of Node.js and Bun) | Rationale |
|---|---|---|
| `List.get` / `set` | ≤ 1.0 ns each over `a[i]` / `a[i] = v` | bounds compare and, for `set`, the `_version` store; R17 measured 0.45 (sequential) / 0.74 (data-driven) / 0.82 ns for the `defineProperty` + unsigned-compare variant |
| `list[i] op= v`, direct form / closure fallback | ≤ 1.5 ns / ≤ 3.0 ns | 0.86 ns measured / closure form estimated |
| `get` at a polymorphic site | ≤ 2.0 ns | 1.08 ns measured (Node) |
| `List.add` | ≤ 0.5 ns over `push` | delegates to `push`, bumps `_version`; 0.05 ns measured |
| lowered `foreach` over `List` | ≤ 0.5 ns per element over `for..of` | 0.23 ns measured on the plain-field class with the version compare only |
| `Dictionary` / `ConcurrentDictionary` / `HashSet` primitive keys | ≤ 2.0 ns over the named `Map` / `Set` calls | estimated, confirmed in 011c |
| `Queue` / `Stack` | ≤ 1.5 ns over `push` / `a[head++]` / `pop` / `at(-1)` | estimated, confirmed in 011c |
| record keys, sorted and immutable classes | recorded per release; `SortedSet` / `SortedDictionary`: time per op at 1e5 ≤ 2.5× at 1e3; `SortedList`: reads only; immutable: 1e4 repeated `Add` ≤ 50 ms | no native equivalent |
| `Queue.dequeue` | O(1) | ring buffer (today O(n)) |
| Checks off (FR-058) | in the bun / esbuild bundle, no store to or read of `_version` outside the constructor, and `get` / `set` do not call the out-of-range throw function | inline `typeof __METANO_CHECKS__` guard, default on |
| `List` alone, minified + gz | ≤ 4586 B | NFR-008, raised by W4-25 (d) to the measured size + 10% (bun 4169 B / esbuild 4113 B; research R13's 1.67 KB had no FR-013 exceptions): FR-016's exception classes (about 0.97 KB) plus `elementEquals` / `defaultEquals` (about 0.5 KB), shared when several collections are used; slimming the exceptions is a follow-up |
| `Dictionary` / `HashSet` / `Queue` / `Stack` alone | ≤ 4506 / 4283 / 3128 / 2947 B | NFR-008, W4-25 (d): measured + 10%, same reason |
| `List` + `Dictionary` + `HashSet` together | ≤ 7112 B | NFR-008, W4-25 (d): measured bun 6465 B / esbuild 6357 B + 10%; the shared cost counted once |
| `ReadOnlyList` / `ImmutableList` / `ImmutableHashSet` / `ImmutableDictionary` / `SortedSet` / `SortedDictionary` / `SortedList` / `ConcurrentDictionary` alone | ≤ 0.8 / 2.0 / 2.0 / 2.5 / 2.5 / 3.0 / 2.5 / 3.5 KB | NFR-008, proposed |
