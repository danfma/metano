# Contract: `metano-runtime/system/collections`

**Normative for**: FR-001–FR-012, FR-016, FR-054–FR-058, NFR-001, NFR-006–NFR-009. Every class below ships in 3.0.0 (FR-054, D24): stage 011c delivers `List`, `HashSet`, `Dictionary`, `Queue`, `Stack`, their interfaces and the dictionary views; stage 011h the read-only, immutable, sorted and concurrent classes. Every member below has a runtime test (FR-041). Names follow OD-2 (C#-like camelCase). Exceptions are the classes of [contracts/runtime-types.md](./runtime-types.md) § Exceptions, imported per class from `system/exceptions/*`. `RuntimeType` and `UnknownType` come from `metano-runtime/system/reflection`; the collection descriptor factories live here (`descriptors.ts`).

## Equality (`default-comparer.ts`, D18)

```ts
export function defaultEquals<T>(a: T, b: T): boolean;      // a === b || (a is NaN && b is NaN) || (hasEquals(a) && a.equals(b))
export function defaultHashCode<T>(value: T): number;       // primitives by value; hasHashCode(value) ? value.hashCode() : identityHash(value)
export interface IEqualityComparer<T> { equals(a: T, b: T): boolean; getHashCode(value: T): number; }
```

This is the `EqualityComparer<T>.Default` analogue (today's `HashSet._areEqual` / `_getHash`, `hash-set.ts` L134-157): a user `equals()` / `hashCode()` when present (records, `IEquatable<T>` implementers lowered to classes), **reference identity otherwise**, SameValueZero for `NaN`. It is **never** `system/equality.equals`, whose structural fallback would merge two distinct class instances with equal fields (research R14). `identityHash` assigns a sequential number per object through a `WeakMap`.

## Interfaces

```ts
export interface IReadOnlyCollection<T> extends Iterable<T> {
  readonly count: number;
}
export interface IReadOnlyList<T> extends IReadOnlyCollection<T> {
  get(index: number): T;              // ArgumentOutOfRangeException
  fromEnd(n: number): T;              // list[^n]; ArgumentOutOfRangeException
  slice(start?: number, end?: number): List<T>; // list[a..b], a new List
  getRange(index: number, count: number): List<T>;
  indexOf(item: T): number;           // defaultEquals
  contains(item: T): boolean;
}
export interface ICollection<T> extends IReadOnlyCollection<T> {
  readonly isReadOnly: boolean;
  add(item: T): void;
  remove(item: T): boolean;
  clear(): void;
  contains(item: T): boolean;
  copyTo(array: T[], arrayIndex?: number): void;
}
export interface IList<T> extends ICollection<T>, IReadOnlyList<T> {
  set(index: number, value: T): void; // ArgumentOutOfRangeException
  update(index: number, f: (current: T) => T): T;            // list[i] op= …; returns the new value
  updateReturningOld(index: number, f: (current: T) => T): T; // list[i]++ in expression position
  insert(index: number, item: T): void;
  removeAt(index: number): void;
}
export interface IReadOnlyDictionary<K, V> extends Iterable<readonly [K, V]> {
  readonly count: number;
  readonly keys: KeyCollection<K>;
  readonly values: ValueCollection<V>;
  get(key: K): V;                      // KeyNotFoundException; ArgumentNullException for null
  containsKey(key: K): boolean;
  tryGetValue(key: K): readonly [true, V] | readonly [false, null];
  getValueOrDefault(key: K): V | null;
  getValueOrDefault<D>(key: K, defaultValue: D): V | D;
}
export interface IDictionary<K, V> extends IReadOnlyDictionary<K, V> {
  set(key: K, value: V): void;         // indexer write: insert or overwrite
  update(key: K, f: (current: V) => V): V;            // dict[k] op= …; KeyNotFoundException on a miss (the C# read throws)
  updateReturningOld(key: K, f: (current: V) => V): V;
  add(key: K, value: V): void;         // ArgumentException on duplicate, ArgumentNullException on null key
  remove(key: K): boolean;
  clear(): void;
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
// A view of a List's backing array (FR-056): index writes and the in-place members that keep the length;
// push / pop / shift / unshift / splice and a length assignment do not type-check (tsgo --strict, research R16).
export interface Span<T> extends ReadonlyArray<T> {
  [index: number]: T;
  sort(compareFn?: (a: T, b: T) => number): this;
  reverse(): this;
  fill(value: T, start?: number, end?: number): this;
  copyWithin(target: number, start: number, end?: number): this;
}
export interface ISet<T> extends ICollection<T>, IReadOnlySet<T> {
  add(item: T): boolean;               // C# ISet<T>.Add returns bool
  unionWith(other: Iterable<T>): void;
  intersectWith(other: Iterable<T>): void;
  exceptWith(other: Iterable<T>): void;
  symmetricExceptWith(other: Iterable<T>): void;
}
```

`ICollection<T>.add` returns `void` while `ISet<T>.add` returns `boolean`; `HashSet<T>` implements `ISet<T>` and its `add` returns `boolean`, which TS accepts for the `void`-returning `ICollection<T>.add`.

## Classes

Every class: `implements Iterable<T>`; does not extend `Array`; `Array.isArray(x) === false`; exposes `count`; declares **no `#private` fields** (proxies used by Solid / Vue / MobX cannot see through them); keeps its storage in **non-enumerable string-keyed fields** defined in the constructor through `Object.defineProperty` (`_items`, `_version`, `_comparer`, and the per-class slot `$typeArguments$List` / `$typeArguments$Dictionary` / …, always defined and `undefined` when no descriptors are passed, so each construction path has one hidden class; FR-002, FR-008), so that `Object.keys`, spread, `JSON.stringify` and the structural `equals` of `system/equality` see nothing; implements `toJSON()`; keeps a `_version` counter for the enumeration-invalidation rule (spec Edge Cases); carries the lazy `$runtimeType` getter of FR-018, which returns a data literal (a collection is reachable through `is List<T>` only via `instanceof`, so the getter is read only by `getRuntimeType`, which specializes it from the most-derived `$typeArguments$<Class>` slot).

### Common signatures

Constructors and factories share one trailing optional `typeArguments` parameter, passed only under FR-008; `null` and `undefined` are both accepted for an absent `items`:

```ts
constructor(items?: Iterable<T> | null, typeArguments?: readonly RuntimeType[]);
static of<T>(items: readonly T[], typeArguments?: readonly RuntimeType[]): List<T>;    // collection initializers / expressions
static from<T>(items: Iterable<T>, typeArguments?): List<T>;                           // copy
static withCapacity<T>(capacity: number, typeArguments?): List<T>;                     // capacity hint dropped, argument evaluated once
static adopt<T>(array: T[], options?: { fixedSize?: boolean }, typeArguments?): List<T>; // zero copy: the array is the backing store
```

`List.of([1, 2])` with no descriptors is the common emission; `List.of([1, 2], [Int32Type])` only when the compilation asks about `List` (FR-008).

### `List<T>`

```ts
export class List<T> implements IList<T> {
  readonly count: number;
  get(index: number): T;  set(index: number, value: T): void;  update(index, f): T;  updateReturningOld(index, f): T;
  fromEnd(n: number): T;  slice(start?: number, end?: number): List<T>;  getRange(index: number, count: number): List<T>;
  add(item: T): void;  addRange(items: Iterable<T>): void;
  insert(index: number, item: T): void;  insertRange(index: number, items: Iterable<T>): void;
  remove(item: T): boolean;  removeAt(index: number): void;  removeRange(index: number, count: number): void;
  removeAll(predicate: (item: T) => boolean): number;
  clear(): void;
  contains(item: T): boolean;  indexOf(item: T): number;  lastIndexOf(item: T): number;
  find(predicate: (item: T) => boolean): T | null;  findIndex(predicate: (item: T) => boolean): number;
  findLast(predicate): T | null;  findAll(predicate): List<T>;  exists(predicate): boolean;  trueForAll(predicate): boolean;
  forEach(action: (item: T) => void): void;
  convertAll<U>(converter: (item: T) => U): List<U>;
  binarySearch(item: T, comparison?: (a: T, b: T) => number): number;   // ~insertionPoint when absent, as .NET
  sort(comparison?: (a: T, b: T) => number): void;  reverse(): void;
  toArray(): T[];                                             // detached shallow copy (slice of the backing array); ToArray() / ToJsArray() (FR-055)
  asJsArray(): T[];                                           // the backing array itself; AsJsArray() (FR-056)
  asSpan(): Span<T>;                                          // the backing array itself; AsSpan() / CollectionsMarshal.AsSpan(list)
  asReadOnlySpan(): ReadonlyArray<T>;                         // the backing array itself; AsReadOnlySpan()
  toJSON(): T[];
  [Symbol.iterator](): Iterator<T>;
}
```

- `get` / `set` / `update` / `insert` / `removeAt` / `fromEnd` / `removeRange` / `getRange` throw `ArgumentOutOfRangeException` with .NET's message (`Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')`).
- **ABI and `foreach` lowering** (FR-057): `_items` and `_version` are declared in the public `.d.ts` and are read by generated code; indexers emit `get` / `set` (FR-010). `foreach (var x in <receiver>)` over a static `List<T>` lowers to:

  ```ts
  {
    const $l = <receiver>;            // always a temporary: the body may reassign a local receiver
    const $a = $l._items; const $v = $l._version; const $n = $a.length;
    for (let $i = 0; ; $i++) {
      if ($l._version !== $v || $a.length !== $n) throwModified(); // every MoveNext, the final one included
      if ($i === $n) break;
      const x = $a[$i];
      /* body: a `break`, `return` or `throw` leaves without a further check */
    }
  }
  ```

  `continue` in the body reaches the next step's check. `goto` is not lowered by Metano: inside a `foreach` it reports `MS0001` and the method body becomes a stub; if `goto` is supported later, it also leaves without a further check. With FR-058's constant defined as `false`, the check line is eliminated. The design that meets NFR-001 (one unsigned bounds compare, cold `throwOOR` / `throwModified`, constructor field order) is in plan.md § Benchmark plan and research R17.
- A fixed-size adopted list (FR-003) throws `NotSupportedException` (`Collection was of a fixed size.`) from `add`, `addRange`, `insert`, `insertRange`, `remove`, `removeAt`, `removeRange`, `removeAll`, `clear`; `set` / `update` / `sort` / `reverse` write through to the shared array. `asArray(list)` (reflection helper) returns its backing array.
- `contains` / `indexOf` / `remove` / `lastIndexOf` / `binarySearch` use `defaultEquals` with a `===` fast path for primitives.
- `sort()` without a comparison uses `Comparer<T>.Default` semantics for numbers, strings, booleans, temporal values (by tag) and `IComparable` implementers (`compareTo`); otherwise throws `InvalidOperationException` (`Failed to compare two elements in the array.`).
- Mutation during enumeration throws `InvalidOperationException` (`Collection was modified; enumeration operation may not execute.`) through `_version` and the backing array's `length` recorded at the start (two integer compares per `next()`), so a length change made through `asJsArray()` is detected too (FR-057). Element writes through a view bump nothing: through `asSpan()` this matches .NET (a span write bumps nothing); through `asJsArray()` it diverges, because .NET's `JsArray<T>` writes through the list's indexer and bumps the version (documented, FR-057). The lowered `foreach` above performs the same two checks (normative, FR-057).
- **One backing array for life** (FR-057): `_items` is assigned once (in the constructor, or by `adopt`) and every member mutates it in place (`clear` sets `length = 0`, `sort` / `reverse` are in place); `count` reads `_items.length`. `asJsArray()`, `asSpan()` and `asReadOnlySpan()` return `_items` itself (`list.asSpan() === list.asJsArray()`, and the free helpers return the same object), so they never go stale (unlike a .NET span after a reallocation, documented). Nothing polices a view at run time: `Span<T>` and `ReadonlyArray<T>` restrict what typed TS may do, a cast does not; a write past the end through `asJsArray()` leaves holes that `get` returns as `undefined` (documented).
- Memory: one backing array plus `count` = the array's `length`; no per-element structures. A C# `List<T>` member not listed here (for example `Capacity`, `TrimExcess`, `EnsureCapacity`, `ForEach` with index) reports `MS0001` at the call site (FR-001).

### `ReadOnlyList<T>` (`ReadOnlyCollection<T>`; stage 011h)

Its own class (`instanceof List` is `false`): a live view over an `IList<T>` or a native array, `IReadOnlyList<T>` members, `count` live, `toJSON`; its `IList<T>` / `ICollection<T>` mutators throw `NotSupportedException` with .NET's message (`Collection is read-only.`, pinned by the 011h runtime tests). Built by `new ReadOnlyList(source)` (`Array.AsReadOnly(arr)`, `new ReadOnlyCollection<T>(source)`) and by the free helper `asReadOnly(list)` (`list.AsReadOnly()`), both in `read-only-list.ts`; `List` has no `asReadOnly` member, so a bundle using only `List` pulls no `ReadOnlyList`.

### `ImmutableList<T>` (`ImmutableList<T>`, `ImmutableArray<T>`, OD-17; stage 011h)

`IReadOnlyList<T>` and `IImmutableList<T>`: `add`, `addRange`, `insert`, `remove`, `removeAt`, `removeAll`, `setItem`, `clear`, `sort` return a **new** `ImmutableList<T>` (copy-on-write over one array, O(n) per mutator; .NET's tree gives O(log n), a documented divergence revisitable by benchmark); `static empty<T>()` (memoized, lazily) and `static of(...)` / `from(...)`; `toJSON`. Builders (`ToBuilder()`, `CreateBuilder()`) are `ImmutableListBuilder<T>` (likewise `ImmutableHashSetBuilder<T>`, `ImmutableDictionaryBuilder<K,V>`): small classes that hold a `List` / `HashSet` / `Dictionary` (composition; `instanceof List` is `false`), each in its own module; `ToImmutable()` lowers to the free helper `toImmutableList(builder)` (`toImmutableHashSet`, `toImmutableDictionary`), which copies. Repeated `Add` is O(n²); FR-054's 1e4-element budget bounds it.

### `ImmutableHashSet<T>`, `ImmutableDictionary<K, V>` (stage 011h)

The `HashSet` / `Dictionary` surfaces of reads plus `IImmutableSet<T>` / `IImmutableDictionary<K,V>` mutators (`add`, `remove`, `setItem`, `union`, `except`, …) that return a **new** instance after an O(n) copy of the storage; same `defaultEquals` / `defaultHashCode` keying (D18), same comparers; builders as for `ImmutableList`.

### `SortedSet<T>`, `SortedDictionary<K, V>` (stage 011h)

A red-black tree ordered by the comparer (the `Comparer<T>.Default` semantics of `List.sort()`, or an `IComparer<T>` / comparison argument): O(log n) `add` / `remove` / `contains` / `containsKey` / `get`, in-order enumeration with the `_version` rule, `min` / `max` / `reverse()` / `getViewBetween(lower, upper)` for the set, `keys` / `values` in key order for the dictionary. `SortedSet.add` returns `boolean`; `SortedDictionary.get` throws `KeyNotFoundException` like `Dictionary`.

### `SortedList<K, V>` (stage 011h)

Two parallel sorted arrays with binary search: O(log n) `get` / `containsKey` / `indexOfKey`, O(n) `add` / `remove`, O(1) `keys.get(i)` / `values.get(i)` / `getKeyAtIndex(i)` / `getValueAtIndex(i)`, `capacity` ignored. Enumeration in key order with the `_version` rule.

### `ConcurrentDictionary<K, V>` (stage 011h)

`class ConcurrentDictionary<K, V> implements IDictionary<K, V>`, a **sibling** of `Dictionary` (both compose the internal `hash-core.ts`; `instanceof Dictionary` is `false`, as `is Dictionary<K,V>` is in .NET), with the dictionary surface plus `getOrAdd(key, valueOrFactory)`, `addOrUpdate(key, addValueOrFactory, updateFactory)`, `tryAdd`, `tryUpdate(key, newValue, comparisonValue)`, `tryRemove(key): readonly [true, V] | readonly [false, null]`, `toArray()` (a `[K, V][]` snapshot), `isEmpty`. Each call runs to completion without yielding, so it is atomic (JavaScript shares no object between threads; FR-054). Its enumerator does **not** throw on modification: like .NET's, it is safe while the dictionary changes and is not a moment-in-time snapshot. `keys` / `values` return **snapshots** (new read-only collections), as .NET's `Keys` / `Values` do, not live views.

### `Dictionary<K, V>`

```ts
export class Dictionary<K, V> implements IDictionary<K, V> {
  constructor(entries?: Iterable<readonly [K, V]> | null, comparer?: IEqualityComparer<K> | null, typeArguments?);
  static of<K, V>(entries: ReadonlyArray<readonly [K, V]>, typeArguments?): Dictionary<K, V>;      // { { k, v } } initializer: add semantics, duplicate throws
  static assign<K, V>(entries: ReadonlyArray<readonly [K, V]>, typeArguments?): Dictionary<K, V>;  // { [k] = v } initializer: last wins
  static from<K, V>(source: Iterable<readonly [K, V]> | ReadonlyMap<K, V>, comparer?, typeArguments?): Dictionary<K, V>;
  readonly count: number;
  readonly keys: KeyCollection<K>;  readonly values: ValueCollection<V>;
  get(key: K): V;  set(key: K, value: V): void;  update(key, f): V;  updateReturningOld(key, f): V;
  add(key: K, value: V): void;  tryAdd(key: K, value: V): boolean;
  remove(key: K): boolean;  clear(): void;
  containsKey(key: K): boolean;  containsValue(value: V): boolean;
  tryGetValue(key: K): readonly [true, V] | readonly [false, null];
  getValueOrDefault(key: K): V | null;  getValueOrDefault<D>(key: K, defaultValue: D): V | D;
  toMap(): Map<K, V>;                                            // interop copy
  toJSON(): Record<string, V>;                                   // String(key) → value
  [Symbol.iterator](): Iterator<readonly [K, V]>;
}
export class KeyCollection<K> implements IReadOnlyCollection<K> { readonly count; contains(key: K): boolean; [Symbol.iterator](); toArray(): K[]; toJSON(): K[]; }
export class ValueCollection<V> implements IReadOnlyCollection<V> { readonly count; contains(value: V): boolean; [Symbol.iterator](); toArray(): V[]; toJSON(): V[]; }
```

- Keys: primitives (string, number, boolean, bigint, symbol) go through a native `Map` fast path; objects hash through `defaultHashCode` / `defaultEquals` (or the comparer) into buckets, as `HashSet` does today (`hash-set.ts` L100-157). A record key with `equals` / `hashCode` addresses one entry; two instances of a class without them are two keys.
- `get` throws `KeyNotFoundException` (`The given key '<key>' was not present in the dictionary.`); `get(null)` / `containsKey(null)` / `add(null, …)` / `remove(null)` throw `ArgumentNullException` (`Value cannot be null. (Parameter 'key')`); `add` throws `ArgumentException` (`An item with the same key has already been added. Key: <key>`).
- Enumeration order is insertion order, also after removals (.NET reuses freed slots; documented divergence, OD-18). `remove` and `clear` do not invalidate a running enumeration (.NET Core 3.0+); `add` / `set` of a new key / `update` do.
- `keys` / `values` are live views (FR-012); `[...dict.keys]` copies.

### `HashSet<T>` (reworked)

Today's class (`hash-set.ts`) keeps its storage and gains the `ISet<T>` surface: `add(): boolean`, `remove(): boolean`, `contains`, `count`, `clear`, the set algebra of `ISet<T>` / `IReadOnlySet<T>`, a `comparer` constructor argument, `static of` / `from`, `toArray` (a detached copy, FR-055), `toJSON`, `_version`. No view: .NET has no span over a set (FR-056). Removed: `size`, `has`, `delete`, `values()`, `forEach` (breaking, migration notes). `system/equality.equals` is **not** taught about `HashSet` or `Dictionary`: a record with a collection field compares it by reference, as .NET does (D18).

### `Queue<T>`

Ring buffer: `enqueue`, `dequeue` (throws `InvalidOperationException`, `Queue empty.`), `peek`, `tryDequeue(): readonly [true, T] | readonly [false, null]`, `tryPeek`, `count`, `contains`, `clear`, `toArray` (a detached copy, front to back, FR-055), `toJSON`, iteration front-to-back, `_version`. `dequeue` is O(1) amortised (today `shift` is O(n)).

### `Stack<T>`

`push`, `pop` (throws `InvalidOperationException`, `Stack empty.`), `peek`, `tryPop`, `tryPeek`, `count`, `contains`, `clear`, `toArray` (a detached copy, top first, FR-055), `toJSON` (top first), iteration **top-first** (C# semantics; `Enumerators.cs` left `Stack` unmapped for this reason), `_version`.

## Helpers (`helpers.ts`)

```ts
export function fromEnd<T>(array: readonly T[], n: number): T;          // arr[^n]; IndexOutOfRangeException
export function isIterable(value: unknown): value is Iterable<unknown>;  // Symbol.iterator present (strings included)
export function asArray(value: unknown): unknown[] | null;               // native array, or the backing array of a fixed-size adopted List, else null
export function asJsArray<T>(list: List<T> | null): T[];                  // AsJsArray(); the backing array; ArgumentNullException for null (FR-056)
export function asSpan<T>(list: List<T> | null): Span<T>;                 // AsSpan() / CollectionsMarshal.AsSpan(list); the backing array; [] for null
export function asReadOnlySpan<T>(list: List<T> | null): ReadonlyArray<T>; // AsReadOnlySpan(); the backing array; [] for null
```

The C# calls lower to these free helpers (each tree-shakes on its own); the `List` members `asJsArray()` / `asSpan()` / `asReadOnlySpan()` are the same views for hand-written TS.

`addEntry`, `listRemove`, `tryGetValue`, `getValueOrDefault` (free functions) are **removed**; their callers are the class members above. `ImmutableCollection` is removed by stage 011h, replaced by the immutable classes.

## Descriptor factories (`descriptors.ts`, FR-017)

`listType(elem)`, `dictionaryType(key, value)`, `hashSetType(elem)`, `queueType(elem)`, `stackType(elem)`, `immutableListType(elem)`, `readOnlyListType(elem)`, `immutableHashSetType(elem)`, `immutableDictionaryType(key, value)`, `sortedSetType(elem)`, `sortedDictionaryType(key, value)`, `sortedListType(key, value)`, `concurrentDictionaryType(key, value)` (these with stage 011h), and the interface descriptors `iListType`, `iCollectionType`, `iReadOnlyListType`, `iReadOnlyCollectionType`, `iDictionaryType`, `iReadOnlyDictionaryType`, `iSetType`, `iReadOnlySetType`, and (stage 011h) `iImmutableListType`, `iImmutableSetType`, `iImmutableDictionaryType`, each memoized on argument identity and carrying `variance` (`invariant` for classes, `covariant` for the `out` parameters of the read-only interfaces and `IEnumerable<out T>`). Importing `List` alone does not pull them (separate module; measured).

## Type-argument descriptors (FR-008)

Passed only when the compilation asks a runtime question about that collection kind; `UnknownType` for a non-reified `T`; a class argument's descriptor only when the class is reached. Stored in the per-class slot (`$typeArguments$List`, …), defined once by the runtime class's constructor; a C# subclass that closes the arguments (`class IntList : List<int>`) passes them to `super` and defines no slot. Runtime-built collections (`slice`, `getRange`, `findAll`, `convertAll`, `toList`) copy the source's slot when present. The test rule: identity for invariant classes, assignability for covariant interfaces, wildcard for an absent field or `UnknownType`.

## LINQ terminals

`toList()` → `List<T>` (new); `toArray()` → `T[]`; `toDictionary(...)` → `Dictionary`; `toHashSet(...)` → `HashSet`. Every operator keeps accepting `Iterable<T>` and stays a standalone function (ADR-0012), so runtime collections are sources without conversion and the operators shake independently of the classes.

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
| `List` alone, minified + gz | ≤ 2.5 KB | measured 1.67 KB with the full surface (research R13) |
| `Dictionary` / `HashSet` / `Queue` / `Stack` alone | ≤ 3.0 / 1.6 / 1.2 / 1.2 KB | NFR-008 |
| `ReadOnlyList` / `ImmutableList` / `ImmutableHashSet` / `ImmutableDictionary` / `SortedSet` / `SortedDictionary` / `SortedList` / `ConcurrentDictionary` alone | ≤ 0.8 / 2.0 / 2.0 / 2.5 / 2.5 / 3.0 / 2.5 / 3.5 KB | NFR-008, proposed |
