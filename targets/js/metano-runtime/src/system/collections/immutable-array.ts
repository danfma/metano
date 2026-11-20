import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import {
  type ComparisonArgument,
  comparisonOf,
  type IEqualityComparer,
} from "./default-comparer.ts";
import { at, ImmutableListCore } from "./immutable-list-core.ts";

import {
  throwArgumentNull,
  throwArgumentOutOfRange,
  throwArrayTooSmall,
  throwCompareFailed,
  throwIndexOutsideArray,
  throwNegativeDestinationIndex,
} from "./throw-helpers.ts";

let ImmutableArray$rt: CollectionType<ImmutableArray<unknown>> | undefined;
let empty: ImmutableArray<unknown> | undefined;

/**
 * Mirrors `System.Collections.Immutable.ImmutableArray<T>` (spec 011 FR-054,
 * OD-17 as amended by W4-66): a sibling of `ImmutableList` over the same
 * `ImmutableListCore`, so `instanceof ImmutableList` is `false` on it, as
 * `is ImmutableList<T>` in .NET. It differs where .NET 10's `ImmutableArray<T>`
 * does (probed 2026-11-19): an `IndexOutOfRangeException` indexer, `Array.Copy`'s
 * `copyTo` checks, a wrapped `binarySearch` comparer failure, `sort()` and an
 * empty `addRange` returning the receiver, `indexOf`'s `startIndex` parameter and
 * its own `toString()` text.
 */
export class ImmutableArray<T> extends ImmutableListCore<T, ImmutableArray<T>> {
  declare readonly $typeArguments$ImmutableArray: readonly RuntimeType[] | undefined;

  /** Internal: the factories and mutators build every instance. */
  constructor(items: T[], count: number, typeArguments: readonly RuntimeType[] | undefined) {
    super(items, count);
    Object.defineProperty(this, "$typeArguments$ImmutableArray", { value: typeArguments });
  }

  /** `ImmutableArray<T>.Empty`, `ImmutableArray.Create<T>()`: one instance, built on first read. */
  static empty<T>(): ImmutableArray<T> {
    empty ??= new ImmutableArray<unknown>([], 0, undefined);

    // The one empty instance serves every `T`: it holds no element.
    return empty as ImmutableArray<T>;
  }

  /** `Create(params T[])`, collection expressions: takes ownership of the array (the compiler passes a fresh literal). */
  static of<T>(items: T[], typeArguments?: readonly RuntimeType[] | null): ImmutableArray<T> {
    return new ImmutableArray(items, items.length, typeArguments ?? undefined);
  }

  /** `CreateRange`, `ToImmutableArray()`, `Create(array)`: a copy, or `items` itself when it is an `ImmutableArray`. */
  static from<T>(
    items: Iterable<T> | null | undefined,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableArray<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    if (items instanceof ImmutableArray) {
      // `instanceof` cannot narrow the element type: an `ImmutableArray` passed as `Iterable<T>` holds `T`s.
      return items as ImmutableArray<T>;
    }

    const copy = [...items];

    return new ImmutableArray(copy, copy.length, typeArguments ?? undefined);
  }

  /**
   * `ImmutableArray.Create(items, start, length)`: a copy of the range, checked
   * as .NET 10 checks it (`items`, then `start`, then `length`).
   */
  static fromRange<T>(
    items: readonly T[] | null | undefined,
    start: number,
    length: number,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableArray<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    if (start < 0 || start > items.length) {
      throwArgumentOutOfRange("start");
    }

    if (length < 0 || start + length > items.length) {
      throwArgumentOutOfRange("length");
    }

    const copy = items.slice(start, start + length);

    return new ImmutableArray(copy, copy.length, typeArguments ?? undefined);
  }

  static get $runtimeType(): CollectionType<ImmutableArray<unknown>> {
    ImmutableArray$rt ??= {
      kind: "collection",
      collection: "ImmutableArray",
      name: "ImmutableArray",
      fullName: "System.Collections.Immutable.ImmutableArray",
      namespace: "System.Collections.Immutable",
      ctor: ImmutableArray,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "IImmutableList", arguments: [0] },
        { collection: "IReadOnlyList", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
        { collection: "IList", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
      ],
      test: (value: unknown): value is ImmutableArray<unknown> => value instanceof ImmutableArray,
      closeFor: (instance) => closeCollectionFor(ImmutableArray.$runtimeType, instance),
    };

    return ImmutableArray$rt;
  }

  /** The indexer: `IndexOutOfRangeException` (`Index was outside the bounds of the array.`), a negative index included. */
  get(index: number): T {
    if (index >>> 0 >= this._count) {
      throwIndexOutsideArray();
    }

    return at(this._items, index);
  }

  /** `IndexOf(item[, startIndex[, count[, comparer]]])`: .NET names the start `startIndex`. */
  indexOf(
    item: T,
    index = 0,
    count: number = this._count - index,
    comparer?: IEqualityComparer<T> | null,
  ): number {
    return this.search(item, index, count, comparer, "startIndex");
  }

  /** .NET's `Array.BinarySearch`: a comparer failure throws `InvalidOperationException` with the failure as inner. */
  binarySearch(item: T, comparison?: ComparisonArgument<T>): number {
    try {
      return this.searchSorted(item, comparison);
    } catch (error) {
      throwCompareFailed(error);
    }
  }

  /** The receiver for an empty source, as .NET's. */
  addRange(items: Iterable<T>): ImmutableArray<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    const copy = [...items];

    return copy.length === 0 ? this : this.append(copy);
  }

  clear(): ImmutableArray<T> {
    return ImmutableArray.empty();
  }

  /**
   * The receiver when already in order: .NET scans adjacent pairs first (a
   * comparer failure there surfaces unwrapped), then sorts a copy as `Array.Sort`
   * does (a failure there is wrapped in `InvalidOperationException`).
   */
  sort(comparison?: ComparisonArgument<T>): ImmutableArray<T> {
    if (this.isInOrder(comparison)) {
      return this;
    }

    const sorted = this.sortedCopy(comparison);

    return this.derive(sorted, sorted.length);
  }

  /** `Array.Copy`'s checks: `destinationArray`, then `destinationIndex`, then the length. */
  copyTo(array: T[], arrayIndex = 0): void {
    if (array === null || array === undefined) {
      throwArgumentNull("destinationArray");
    }

    if (arrayIndex < 0) {
      throwNegativeDestinationIndex(arrayIndex);
    }

    if (array.length - arrayIndex < this._count) {
      throwArrayTooSmall();
    }

    this.copyInto(array, arrayIndex);
  }

  /** .NET's ``System.Collections.Immutable.ImmutableArray`1[T]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableArray`1",
      ["T"],
      this.$typeArguments$ImmutableArray,
    );
  }

  protected derive(items: T[], count: number): ImmutableArray<T> {
    return new ImmutableArray(items, count, this.$typeArguments$ImmutableArray);
  }

  protected self(): ImmutableArray<T> {
    return this;
  }

  /** Whether no adjacent pair is out of order under `comparison`. */
  private isInOrder(comparison: ComparisonArgument<T> | undefined): boolean {
    const compare = comparisonOf(comparison);

    for (let index = 1; index < this._count; index++) {
      if (compare(at(this._items, index - 1), at(this._items, index)) > 0) {
        return false;
      }
    }

    return true;
  }
}
