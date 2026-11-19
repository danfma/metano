/**
 * `System.Collections.Generic.List<T>` (spec 011 FR-001–FR-010, FR-055–FR-058;
 * contracts/runtime-collections.md § `List<T>`).
 *
 * One native array (`_items`) holds the elements for the list's whole life;
 * generated code reads `_items` and `_version` (the lowered `foreach`), so both
 * are part of the public declarations. The storage is defined through
 * `Object.defineProperty` (non-enumerable, always the same fields in the same
 * order), so spread, `Object.keys`, `JSON.stringify` and structural equality
 * see nothing and every construction path shares one hidden class.
 */
import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { copyInto } from "./copy-to.ts";
import { type ComparisonArgument, comparisonOf, elementEquals } from "./default-comparer.ts";
import type { IList, IReadOnlyList, Span } from "./interfaces.ts";
import {
  throwArgumentNull,
  throwCompareFailed,
  throwFixedSize,
  throwIndexAtMostCount,
  throwInsertOOR,
  throwInvalidRange,
  throwModified,
  throwNegative,
  throwOOR,
  throwReadOnly,
} from "./throw-helpers.ts";

export type { MetanoChecks } from "./checks.ts";

/**
 * The array the next construction adopts instead of copying (`of`, `adopt`). Read and
 * cleared first thing in the constructor, so every path goes through it (one hidden
 * class) and a user iterator run by a copy cannot observe it.
 */
let adopting: unknown[] | undefined;

let List$rt: CollectionType<List<unknown>> | undefined;

export class List<T> implements IList<T>, IReadOnlyList<T> {
  declare readonly _items: T[];
  declare _version: number;
  declare readonly $typeArguments$List: readonly RuntimeType[] | undefined;

