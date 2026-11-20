import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor, emptyCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { defaultEqualityComparerFor, type IEqualityComparer } from "./default-comparer.ts";
import type { IImmutableSet } from "./immutable-interfaces.ts";
import { ImmutableHashStorage, normalizedComparer } from "./immutable-storage.ts";
import type { IReadOnlySet } from "./interfaces.ts";
import * as setAlgebra from "./set-algebra.ts";
import { throwArgumentNull } from "./throw-helpers.ts";

let ImmutableHashSet$rt: CollectionType<ImmutableHashSet<unknown>> | undefined;
let empty: ImmutableHashSet<unknown> | undefined;

/**
 * Mirrors `System.Collections.Immutable.ImmutableHashSet<T>` (spec 011
 * FR-054): copy-on-write over `immutable-storage.ts`'s append-only keyed store
 * with W4-11's tip-append sharing (an `add` of a new element on the newest
 * version appends in place; every other change copies). Keys by
 * `elementEquals` / `elementHashCode` or the comparer (D18). Enumeration is the
 * version's insertion order (.NET's is its hash order; both are unspecified).
 * Calls that change nothing return the receiver where .NET does (probed on
 * .NET 10): `add` of a present element, `remove` of an absent one, `union` /
 * `except` that change nothing, `withComparer` of the same comparer.
 */
export class ImmutableHashSet<T> implements IImmutableSet<T>, IReadOnlySet<T> {
  declare readonly _storage: ImmutableHashStorage<T, true>;
  declare readonly _count: number;
  declare readonly $typeArguments$ImmutableHashSet: readonly RuntimeType[] | undefined;

  /** Internal: the factories and mutators build every instance (C# has no public constructor). */
  constructor(
    storage: ImmutableHashStorage<T, true>,
    count: number,
    typeArguments: readonly RuntimeType[] | undefined,
  ) {
    Object.defineProperty(this, "_storage", { value: storage });
    Object.defineProperty(this, "_count", { value: count });
    Object.defineProperty(this, "$typeArguments$ImmutableHashSet", { value: typeArguments });
  }

  /** `ImmutableHashSet<T>.Empty`: one instance (default comparer), built on first read. */
  static empty<T>(typeArguments?: readonly RuntimeType[] | null): ImmutableHashSet<T> {
    if (typeArguments) {
      return emptyCollectionFor(
        ImmutableHashSet.$runtimeType,
        typeArguments,
        () => new ImmutableHashSet<T>(new ImmutableHashStorage(null), 0, typeArguments),
      );
    }

    empty ??= new ImmutableHashSet<unknown>(new ImmutableHashStorage(null), 0, undefined);

    // The one empty instance serves every `T`: it holds no element.
    return empty as ImmutableHashSet<T>;
  }

