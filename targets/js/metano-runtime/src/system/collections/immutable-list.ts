import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import {
  closeCollectionFor,
  emptyCollectionFor,
  sameCollectionTypeArguments,
} from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import type { ComparisonArgument, IEqualityComparer } from "./default-comparer.ts";
import { at, ImmutableListCore } from "./immutable-list-core.ts";
import {
  throwArgumentNull,
  throwArgumentOutOfRange,
  throwOutsideExpectedRange,
} from "./throw-helpers.ts";

let ImmutableList$rt: CollectionType<ImmutableList<unknown>> | undefined;
let empty: ImmutableList<unknown> | undefined;

/**
 * Mirrors `System.Collections.Immutable.ImmutableList<T>` (spec 011 FR-054)
 * over `ImmutableListCore` (copy-on-write with W4-11's tip-append sharing);
 * `ImmutableArray<T>` is its sibling over the same core (W4-66). Mutators
 * return the receiver where .NET does (probed on .NET 10): `remove` /
 * `removeItems` / `removeAll` that remove nothing, `removeRange(i, 0)`,
 * `reverse` of at most one element; `addRange` / `insertRange` of an empty
 * source, `setItem` of an equal value and `sort()` return a new list; `clear`
 * returns `empty()`.
 */
export class ImmutableList<T> extends ImmutableListCore<T, ImmutableList<T>> {
  declare readonly $typeArguments$ImmutableList: readonly RuntimeType[] | undefined;

  /** Internal: the factories and mutators build every instance (C# has no public constructor). */
  constructor(items: T[], count: number, typeArguments: readonly RuntimeType[] | undefined) {
    super(items, count);
    Object.defineProperty(this, "$typeArguments$ImmutableList", { value: typeArguments });
  }

  /** `ImmutableList<T>.Empty`, `Create()`: one instance, built on first read. */
  static empty<T>(typeArguments?: readonly RuntimeType[] | null): ImmutableList<T> {
    if (typeArguments) {
      return emptyCollectionFor(
        ImmutableList.$runtimeType,
        typeArguments,
        () => new ImmutableList<T>([], 0, typeArguments),
      );
    }

    empty ??= new ImmutableList<unknown>([], 0, undefined);

    // The one empty instance serves every `T`: it holds no element.
    return empty as ImmutableList<T>;
  }

  /** `Create(params T[])`, collection expressions: takes ownership of the array (the compiler passes a fresh literal). */
  static of<T>(items: T[], typeArguments?: readonly RuntimeType[] | null): ImmutableList<T> {
    return items.length === 0 && typeArguments != null
      ? ImmutableList.empty<T>(typeArguments)
      : new ImmutableList(items, items.length, typeArguments ?? undefined);
  }

  /** `CreateRange`, `ToImmutableList()`: a copy, or `items` itself when it is an `ImmutableList`. */
  static from<T>(
    items: Iterable<T> | null | undefined,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableList<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    if (
      items instanceof ImmutableList &&
      sameCollectionTypeArguments(items.$typeArguments$ImmutableList, typeArguments)
    ) {
      // `instanceof` cannot narrow the element type: an `ImmutableList` passed as `Iterable<T>` holds `T`s.
      return items as ImmutableList<T>;
    }

    const copy = [...items];

    return ImmutableList.of(copy, typeArguments);
  }

  static get $runtimeType(): CollectionType<ImmutableList<unknown>> {
    ImmutableList$rt ??= {
      kind: "collection",
      collection: "ImmutableList",
      name: "ImmutableList",
      fullName: "System.Collections.Immutable.ImmutableList",
      namespace: "System.Collections.Immutable",
      ctor: ImmutableList,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "IImmutableList", arguments: [0] },
        { collection: "IReadOnlyList", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
        { collection: "IList", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
      ],
      test: (value: unknown): value is ImmutableList<unknown> => value instanceof ImmutableList,
      closeFor: (instance) => closeCollectionFor(ImmutableList.$runtimeType, instance),
    };

    return ImmutableList$rt;
  }

