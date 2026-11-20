import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { BuilderIterator } from "./builder-iterator.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { defaultEqualityComparerFor, type IEqualityComparer } from "./default-comparer.ts";
import { HashSet } from "./hash-set.ts";
import { ImmutableHashSet } from "./immutable-hash-set.ts";
import type { ISet } from "./interfaces.ts";

let ImmutableHashSetBuilder$rt: CollectionType<ImmutableHashSetBuilder<unknown>> | undefined;

/**
 * Mirrors `ImmutableHashSet<T>.Builder` (spec 011 FR-054): a small mutable
 * class over its own `HashSet` (`instanceof HashSet` is `false`). It keeps its
 * own version, bumped by every mutating call as .NET 10 bumps it (a no-op
 * included: `add` of a present element, `remove` of an absent one,
 * `unionWith([])`, `exceptWith` of absent elements), so a running enumeration
 * throws as .NET's builder does (a removal included, unlike `HashSet`). `toImmutableHashSet`
 * copies, so a builder never shares storage with an immutable version (W4-11).
 */
export class ImmutableHashSetBuilder<T> implements ISet<T> {
  declare readonly _set: HashSet<T>;
  declare _version: number;
  declare readonly $typeArguments$ImmutableHashSetBuilder: readonly RuntimeType[] | undefined;

  /**
   * `ToBuilder()` / `CreateBuilder<T>([comparer])`: a copy of `source`, whose
   * comparer and type arguments it keeps when `source` is an `ImmutableHashSet`
   * and no comparer is given.
   */
  constructor(
    source?: Iterable<T> | null,
    comparer?: IEqualityComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ) {
    // `instanceof` cannot narrow the type arguments: a source passed as this builder's type holds them.
    const immutable =
      source instanceof ImmutableHashSet ? (source as ImmutableHashSet<T>) : undefined;
    const ownComparer = comparer ?? immutable?._storage.comparer ?? null;

    Object.defineProperty(this, "_set", { value: new HashSet<T>(source ?? null, ownComparer) });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "$typeArguments$ImmutableHashSetBuilder", {
      value: typeArguments ?? immutable?.$typeArguments$ImmutableHashSet,
    });
  }

  static get $runtimeType(): CollectionType<ImmutableHashSetBuilder<unknown>> {
    ImmutableHashSetBuilder$rt ??= {
      kind: "collection",
      collection: "ImmutableHashSetBuilder",
      name: "ImmutableHashSet.Builder",
      fullName: "System.Collections.Immutable.ImmutableHashSet.Builder",
      namespace: "System.Collections.Immutable",
      ctor: ImmutableHashSetBuilder,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "ISet", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
      ],
      test: (value: unknown): value is ImmutableHashSetBuilder<unknown> =>
        value instanceof ImmutableHashSetBuilder,
      closeFor: (instance) => closeCollectionFor(ImmutableHashSetBuilder.$runtimeType, instance),
    };

    return ImmutableHashSetBuilder$rt;
  }

  get count(): number {
    return this._set.count;
  }

  get isReadOnly(): boolean {
    return false;
  }

  get keyComparer(): IEqualityComparer<T> {
    return this._set._store.comparer ?? defaultEqualityComparerFor<T>();
  }

  add(item: T): boolean {
    const added = this._set.add(item);

    this.modified();

    return added;
  }

  remove(item: T): boolean {
    const removed = this._set.remove(item);

    this.modified();

    return removed;
  }

  contains(item: T): boolean {
    return this._set.contains(item);
  }

  /** `TryGetValue`: the stored element equal to `equalValue`, or `equalValue` itself on a miss, as .NET's (the `default(T)` argument, D30, is not read). */
  tryGetValue(equalValue: T, _defaultValue?: unknown): readonly [boolean, T] {
    const store = this._set._store;

    // `findKey` after `has`: a hit, so never the miss `undefined`.
    return store.has(equalValue) ? [true, store.findKey(equalValue) as T] : [false, equalValue];
  }

  clear(): void {
    this._set.clear();
    this.modified();
  }

  unionWith(other: Iterable<T>): void {
    this._set.unionWith(other);
    this.modified();
  }

  intersectWith(other: Iterable<T>): void {
    this._set.intersectWith(other);
    this.modified();
  }

  exceptWith(other: Iterable<T>): void {
    this._set.exceptWith(other);
    this.modified();
  }

  symmetricExceptWith(other: Iterable<T>): void {
    this._set.symmetricExceptWith(other);
    this.modified();
  }

  isSubsetOf(other: Iterable<T>): boolean {
    return this._set.isSubsetOf(other);
  }

  isSupersetOf(other: Iterable<T>): boolean {
    return this._set.isSupersetOf(other);
  }

  isProperSubsetOf(other: Iterable<T>): boolean {
    return this._set.isProperSubsetOf(other);
  }

  isProperSupersetOf(other: Iterable<T>): boolean {
    return this._set.isProperSupersetOf(other);
  }

  overlaps(other: Iterable<T>): boolean {
    return this._set.overlaps(other);
  }

  setEquals(other: Iterable<T>): boolean {
    return this._set.setEquals(other);
  }

  copyTo(array: T[], arrayIndex = 0): void {
    this._set.copyTo(array, arrayIndex);
  }

  toJSON(): T[] {
    return this._set.toArray();
  }

  /** .NET's ``System.Collections.Immutable.ImmutableHashSet`1+Builder[T]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableHashSet`1+Builder",
      ["T"],
      this.$typeArguments$ImmutableHashSetBuilder,
    );
  }

  [Symbol.iterator](): Iterator<T> {
    return new BuilderIterator(this, this._set._store.keys());
  }

  /** Bumps the version on every mutating call, a no-op included (.NET 10). */
  private modified(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/** `builder.ToImmutable()`: a copy, never sharing the builder's storage. */
export function toImmutableHashSet<T>(builder: ImmutableHashSetBuilder<T>): ImmutableHashSet<T> {
  return ImmutableHashSet.from(
    builder._set.toArray(),
    builder._set._store.comparer,
    builder.$typeArguments$ImmutableHashSetBuilder,
  );
}
