# Contract: `metano-runtime/system/collections`

**Normative for**: FR-001–FR-012, FR-016, NFR-001, NFR-006. Every member below has a runtime test (FR-037). Names follow OD-2 (C#-like camelCase). Exceptions are the classes of [contracts/runtime-types.md](./runtime-types.md) § Exceptions. `RuntimeType` and `Unknown` come from `metano-runtime/system/reflection`.

## Interfaces

```ts
export interface IReadOnlyCollection<T> extends Iterable<T> {
  readonly count: number;
}
export interface IReadOnlyList<T> extends IReadOnlyCollection<T> {
  get(index: number): T;              // ArgumentOutOfRangeException
  fromEnd(n: number): T;              // list[^n]; ArgumentOutOfRangeException
  slice(start?: number, end?: number): List<T>; // list[a..b], a new List
  indexOf(item: T): number;           // equals()-based
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
  insert(index: number, item: T): void;
  removeAt(index: number): void;
}
export interface IReadOnlyDictionary<K, V> extends Iterable<readonly [K, V]> {
  readonly count: number;
  readonly keys: KeyCollection<K>;
  readonly values: ValueCollection<V>;
  get(key: K): V;                      // KeyNotFoundException
  containsKey(key: K): boolean;
  tryGetValue(key: K): readonly [true, V] | readonly [false, null];
  getValueOrDefault(key: K): V | null;
  getValueOrDefault<D>(key: K, defaultValue: D): V | D;
}
export interface IDictionary<K, V> extends IReadOnlyDictionary<K, V> {
  set(key: K, value: V): void;         // indexer write: insert or overwrite
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
export interface IEqualityComparer<T> {
  equals(a: T, b: T): boolean;
  getHashCode(value: T): number;
}
```

`ICollection<T>.add` returns `void` while `ISet<T>.add` returns `boolean`; `HashSet<T>` implements `ISet<T>` and its `add` returns `boolean`, which TS accepts for the `void`-returning `ICollection<T>.add` (a `boolean` is assignable to a `void` return in an implementation).

## Classes

Every class: `implements Iterable<T>`, does not extend `Array`, has `Array.isArray(x) === false`, exposes `count`, stores its type-argument descriptors in a non-enumerable `[typeArguments]` symbol field (FR-008), and carries `static readonly [runtimeType]` (FR-018; collections are always reached because `is List<T>` is answered through the class).

### `List<T>`

```ts
export class List<T> implements IList<T> {
  constructor(items?: Iterable<T> | null, typeArguments?: readonly [RuntimeType<T>]);
  static of<T>(...items: T[]): List<T>;                       // collection initializers / expressions
  static from<T>(items: Iterable<T>, typeArguments?): List<T>; // copy
  static withCapacity<T>(capacity: number, typeArguments?): List<T>; // capacity hint dropped, argument evaluated
  static adopt<T>(array: T[], typeArguments?): List<T>;       // zero copy: the array is the backing store
  readonly count: number;
  get(index: number): T;  set(index: number, value: T): void;
  fromEnd(n: number): T;  slice(start?: number, end?: number): List<T>;
  add(item: T): void;  addRange(items: Iterable<T>): void;
  insert(index: number, item: T): void;  insertRange(index: number, items: Iterable<T>): void;
  remove(item: T): boolean;  removeAt(index: number): void;  removeRange(index: number, count: number): void;
  removeAll(predicate: (item: T) => boolean): number;
  clear(): void;
  contains(item: T): boolean;  indexOf(item: T): number;  lastIndexOf(item: T): number;
  find(predicate: (item: T) => boolean): T | null;  findIndex(predicate: (item: T) => boolean): number;
  findLast(predicate): T | null;  findAll(predicate): List<T>;  exists(predicate): boolean;  trueForAll(predicate): boolean;
  forEach(action: (item: T) => void): void;
  sort(comparison?: (a: T, b: T) => number): void;  reverse(): void;
  toArray(): T[];                                             // copy; ToArray() / ToJsArray()
  asReadOnly(): ReadOnlyList<T>;
  [Symbol.iterator](): Iterator<T>;
}
```

- `get` / `set` / `insert` / `removeAt` / `fromEnd` / `removeRange` throw `ArgumentOutOfRangeException` with .NET's message (`Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')`).
- `contains` / `indexOf` / `remove` / `lastIndexOf` use `equals` from `system/equality` with a `===` fast path for primitives.
- `sort()` without a comparison uses `Comparer<T>.Default` semantics for numbers, strings, booleans, `Temporal` values and `IComparable` implementers (`compareTo`); otherwise throws `InvalidOperationException` (`Failed to compare two elements in the array.`).
- Mutation during enumeration throws `InvalidOperationException` (`Collection was modified; enumeration operation may not execute.`) through a version counter, as .NET does.
- Memory: one backing array plus `count` is the array's `length`; no per-element structures.

### `ReadOnlyList<T>` (`ReadOnlyCollection<T>`)

A view over an `IList<T>` or a native array: `IReadOnlyList<T>` members only, `count` live. `Array.asReadOnly(arr)` and `list.asReadOnly()` build it.

### `ImmutableList<T>` (`ImmutableList<T>`, `ImmutableArray<T>`, OD-17)

`IReadOnlyList<T>` plus `add`, `addRange`, `insert`, `remove`, `removeAt`, `removeAll`, `set`, `clear`, `sort` returning a **new** `ImmutableList<T>` (copy-on-write over one array); `static empty<T>()` (a memoized empty instance per element descriptor) and `static of(...)` / `from(...)`.

### `Dictionary<K, V>`

```ts
export class Dictionary<K, V> implements IDictionary<K, V> {
  constructor(entries?: Iterable<readonly [K, V]> | null, comparer?: IEqualityComparer<K> | null, typeArguments?: readonly [RuntimeType<K>, RuntimeType<V>]);
  static of<K, V>(entries: ReadonlyArray<readonly [K, V]>, typeArguments?): Dictionary<K, V>;   // initializers (uses add: duplicate throws)
  static from<K, V>(source: Iterable<readonly [K, V]> | ReadonlyMap<K, V>, comparer?, typeArguments?): Dictionary<K, V>;
  readonly count: number;
  readonly keys: KeyCollection<K>;  readonly values: ValueCollection<V>;
  get(key: K): V;  set(key: K, value: V): void;
  add(key: K, value: V): void;  tryAdd(key: K, value: V): boolean;
  remove(key: K): boolean;  clear(): void;
  containsKey(key: K): boolean;  containsValue(value: V): boolean;
  tryGetValue(key: K): readonly [true, V] | readonly [false, null];
  getValueOrDefault(key: K): V | null;  getValueOrDefault<D>(key: K, defaultValue: D): V | D;
  toMap(): Map<K, V>;                                            // interop copy
  [Symbol.iterator](): Iterator<readonly [K, V]>;
}
export class KeyCollection<K> implements IReadOnlyCollection<K> { readonly count; contains(key: K): boolean; [Symbol.iterator](); toArray(): K[]; }
export class ValueCollection<V> implements IReadOnlyCollection<V> { readonly count; contains(value: V): boolean; [Symbol.iterator](); toArray(): V[]; }
```

- Keys: primitives (string, number, boolean, bigint, symbol, null) go through a native `Map` fast path; objects hash through `hashCode` / `equals` (or the comparer) into buckets, as `HashSet` does today (`hash-set.ts` L100-157). A record key with value equality addresses one entry.
- `get` throws `KeyNotFoundException` (`The given key '<key>' was not present in the dictionary.`); `add` throws `ArgumentException` (`An item with the same key has already been added. Key: <key>`) and `ArgumentNullException` for a `null` key.
- Enumeration order is insertion order; removal does not reorder.
- `keys` / `values` are live views (FR-012); `[...dict.keys]` copies.

### `HashSet<T>` (reworked)

Today's class (`hash-set.ts`) keeps its storage and gains the `ISet<T>` surface: `add(): boolean`, `remove(): boolean`, `contains`, `count`, `clear`, the set algebra of `ISet<T>` / `IReadOnlySet<T>`, a `comparer` constructor argument, `static of` / `from`, `toArray`. Removed: `size`, `has`, `delete`, `values()`, `forEach` (breaking, migration notes). `equals(a, b)` in `system/equality` recognises `HashSet` (set equality) and `Dictionary` (entry equality) so record fields of these types compare by value.

### `Queue<T>`

Ring buffer: `enqueue`, `dequeue` (throws `InvalidOperationException`, `Queue empty.`), `peek`, `tryDequeue(): readonly [true, T] | readonly [false, null]`, `tryPeek`, `count`, `contains`, `clear`, `toArray`, iteration front-to-back. `dequeue` is O(1) amortised (today `shift` is O(n)).

### `Stack<T>`

`push`, `pop` (throws `InvalidOperationException`, `Stack empty.`), `peek`, `tryPop`, `tryPeek`, `count`, `contains`, `clear`, `toArray` (top first), iteration **top-first** (C# semantics; `Enumerators.cs` left `Stack` unmapped for this reason).

## Helpers (`system/collections/helpers.ts`)

```ts
export function fromEnd<T>(array: readonly T[], n: number): T;          // arr[^n]; IndexOutOfRangeException
export function isIterable(value: unknown): value is Iterable<unknown>;  // Symbol.iterator present (strings included)
```

`addEntry`, `listRemove`, `tryGetValue`, `getValueOrDefault` (free functions) and `ImmutableCollection` are **removed**; their callers are the class members above.

## Type-argument descriptors (FR-008)

Factories and constructors take an optional trailing `typeArguments` tuple, so `of` takes its items as one array: `of<T>(items: T[], typeArguments?)`. The compiler passes the tuple when every argument is statically closed: `new List<string>()` → `new List<string>(null, [String])`, `new List<int> { 1, 2 }` → `List.of([1, 2], [Int32])`. Primitives are passed like any other descriptor, for uniformity. A class argument is passed as its descriptor only when the class is reached (FR-027); otherwise `Unknown`. When an argument is an open, non-reified `T`, `Unknown` is passed. A collection created by the runtime (`slice`, `findAll`, `toList`) copies its source's descriptors. `is List<string>` compares element descriptors by identity and treats `Unknown` on either side as a match.

## LINQ terminals

`toList()` → `List<T>` (new); `toArray()` → `T[]`; `toDictionary(...)` → `Dictionary`; `toHashSet(...)` → `HashSet`. Every operator keeps accepting `Iterable<T>`, so runtime collections are sources without conversion.

## Performance budgets (NFR-001)

| Operation | Budget vs native | Rationale |
|---|---|---|
| `List.get` / `set` | ≤ 2.0× `arr[i]` | one bounds check + an inlinable method |
| `List.add` | ≤ 1.5× `push` | delegates to `push` |
| `for..of` over `List` | ≤ 1.5× over array | delegates to the array iterator |
| `Dictionary` primitive keys | ≤ 1.5× `Map` | native `Map` fast path |
| `Dictionary` record keys | ≤ 3× `Map` (which cannot do it) | hashing is new semantics |
| `Queue.dequeue` | O(1) | ring buffer (today O(n)) |
| Module size | ≤ 12 KB gz for collections + exceptions + reflection | NFR-006 |