  /** The indexer: `ArgumentOutOfRangeException` (`index`) with .NET's default text, unlike `List`'s. */
  get(index: number): T {
    if (index >>> 0 >= this._count) {
      throwArgumentOutOfRange("index");
    }

    return at(this._items, index);
  }

  indexOf(
    item: T,
    index = 0,
    count: number = this._count - index,
    comparer?: IEqualityComparer<T> | null,
  ): number {
    return this.search(item, index, count, comparer, "index");
  }

  getRange(index: number, count: number): ImmutableList<T> {
    this.checkRange(index, count);

    return this.range(index, count);
  }

  /** `LastIndexOf(item[, index, count[, comparer]])`, checked in .NET's order (see `ImmutableListBuilder.backwardRange`). */
  lastIndexOf(
    item: T,
    index?: number,
    count?: number,
    comparer?: IEqualityComparer<T> | null,
  ): number {
    if (count === undefined && this._count === 0) {
      return -1;
    }

    const start = index ?? this._count - 1;
    const length = count ?? start + 1;

    if (start < 0) {
      throwArgumentOutOfRange("index");
    }

    if (length < 0 || length > this._count) {
      throwArgumentOutOfRange("count");
    }

    if (start >= this._count) {
      return -1;
    }

    if (start - length + 1 < 0) {
      throwOutsideExpectedRange();
    }

    return this.searchBackward(item, start, length, comparer);
  }

  /** A new list even for an empty source, as .NET's. */
  insertRange(index: number, items: Iterable<T>): ImmutableList<T> {
    if (index < 0 || index > this._count) {
      throwArgumentOutOfRange("index");
    }

    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    return this.inserted(index, [...items]);
  }

  removeAt(index: number): ImmutableList<T> {
    if (index >>> 0 >= this._count) {
      throwArgumentOutOfRange("index");
    }

    return this.without(index, 1);
  }

  removeRange(index: number, count: number): ImmutableList<T> {
    this.checkRange(index, count);

    return this.without(index, count);
  }

  convertAll<U>(converter: (item: T) => U): ImmutableList<U> {
    if (typeof converter !== "function") {
      throwArgumentNull("converter");
    }

    const converted = this.visible().map((item) => converter(item));

    return new ImmutableList(converted, converted.length, undefined);
  }

  /** .NET's `Array.BinarySearch` order; a comparer failure surfaces unwrapped, as .NET's `ImmutableList` does. */
  binarySearch(item: T, comparison?: ComparisonArgument<T>): number {
    return this.searchSorted(item, comparison);
  }

  /** A new list even for an empty source, as .NET's. */
  addRange(items: Iterable<T>): ImmutableList<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    return this.append([...items]);
  }

  clear(): ImmutableList<T> {
    return ImmutableList.empty(this.$typeArguments$ImmutableList);
  }

  /** Stable, `Comparer<T>.Default` without an argument; a comparer failure throws `InvalidOperationException`, as .NET. */
  sort(comparison?: ComparisonArgument<T>): ImmutableList<T> {
    const sorted = this.sortedCopy(comparison);

    return this.derive(sorted, sorted.length);
  }

  /** `CopyTo`: a short array or a negative index throws `ArgumentOutOfRangeException` (`arrayIndex`), as .NET's. */
  copyTo(array: T[], arrayIndex = 0): void {
    if (array === null || array === undefined) {
      throwArgumentNull("array");
    }

    if (arrayIndex < 0 || array.length - arrayIndex < this._count) {
      throwArgumentOutOfRange("arrayIndex");
    }

    this.copyInto(array, arrayIndex);
  }

  /** .NET's ``System.Collections.Immutable.ImmutableList`1[T]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableList`1",
      ["T"],
      this.$typeArguments$ImmutableList,
    );
  }

  protected derive(items: T[], count: number): ImmutableList<T> {
    return count === 0
      ? this.clear()
      : new ImmutableList(items, count, this.$typeArguments$ImmutableList);
  }

  protected self(): ImmutableList<T> {
    return this;
  }
}
