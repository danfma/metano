import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { ImmutableArray } from "./immutable-array.ts";
import { ImmutableListBuilderCore } from "./immutable-list-builder-core.ts";
import { throwArgumentOutOfRange, throwIndexOutsideArray } from "./throw-helpers.ts";

let ImmutableArrayBuilder$rt: CollectionType<ImmutableArrayBuilder<unknown>> | undefined;

/**
 * Mirrors `ImmutableArray<T>.Builder` (spec 011 FR-054) over
 * `ImmutableListBuilderCore`, a sibling of `ImmutableListBuilder` (W4-66). As
 * .NET 10's, it has no version: its enumerator reads `get(i)` while
 * `i < count`, so a loop that writes or adds never throws; its indexer throws
 * `IndexOutOfRangeException`, while `removeAt` / `insert` keep the
 * `ArgumentOutOfRangeException`. `toImmutableArray` copies (W4-11).
 */
export class ImmutableArrayBuilder<T> extends ImmutableListBuilderCore<T> {
  declare readonly $typeArguments$ImmutableArrayBuilder: readonly RuntimeType[] | undefined;

  /** `ToBuilder()` / `CreateBuilder<T>()`: a copy of `source`, whose type arguments it keeps when `source` is an `ImmutableArray`. */
  constructor(source?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null) {
    super(source);

    const inherited =
      source instanceof ImmutableArray ? source.$typeArguments$ImmutableArray : undefined;

    Object.defineProperty(this, "$typeArguments$ImmutableArrayBuilder", {
      value: typeArguments ?? inherited,
    });
  }

  static get $runtimeType(): CollectionType<ImmutableArrayBuilder<unknown>> {
    ImmutableArrayBuilder$rt ??= {
      kind: "collection",
      collection: "ImmutableArrayBuilder",
      name: "ImmutableArray.Builder",
      fullName: "System.Collections.Immutable.ImmutableArray.Builder",
      namespace: "System.Collections.Immutable",
      ctor: ImmutableArrayBuilder,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "IList", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
        { collection: "IReadOnlyList", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
      ],
      test: (value: unknown): value is ImmutableArrayBuilder<unknown> =>
        value instanceof ImmutableArrayBuilder,
      closeFor: (instance) => closeCollectionFor(ImmutableArrayBuilder.$runtimeType, instance),
    };

    return ImmutableArrayBuilder$rt;
  }

  /** .NET's ``System.Collections.Immutable.ImmutableArray`1+Builder[T]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableArray`1+Builder",
      ["T"],
      this.$typeArguments$ImmutableArrayBuilder,
    );
  }

  [Symbol.iterator](): Iterator<T> {
    return new ImmutableArrayBuilderIterator(this);
  }

  /** The indexer: `IndexOutOfRangeException` (`Index was outside the bounds of the array.`). */
  protected checkItemIndex(index: number): void {
    if (index >>> 0 >= this.count) {
      throwIndexOutsideArray();
    }
  }

  /**
   * `ImmutableArray<T>.Builder.IndexOf`: `(0, 0)` finds nothing; otherwise `startIndex`
   * must be inside the builder, then `count` inside what remains.
   */
  protected forwardRange(
    startIndex = 0,
    count: number = this.count - startIndex,
  ): readonly [number, number] {
    if (count === 0 && startIndex === 0) {
      return [0, 0];
    }

    if (startIndex < 0 || startIndex >= this.count) {
      throwArgumentOutOfRange("startIndex");
    }

    if (count < 0 || startIndex + count > this.count) {
      throwArgumentOutOfRange("count");
    }

    return [startIndex, count];
  }

  /**
   * `ImmutableArray<T>.Builder.LastIndexOf`: `(0, 0)`, and an empty builder without a
   * count from start 0, find nothing; otherwise `startIndex` must be inside the builder
   * and `count` must not run past its start.
   */
  protected backwardRange(startIndex?: number, count?: number): readonly [number, number] | null {
    if (this.count === 0 && count === undefined && (startIndex === undefined || startIndex === 0)) {
      return null;
    }

    const start = startIndex ?? this.count - 1;
    const length = count ?? start + 1;

    if (start === 0 && length === 0) {
      return null;
    }

    if (start < 0 || start >= this.count) {
      throwArgumentOutOfRange("startIndex");
    }

    if (length < 0 || start - length + 1 < 0) {
      throwArgumentOutOfRange("count");
    }

    return [start, length];
  }

  /** Nothing to record: .NET's array builder has no version. */
  protected modified(): void {}
}

/** `builder.ToImmutable()`: a copy, never sharing the builder's storage. */
export function toImmutableArray<T>(
  builder: ImmutableArrayBuilder<T>,
  typeArguments?: readonly RuntimeType[] | null,
): ImmutableArray<T> {
  return ImmutableArray.of(
    builder._list.toArray(),
    typeArguments ?? builder.$typeArguments$ImmutableArrayBuilder,
  );
}

/** .NET's array-builder enumerator: an index walk re-reading `count` on every step, never checked. */
class ImmutableArrayBuilderIterator<T> implements Iterator<T> {
  private readonly builder: ImmutableArrayBuilder<T>;
  private index: number;

  constructor(builder: ImmutableArrayBuilder<T>) {
    this.builder = builder;
    this.index = 0;
  }

  next(): IteratorResult<T> {
    if (this.index >= this.builder.count) {
      return { done: true, value: undefined };
    }

    return { done: false, value: this.builder.get(this.index++) };
  }
}
