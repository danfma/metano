/**
 * `System.Collections.Generic.HashSet<T>` (spec 011 FR-001, FR-006, D18, D30;
 * contracts/runtime-collections.md § `HashSet<T>`). Elements key through
 * `elementEquals` / `elementHashCode` (records by value, other objects by
 * reference) or a user `IEqualityComparer<T>`, over the shared `hash-core`, and
 * enumerate in insertion order.
 *
 * The 2.x members `size`, `has`, `delete`, `values()` and `forEach` stay until
 * the compiler stops emitting them (W4-6).
 */
import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { copyInto } from "./copy-to.ts";
import type { IEqualityComparer } from "./default-comparer.ts";
import { HashCore } from "./hash-core.ts";
import type { IReadOnlySet, ISet } from "./interfaces.ts";
import * as algebra from "./set-algebra.ts";
import { throwArgumentNull, throwModified, throwNegative } from "./throw-helpers.ts";

let HashSet$rt: CollectionType<HashSet<unknown>> | undefined;

export class HashSet<T> implements ISet<T>, IReadOnlySet<T> {
  declare readonly _store: HashCore<T, T>;
  declare _version: number;
  declare readonly $typeArguments$HashSet: readonly RuntimeType[] | undefined;

  constructor(
    items?: Iterable<T> | null,
    comparer?: IEqualityComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ) {
    Object.defineProperty(this, "_store", { value: new HashCore<T, T>(comparer) });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "$typeArguments$HashSet", { value: typeArguments ?? undefined });