  constructor(items?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null) {
    const adopted = adopting as T[] | undefined;

    adopting = undefined;

    const storage = adopted ?? (items === null || items === undefined ? [] : Array.from(items));

    Object.defineProperty(this, "_items", { value: storage });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "$typeArguments$List", { value: typeArguments ?? undefined });
  }

  /** A collection initializer or expression: the list takes `items` as its storage. */
  static of<T>(items: T[], typeArguments?: readonly RuntimeType[] | null): List<T> {
    adopting = items;

    return new List<T>(null, typeArguments);
  }

  /** `new List<T>(collection)`: a copy. */
  static from<T>(items: Iterable<T>, typeArguments?: readonly RuntimeType[] | null): List<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("collection");
    }

    return new List<T>(items, typeArguments);
  }

  /** `new List<T>(capacity)`: the capacity is a hint, evaluated and validated once. */
  static withCapacity<T>(capacity: number, typeArguments?: readonly RuntimeType[] | null): List<T> {
    if (capacity < 0) {
      throwNegative("capacity");
    }

    return new List<T>(null, typeArguments);
  }

  /**
   * A list over `array` itself (zero copy). `fixedSize` gives an array's `IList<T>`
   * behaviour: size changes throw `NotSupportedException`, writes go through.
   */
  static adopt<T>(
    array: T[],
    options?: { readonly fixedSize?: boolean } | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): List<T> {
    adopting = array;

    return options?.fixedSize === true
      ? new FixedSizeList<T>(null, typeArguments)
      : new List<T>(null, typeArguments);
  }

  /** The runtime type descriptor of the open `List<T>`: a data literal, built on first read. */
  static get $runtimeType(): CollectionType<List<unknown>> {
    List$rt ??= {
      kind: "collection",
      collection: "List",
      name: "List",
      fullName: "System.Collections.Generic.List",
      namespace: "System.Collections.Generic",
      ctor: List,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "IList", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
        { collection: "IReadOnlyList", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
      ],
      test: (value: unknown): value is List<unknown> => value instanceof List,
    };

    return List$rt;
  }

  get count(): number {
    return this._items.length;
  }

  get isReadOnly(): boolean {
    return false;
  }

  get isFixedSize(): boolean {
    return false;
  }

  get(index: number): T {
    const items = this._items;

    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      index >>> 0 >= items.length
    ) {
      throwOOR();
    }

    return items[index] as T;
  }

  set(index: number, value: T): void {
    const items = this._items;

    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      index >>> 0 >= items.length
    ) {
      throwOOR();
    }

    items[index] = value;

    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }

  /** `list[i] op= …` (closure form): reads, applies `f`, writes; returns the new value. */
  update(index: number, f: (current: T) => T): T {
    const next = f(this.get(index));

    this.set(index, next);

    return next;
  }

  /** `list[i]++` whose value is used: like {@link update}, returning the old value. */
  updateReturningOld(index: number, f: (current: T) => T): T {
    const old = this.get(index);

    this.set(index, f(old));

    return old;
  }

  /** `list[^n]`. */
  fromEnd(n: number): T {
    return this.get(this._items.length - n);
  }

  /** `list[start..end]`: a new list, with absolute indices (no negative JS semantics). */
  slice(start: number, end?: number): List<T> {
    return this.getRange(start, (end ?? this._items.length) - start);
  }

  getRange(index: number, count: number): List<T> {
    checkRange(this._items.length, index, count);

    return List.of(this._items.slice(index, index + count), this.$typeArguments$List);
  }

  add(item: T): void {
    this._items.push(item);

    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }

  /** Appends a snapshot of `items` (so `addRange(this)` doubles the list, as on .NET). */
  addRange(items: Iterable<T>): void {
    this.insertRange(this._items.length, items);
  }

  insert(index: number, item: T): void {
    const items = this._items;

    if (index >>> 0 > items.length) {
      throwInsertOOR();
    }

    items.splice(index, 0, item);
    this._bump();
  }

  insertRange(index: number, items: Iterable<T>): void {
    if (items === null || items === undefined) {
      throwArgumentNull("collection");
    }

    const storage = this._items;

    if (index >>> 0 > storage.length) {
      throwIndexAtMostCount();
    }

    const added = Array.from(items);

    if (added.length === 0) {
      return;
    }

    insertAll(storage, index, added);
    this._bump();
  }

  remove(item: T): boolean {
    const index = this.indexOf(item);

    if (index < 0) {
      return false;
    }

    this._items.splice(index, 1);
    this._bump();

    return true;
  }

  removeAt(index: number): void {
    const items = this._items;

    if (index >>> 0 >= items.length) {
      throwOOR();
    }

    items.splice(index, 1);
    this._bump();
  }

  removeRange(index: number, count: number): void {
    checkRange(this._items.length, index, count);

    if (count > 0) {
      this._items.splice(index, count);
      this._bump();
    }
  }

  removeAll(match: (item: T) => boolean): number {
    requireDelegate(match, "match");

    const items = this._items;
    let kept = 0;

    for (let read = 0; read < items.length; read++) {
      const item = items[read] as T;

      if (!match(item)) {
        items[kept++] = item;
      }
    }

    const removed = items.length - kept;

    if (removed > 0) {
      items.length = kept;
      this._bump();
    }

    return removed;
  }

  clear(): void {
    this._items.length = 0;
    this._bump();
  }

  contains(item: T): boolean {
    return this.indexOf(item) >= 0;
  }

  indexOf(item: T, index?: number): number {
    const items = this._items;
    const start = index ?? 0;

    if (start >>> 0 > items.length) {
      throwIndexAtMostCount();
    }

    if (isIdentityComparable(item)) {
      return items.indexOf(item, start);
    }

    for (let position = start; position < items.length; position++) {
      if (elementEquals(items[position], item)) {
        return position;
      }
    }

    return -1;
  }

  lastIndexOf(item: T): number {
    const items = this._items;

    if (isIdentityComparable(item)) {
      return items.lastIndexOf(item);
    }

    for (let position = items.length - 1; position >= 0; position--) {
      if (elementEquals(items[position], item)) {
        return position;
      }
    }

    return -1;
  }

  find(match: (item: T) => boolean): T | null;
  find<D>(match: (item: T) => boolean, defaultValue: D): T | D;
  find(match: (item: T) => boolean, defaultValue: unknown = null): unknown {
    const index = this.findIndex(match);

    return index < 0 ? defaultValue : this._items[index];
  }

  findIndex(match: (item: T) => boolean): number {
    requireDelegate(match, "match");

    return this._items.findIndex((item) => match(item));
  }

  findLast(match: (item: T) => boolean): T | null;
  findLast<D>(match: (item: T) => boolean, defaultValue: D): T | D;
  findLast(match: (item: T) => boolean, defaultValue: unknown = null): unknown {
    const index = this.findLastIndex(match);

    return index < 0 ? defaultValue : this._items[index];
  }

  findLastIndex(match: (item: T) => boolean): number {
    requireDelegate(match, "match");

    const items = this._items;

    for (let position = items.length - 1; position >= 0; position--) {
      if (match(items[position] as T)) {
        return position;
      }
    }

    return -1;
  }

  findAll(match: (item: T) => boolean): List<T> {
    requireDelegate(match, "match");

    return List.of(
      this._items.filter((item) => match(item)),
      this.$typeArguments$List,
    );
  }

  exists(match: (item: T) => boolean): boolean {
    return this.findIndex(match) >= 0;
  }

  trueForAll(match: (item: T) => boolean): boolean {
    requireDelegate(match, "match");

    return this._items.every((item) => match(item));
  }

  /** `ForEach`: a change made by `action` stops the walk with `InvalidOperationException`. */
  forEach(action: (item: T) => void): void {
    requireDelegate(action, "action");

    const items = this._items;
    const version =
      typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__ ? this._version : 0;

    for (let index = 0; index < items.length; index++) {
      if (
        (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
        this._version !== version
      ) {
        break;
      }

      action(items[index] as T);
    }

    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      this._version !== version
    ) {
      throwModified();
    }
  }

  convertAll<U>(converter: (item: T) => U): List<U> {
    requireDelegate(converter, "converter");

    return List.of(this._items.map((item) => converter(item)));
  }

  /** The index of `item` in the sorted list, or the bitwise complement of its insertion point. */
  binarySearch(item: T, comparison?: ComparisonArgument<T>): number {
    const compare = comparisonOf(comparison);
    const items = this._items;
    let low = 0;
    let high = items.length - 1;

    try {
      while (low <= high) {
        const middle = low + ((high - low) >> 1);
        const order = compare(items[middle] as T, item);

        if (order === 0) {
          return middle;
        }

        if (order < 0) {
          low = middle + 1;
        } else {
          high = middle - 1;
        }
      }
    } catch (error) {
      throwCompareFailed(error);
    }

    return ~low;
  }

  /** In place, stable; `Comparer<T>.Default` without an argument. */
  sort(comparison?: ComparisonArgument<T>): void {
    const items = this._items;

    if (items.length > 1) {
      sortInPlace(items, comparison);
    }

    this._bump();
  }

  reverse(): void {
    this._items.reverse();
    this._bump();
  }

  copyTo(array: T[], arrayIndex?: number): void {
    copyInto(this._items, array, arrayIndex ?? 0);
  }

  /** `ToArray()`: a detached shallow copy (FR-055). */
  toArray(): T[] {
    return this._items.slice();
  }

  /** The backing array itself (FR-056). */
  asJsArray(): T[] {
    return this._items;
  }

  /** The backing array itself, typed as a length-keeping view (FR-056). */
  asSpan(): Span<T> {
    return this._items;
  }

  /** The backing array itself, typed read-only (FR-056). */
  asReadOnlySpan(): ReadonlyArray<T> {
    return this._items;
  }

  /** Initializer elements after a capacity / copy constructor: add semantics, chainable. */
  initAdd(items: readonly T[]): this {
    for (const item of items) {
      this.add(item);
    }

    return this;
  }

  toJSON(): T[] {
    return this._items.slice();
  }

  toString(): string {
    return collectionTypeName("System.Collections.Generic.List`1", ["T"], this.$typeArguments$List);
  }

  [Symbol.iterator](): IterableIterator<T> {
    return new ListIterator(this);
  }

  /** Bumps the enumeration version (FR-058: removed with the checks). */
  protected _bump(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/**
 * A list adopted from an array the C# source converts to a list interface
 * (`List.adopt(arr, { fixedSize: true })`): it behaves as .NET's array behind
 * `IList<T>`. Size changes throw; writes go through without bumping the version,
 * because an array's enumerator detects no write. Not exported: `isFixedSize` and
 * `asArray` tell it apart.
 */
class FixedSizeList<T> extends List<T> {
  static override get $runtimeType(): CollectionType<List<unknown>> {
    return List.$runtimeType;
  }

  override get isReadOnly(): boolean {
    return true;
  }

  override get isFixedSize(): boolean {
    return true;
  }

  override set(index: number, value: T): void {
    const items = this._items;

    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      index >>> 0 >= items.length
    ) {
      throwOOR();
    }

    items[index] = value;
  }

  override add(_item: T): void {
    throwFixedSize();
  }

  override insert(_index: number, _item: T): void {
    throwFixedSize();
  }

  override insertRange(_index: number, _items: Iterable<T>): void {
    throwFixedSize();
  }

  override remove(_item: T): boolean {
    return throwFixedSize();
  }

  override removeAt(_index: number): void {
    throwFixedSize();
  }

  override removeRange(_index: number, _count: number): void {
    throwFixedSize();
  }

  override removeAll(_match: (item: T) => boolean): number {
    return throwFixedSize();
  }

  override clear(): void {
    throwReadOnly();
  }

  protected override _bump(): void {}
}