  /** `Create([comparer, ]params T[])`: duplicates collapse. */
  static of<T>(
    items: readonly T[],
    comparer?: IEqualityComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableHashSet<T> {
    return ImmutableHashSet.build(items, normalizedComparer(comparer), typeArguments ?? undefined);
  }

  /** `CreateRange`, `ToImmutableHashSet()`: a copy, or `items` itself when it is an `ImmutableHashSet` with that comparer. */
  static from<T>(
    items: Iterable<T> | null | undefined,
    comparer?: IEqualityComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableHashSet<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    if (
      items instanceof ImmutableHashSet &&
      items._storage.comparer === normalizedComparer(comparer)
    ) {
      // `instanceof` cannot narrow the element type: an `ImmutableHashSet` passed as `Iterable<T>` holds `T`s.
      return items as ImmutableHashSet<T>;
    }

    return ImmutableHashSet.build(items, normalizedComparer(comparer), typeArguments ?? undefined);
  }

  static get $runtimeType(): CollectionType<ImmutableHashSet<unknown>> {
    ImmutableHashSet$rt ??= {
      kind: "collection",
      collection: "ImmutableHashSet",
      name: "ImmutableHashSet",
      fullName: "System.Collections.Immutable.ImmutableHashSet",
      namespace: "System.Collections.Immutable",
      ctor: ImmutableHashSet,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "IImmutableSet", arguments: [0] },
        { collection: "IReadOnlySet", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
        { collection: "ISet", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
      ],
      test: (value: unknown): value is ImmutableHashSet<unknown> =>
        value instanceof ImmutableHashSet,
      closeFor: (instance) => closeCollectionFor(ImmutableHashSet.$runtimeType, instance),
    };

    return ImmutableHashSet$rt;
  }

  private static build<T>(
    items: Iterable<T>,
    comparer: IEqualityComparer<T> | null,
    typeArguments: readonly RuntimeType[] | undefined,
  ): ImmutableHashSet<T> {
    const storage = new ImmutableHashStorage<T, true>(comparer);
    let count = 0;

    for (const item of items) {
      if (storage.find(item, count) < 0) {
        storage.append(count++, item, true);
      }
    }

    return count === 0 && comparer === null
      ? ImmutableHashSet.empty<T>(typeArguments)
      : new ImmutableHashSet(storage, count, typeArguments);
  }

  get count(): number {
    return this._count;
  }

  get isEmpty(): boolean {
    return this._count === 0;
  }

  /** The argument comparer, or `defaultEqualityComparer` (`EqualityComparer<T>.Default`). */
  get keyComparer(): IEqualityComparer<T> {
    return this._storage.comparer ?? defaultEqualityComparerFor<T>();
  }

  contains(item: T): boolean {
    return this._storage.find(item, this._count) >= 0;
  }

  /**
   * `TryGetValue`: the stored element equal to `equalValue`, or `equalValue` itself on a
   * miss, as .NET assigns `actualValue = equalValue`. The compiler's `default(T)` argument
   * (D30) is not read.
   */
  tryGetValue(equalValue: T, _defaultValue?: unknown): readonly [boolean, T] {
    const position = this._storage.find(equalValue, this._count);

    return position < 0 ? [false, equalValue] : [true, this._storage.entryAt(position)[0]];
  }

  add(item: T): ImmutableHashSet<T> {
    if (this.contains(item)) {
      return this;
    }

    return new ImmutableHashSet(
      this._storage.append(this._count, item, true),
      this._count + 1,
      this.$typeArguments$ImmutableHashSet,
    );
  }

  remove(item: T): ImmutableHashSet<T> {
    const position = this._storage.find(item, this._count);

    if (position < 0) {
      return this;
    }

    const items = this.toArray();

    items.splice(position, 1);

    return this.copyOf(items);
  }

  /** `Clear`: the receiver when empty, else the empty set with this set's comparer. */
  clear(): ImmutableHashSet<T> {
    if (this._count === 0) {
      return this;
    }

    return this._storage.comparer === null
      ? ImmutableHashSet.empty(this.$typeArguments$ImmutableHashSet)
      : ImmutableHashSet.build([], this._storage.comparer, this.$typeArguments$ImmutableHashSet);
  }

  union(other: Iterable<T>): ImmutableHashSet<T> {
    let result: ImmutableHashSet<T> = this;

    for (const item of argumentOther(other)) {
      result = result.add(item);
    }

    return result;
  }

  /** A new set unless this one is empty (.NET builds the intersection even when it keeps everything). */
  intersect(other: Iterable<T>): ImmutableHashSet<T> {
    const others = this.distinct(argumentOther(other));

    return this.copyKeeping((item) => others.contains(item));
  }

  except(other: Iterable<T>): ImmutableHashSet<T> {
    let result: ImmutableHashSet<T> = this;

    for (const item of argumentOther(other)) {
      result = result.remove(item);
    }

    return result;
  }

  /** Always a new set, as .NET's (probed: `SymmetricExcept([])` is not the receiver). */
  symmetricExcept(other: Iterable<T>): ImmutableHashSet<T> {
    const others = this.distinct(argumentOther(other));
    const kept = this.copyKeeping((item) => !others.contains(item));
    let result = kept;

    for (const item of others) {
      if (!this.contains(item)) {
        result = result.add(item);
      }
    }

    return result;
  }

  /** The same elements under `comparer` (`null`: the default); the receiver when the comparer is the same. */
  withComparer(comparer: IEqualityComparer<T> | null): ImmutableHashSet<T> {
    const target = normalizedComparer(comparer);

    if (target === this._storage.comparer) {
      return this;
    }

    return ImmutableHashSet.build(this, target, this.$typeArguments$ImmutableHashSet);
  }

  isSubsetOf(other: Iterable<T>): boolean {
    return setAlgebra.isSubsetOf(this, other, (items) => this.distinct(items));
  }

  isSupersetOf(other: Iterable<T>): boolean {
    return setAlgebra.isSupersetOf(this, other);
  }

  isProperSubsetOf(other: Iterable<T>): boolean {
    return setAlgebra.isProperSubsetOf(this, other, (items) => this.distinct(items));
  }

  isProperSupersetOf(other: Iterable<T>): boolean {
    return setAlgebra.isProperSupersetOf(this, other, (items) => this.distinct(items));
  }

  overlaps(other: Iterable<T>): boolean {
    return setAlgebra.overlaps(this, other);
  }

  setEquals(other: Iterable<T>): boolean {
    return setAlgebra.setEquals(this, other, (items) => this.distinct(items));
  }

  /** A detached copy in enumeration order. */
  toArray(): T[] {
    return this._storage.visible(this._count).map(([item]) => item);
  }

  toJSON(): T[] {
    return this.toArray();
  }

  /** .NET's ``System.Collections.Immutable.ImmutableHashSet`1[T]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableHashSet`1",
      ["T"],
      this.$typeArguments$ImmutableHashSet,
    );
  }

  [Symbol.iterator](): Iterator<T> {
    return new ImmutableHashSetIterator(this._storage.entries, this._count);
  }

  /** Always a copy of the elements `keep` accepts (except for an empty set). */
  private copyKeeping(keep: (item: T) => boolean): ImmutableHashSet<T> {
    return this._count === 0 ? this : this.copyOf(this.toArray().filter(keep));
  }

  private copyOf(items: readonly T[]): ImmutableHashSet<T> {
    return new ImmutableHashSet(
      ImmutableHashStorage.of(
        this._storage.comparer,
        items.map((item) => [item, true] as const),
      ),
      items.length,
      this.$typeArguments$ImmutableHashSet,
    );
  }

  /** `other` as a set with this set's comparer, so it is deduplicated by this set's rule. */
  private distinct(items: Iterable<T>): ImmutableHashSet<T> {
    return ImmutableHashSet.from(items, this._storage.comparer);
  }
}

/** An index walk over a version's first `count` entries (nothing can change them). */
class ImmutableHashSetIterator<T> implements Iterator<T> {
  private readonly _entries: ReadonlyArray<readonly [T, true]>;
  private readonly _count: number;
  private _index: number;

  constructor(entries: ReadonlyArray<readonly [T, true]>, count: number) {
    this._entries = entries;
    this._count = count;
    this._index = 0;
  }

  next(): IteratorResult<T> {
    if (this._index >= this._count) {
      return { done: true, value: undefined };
    }

    return { done: false, value: entryOf(this._entries, this._index++)[0] };
  }
}

function argumentOther<T>(other: Iterable<T> | null | undefined): Iterable<T> {
  if (other === null || other === undefined) {
    throwArgumentNull("other");
  }

  return other;
}

/** The entry at a position below the version's count: `noUncheckedIndexedAccess` cannot see the bound. */
function entryOf<T>(entries: ReadonlyArray<readonly [T, true]>, index: number): readonly [T, true] {
  return entries[index] as readonly [T, true];
}