    if (items !== null && items !== undefined) {
      for (const item of items) {
        this._store.set(item, item);
      }
    }
  }

  /** A collection initializer or expression; duplicates collapse. */
  static of<T>(
    items: readonly T[],
    comparer?: IEqualityComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): HashSet<T> {
    return new HashSet<T>(items, comparer, typeArguments);
  }

  /** `new HashSet<T>(collection[, comparer])`. */
  static from<T>(
    items: Iterable<T>,
    comparer?: IEqualityComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): HashSet<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("collection");
    }

    return new HashSet<T>(items, comparer, typeArguments);
  }

  /** `new HashSet<T>(capacity[, comparer])`: the capacity is evaluated and validated once. */
  static withCapacity<T>(
    capacity: number,
    comparer?: IEqualityComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): HashSet<T> {
    if (capacity < 0) {
      throwNegative("capacity");
    }

    return new HashSet<T>(null, comparer, typeArguments);
  }

  /** The runtime type descriptor of the open `HashSet<T>`: a data literal, built on first read. */
  static get $runtimeType(): CollectionType<HashSet<unknown>> {
    HashSet$rt ??= {
      kind: "collection",
      collection: "HashSet",
      name: "HashSet",
      fullName: "System.Collections.Generic.HashSet",
      namespace: "System.Collections.Generic",
      ctor: HashSet,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "ISet", arguments: [0] },
        { collection: "IReadOnlySet", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
      ],
      test: (value: unknown): value is HashSet<unknown> => value instanceof HashSet,
    };

    return HashSet$rt;
  }

  get count(): number {
    return this._store.size;
  }

  get isReadOnly(): boolean {
    return false;
  }

  /** `true` when `item` was added; only an addition invalidates a running enumeration. */
  add(item: T): boolean {
    const added = this._store.set(item, item);

    if (added) {
      this._bump();
    }

    return added;
  }

  remove(item: T): boolean {
    return this._store.delete(item);
  }

  contains(item: T): boolean {
    return this._store.has(item);
  }

  clear(): void {
    this._store.clear();
  }

  /** `TryGetValue(equalValue, out actualValue)`: the stored element equal to `equalValue`. */
  tryGetValue<D>(
    equalValue: T,
    missingValue: D,
  ): readonly [found: true, value: T] | readonly [found: false, value: D] {
    return this._store.has(equalValue)
      ? [true, this._store.findKey(equalValue) as T]
      : [false, missingValue];
  }

  removeWhere(match: (item: T) => boolean): number {
    if (match === null || match === undefined) {
      throwArgumentNull("match");
    }

    let removed = 0;

    for (const item of Array.from(this._store.keys())) {
      if (match(item) && this._store.delete(item)) {
        removed++;
      }
    }

    return removed;
  }

  unionWith(other: Iterable<T>): void {
    for (const item of Array.from(requireOther(other))) {
      this.add(item);
    }
  }

  intersectWith(other: Iterable<T>): void {
    const kept = this._distinct(requireOther(other));

    for (const item of Array.from(this._store.keys())) {
      if (!kept.contains(item)) {
        this._store.delete(item);
      }
    }
  }

  exceptWith(other: Iterable<T>): void {
    for (const item of Array.from(requireOther(other))) {
      this._store.delete(item);
    }
  }

  symmetricExceptWith(other: Iterable<T>): void {
    if (requireOther(other) === this) {
      this.clear();

      return;
    }

    for (const item of this._distinct(other)) {
      if (!this._store.delete(item)) {
        this.add(item);
      }
    }
  }

  isSubsetOf(other: Iterable<T>): boolean {
    return algebra.isSubsetOf(this, other, (items) => this._distinct(items));
  }

  isSupersetOf(other: Iterable<T>): boolean {
    return algebra.isSupersetOf(this, other);
  }

  isProperSubsetOf(other: Iterable<T>): boolean {
    return algebra.isProperSubsetOf(this, other, (items) => this._distinct(items));
  }

  isProperSupersetOf(other: Iterable<T>): boolean {
    return algebra.isProperSupersetOf(this, other, (items) => this._distinct(items));
  }

  overlaps(other: Iterable<T>): boolean {
    return algebra.overlaps(this, other);
  }

  setEquals(other: Iterable<T>): boolean {
    return algebra.setEquals(this, other, (items) => this._distinct(items));
  }

  copyTo(array: T[], arrayIndex?: number): void {
    copyInto(this.toArray(), array, arrayIndex ?? 0);
  }

  /** A detached copy, in enumeration order (FR-055). */
  toArray(): T[] {
    return Array.from(this._store.keys());
  }

  /** Initializer elements after a capacity / copy / comparer constructor, chainable. */
  initAdd(items: readonly T[]): this {
    for (const item of items) {
      this.add(item);
    }

    return this;
  }

  toJSON(): T[] {
    return this.toArray();
  }

  toString(): string {
    return collectionTypeName(
      "System.Collections.Generic.HashSet`1",
      ["T"],
      this.$typeArguments$HashSet,
    );
  }

  [Symbol.iterator](): IterableIterator<T> {
    return new HashSetIterator(this);
  }

  /** @deprecated 2.x name of {@link count}; kept until the compiler stops emitting it. */
  get size(): number {
    return this.count;
  }

  /** @deprecated 2.x name of {@link contains}. */
  has(item: T): boolean {
    return this.contains(item);
  }

  /** @deprecated 2.x name of {@link remove}. */
  delete(item: T): boolean {
    return this.remove(item);
  }

  /** @deprecated 2.x iterator; iterate the set itself. */
  values(): IterableIterator<T> {
    return this[Symbol.iterator]();
  }

  /** @deprecated 2.x callback walk; use `for..of`. */
  forEach(callback: (item: T) => void): void {
    for (const item of this) {
      callback(item);
    }
  }

  /** `items` as a set with this set's comparer, deduplicated by its rule. */
  private _distinct(items: Iterable<T>): HashSet<T> {
    return items instanceof HashSet && items._store.comparer === this._store.comparer
      ? items
      : new HashSet<T>(items, this._store.comparer);
  }

  private _bump(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/** `HashSet<T>`'s enumerator: insertion order; an addition makes the next step throw. */
class HashSetIterator<T> implements IterableIterator<T> {
  private readonly _set: HashSet<T>;
  private readonly _expectedVersion: number;
  private readonly _keys: Iterator<T>;

  constructor(set: HashSet<T>) {
    this._set = set;
    this._expectedVersion =
      typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__ ? set._version : 0;
    this._keys = set._store.keys();
  }

  next(): IteratorResult<T> {
    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      this._set._version !== this._expectedVersion
    ) {
      throwModified();
    }

    return this._keys.next();
  }

  [Symbol.iterator](): IterableIterator<T> {
    return this;
  }
}

function requireOther<T>(other: Iterable<T>): Iterable<T> {
  if (other === null || other === undefined) {
    throwArgumentNull("other");
  }

  return other;
}