/**
 * `List<T>`'s enumerator: an iterator object (no generator, research R17) that checks
 * the version and the backing array's length on every step, the last one included.
 */
class ListIterator<T> implements IterableIterator<T> {
  private readonly _list: List<T>;
  private readonly _expectedVersion: number;
  private readonly _length: number;
  private _index = 0;

  constructor(list: List<T>) {
    this._list = list;
    this._expectedVersion =
      typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__ ? list._version : 0;
    this._length = list._items.length;
  }

  next(): IteratorResult<T> {
    const list = this._list;
    const items = list._items;

    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      (list._version !== this._expectedVersion || items.length !== this._length)
    ) {
      throwModified();
    }

    if (this._index >= this._length) {
      return { done: true, value: undefined };
    }

    return { done: false, value: items[this._index++] as T };
  }

  [Symbol.iterator](): IterableIterator<T> {
    return this;
  }
}

/** Primitives other than `NaN` compare by `===`, which `Array#indexOf` uses. */
function isIdentityComparable(value: unknown): boolean {
  return typeof value !== "object" && !Number.isNaN(value);
}

function requireDelegate(delegate: unknown, paramName: string): void {
  if (delegate === null || delegate === undefined) {
    throwArgumentNull(paramName);
  }
}

/** The `index` / `count` checks of `GetRange` / `RemoveRange`, in .NET's order. */
function checkRange(length: number, index: number, count: number): void {
  if (index < 0) {
    throwNegative("index");
  }

  if (count < 0) {
    throwNegative("count");
  }

  if (length - index < count) {
    throwInvalidRange();
  }
}

/** Inserts `added` at `index` without spreading (no argument-count limit). */
function insertAll<T>(storage: T[], index: number, added: readonly T[]): void {
  const tail = storage.splice(index);

  for (const item of added) {
    storage.push(item);
  }

  for (const item of tail) {
    storage.push(item);
  }
}

function sortInPlace<T>(items: T[], comparison: ComparisonArgument<T>): void {
  const compare = comparisonOf(comparison);

  try {
    items.sort(compare);
  } catch (error) {
    throwCompareFailed(error);
  }
}
