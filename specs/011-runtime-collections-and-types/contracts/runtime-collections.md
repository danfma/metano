# Contract: `metano-runtime/system/collections`

**Normative for**: FR-001–FR-012, FR-016, NFR-001, NFR-006–NFR-009. Every member below has a runtime test (FR-041). Names follow OD-2 (C#-like camelCase). Exceptions are the classes of [contracts/runtime-types.md](./runtime-types.md) § Exceptions, imported per class from `system/exceptions/*`. `RuntimeType` and `UnknownType` come from `metano-runtime/system/reflection`; the collection descriptor factories live here (`descriptors.ts`).

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

Every class: `implements Iterable<T>`; does not extend `Array`; `Array.isArray(x) === false`; exposes `count`; declares **no `#private` fields** (proxies used by Solid / Vue / MobX cannot see through them); keeps its storage in **non-enumerable string-keyed fields** defined in the constructor through `Object.defineProperty` (`_items`, `_version`, `_comparer`, and `$typeArguments` when passed; FR-002), so that `Object.keys`, spread, `JSON.stringify` and the structural `equals` of `system/equality` see nothing; implements `toJSON()`; keeps a `_version` counter for the enumeration-invalidation rule (spec Edge Cases); carries the lazy `$runtimeType` getter of FR-018 (a collection is reachable through `is List<T>` only via `instanceof`, so the getter is read only by `getType`).

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
  toArray(): T[];                                             // copy; ToArray() / ToJsArray()
  asReadOnly(): ReadOnlyList<T>;
  toJSON(): T[];
  [Symbol.iterator](): Iterator<T>;
}
```

- `get` / `set` / `update` / `insert` / `removeAt` / `fromEnd` / `removeRange` / `getRange` throw `ArgumentOutOfRangeException` with .NET's message (`Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')`).
- A fixed-size adopted list (FR-003) throws `NotSupportedException` (`Collection was of a fixed size.`) from `add`, `addRange`, `insert`, `insertRange`, `remove`, `removeAt`, `removeRange`, `removeAll`, `clear`; `set` / `update` / `sort` / `reverse` write through to the shared array. `asArray(list)` (reflection helper) returns its backing array.
- `contains` / `indexOf` / `remove` / `lastIndexOf` / `binarySearch` use `defaultEquals` with a `===` fast path for primitives.
- `sort()` without a comparison uses `Comparer<T>.Default` semantics for numbers, strings, booleans, temporal values (by tag) and `IComparable` implementers (`compareTo`); otherwise throws `InvalidOperationException` (`Failed to compare two elements in the array.`).
- Mutation during enumeration throws `InvalidOperationException` (`Collection was modified; enumeration operation may not execute.`) through `_version` (one integer compare per `next()`).
- Memory: one backing array plus `count` = the array's `length`; no per-element structures. A C# `List<T>` member not listed here (for example `Capacity`, `TrimExcess`, `EnsureCapacity`, `ForEach` with index) reports `MS0001` at the call site (FR-001).

### `ReadOnlyList<T>` (`ReadOnlyCollection<T>`)

A view over an `IList<T>` or a native array: `IReadOnlyList<T>` members only, `count` live, `toJSON`. `Array.asReadOnly(arr)` and `list.asReadOnly()` build it.

### `ImmutableList<T>` (`ImmutableList<T>`, `ImmutableArray<T>`, OD-17)

`IReadOnlyList<T>` plus `add`, `addRange`, `insert`, `remove`, `removeAt`, `removeAll`, `set`, `clear`, `sort` returning a **new** `ImmutableList<T>` (copy-on-write over one array); `static empty<T>()` (a memoized empty instance) and `static of(...)` / `from(...)`; `toJSON`.

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

Today's class (`hash-set.ts`) keeps its storage and gains the `ISet<T>` surface: `add(): boolean`, `remove(): boolean`, `contains`, `count`, `clear`, the set algebra of `ISet<T>` / `IReadOnlySet<T>`, a `comparer` constructor argument, `static of` / `from`, `toArray`, `toJSON`, `_version`. Removed: `size`, `has`, `delete`, `values()`, `forEach` (breaking, migration notes). `system/equality.equals` is **not** taught about `HashSet` or `Dictionary`: a record with a collection field compares it by reference, as .NET does (D18).

### `Queue<T>`

Ring buffer: `enqueue`, `dequeue` (throws `InvalidOperationException`, `Queue empty.`), `peek`, `tryDequeue(): readonly [true, T] | readonly [false, null]`, `tryPeek`, `count`, `contains`, `clear`, `toArray`, `toJSON`, iteration front-to-back, `_version`. `dequeue` is O(1) amortised (today `shift` is O(n)).

### `Stack<T>`

`push`, `pop` (throws `InvalidOperationException`, `Stack empty.`), `peek`, `tryPop`, `tryPeek`, `count`, `contains`, `clear`, `toArray` (top first), `toJSON` (top first), iteration **top-first** (C# semantics; `Enumerators.cs` left `Stack` unmapped for this reason), `_version`.

## Helpers (`helpers.ts`)

```ts
export function fromEnd<T>(array: readonly T[], n: number): T;          // arr[^n]; IndexOutOfRangeException
export function isIterable(value: unknown): value is Iterable<unknown>;  // Symbol.iterator present (strings included)
export function asArray(value: unknown): unknown[] | null;               // native array, or the backing array of a fixed-size adopted List, else null
```

`addEntry`, `listRemove`, `tryGetValue`, `getValueOrDefault` (free functions) and `ImmutableCollection` are **removed**; their callers are the class members above.

## Descriptor factories (`descriptors.ts`, FR-017)

`listType(elem)`, `dictionaryType(key, value)`, `hashSetType(elem)`, `queueType(elem)`, `stackType(elem)`, `immutableListType(elem)`, `readOnlyListType(elem)`, and the interface descriptors `iListType`, `iCollectionType`, `iReadOnlyListType`, `iReadOnlyCollectionType`, `iDictionaryType`, `iReadOnlyDictionaryType`, `iSetType`, `iReadOnlySetType`, each memoized on argument identity and carrying `variance` (`invariant` for classes, `covariant` for the `out` parameters of the read-only interfaces and `IEnumerable<out T>`). Importing `List` alone does not pull them (separate module; measured).

## Type-argument descriptors (FR-008)

Passed only when the compilation asks a runtime question about that collection kind; `UnknownType` for a non-reified `T`; a class argument's descriptor only when the class is reached. Runtime-built collections (`slice`, `getRange`, `findAll`, `convertAll`, `toList`) copy the source's field when present. The test rule: identity for invariant classes, assignability for covariant interfaces, wildcard for an absent field or `UnknownType`.

## LINQ terminals

`toList()` → `List<T>` (new); `toArray()` → `T[]`; `toDictionary(...)` → `Dictionary`; `toHashSet(...)` → `HashSet`. Every operator keeps accepting `Iterable<T>` and stays a standalone function (ADR-0012), so runtime collections are sources without conversion and the operators shake independently of the classes.

## Performance and size budgets (NFR-001, NFR-008)

| Operation / entry | Budget | Rationale |
|---|---|---|
| `List.get` / `set` | ≤ 2.0× `arr[i]` | one bounds check + an inlinable method |
| `List.add` | ≤ 1.5× `push` | delegates to `push`, bumps `_version` |
| `for..of` over `List` | ≤ 1.5× over array | the array iterator plus one version compare per step |
| `Dictionary` primitive keys | ≤ 1.5× `Map` | native `Map` fast path |
| `Dictionary` record keys | ≤ 3× `Map` (which cannot do it) | hashing is new semantics |
| `Queue.dequeue` | O(1) | ring buffer (today O(n)) |
| `List` alone, minified + gz | ≤ 2.5 KB | measured 1.67 KB with the full surface (research R13) |
| `Dictionary` / `HashSet` / `Queue` / `Stack` alone | ≤ 3.0 / 1.6 / 1.2 / 1.2 KB | NFR-008 |
