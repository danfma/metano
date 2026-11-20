import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { BuilderIterator } from "./builder-iterator.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { ImmutableList } from "./immutable-list.ts";
import { ImmutableListBuilderCore } from "./immutable-list-builder-core.ts";
import { throwArgumentOutOfRange } from "./throw-helpers.ts";

let ImmutableListBuilder$rt: CollectionType<ImmutableListBuilder<unknown>> | undefined;

/**
 * Mirrors `ImmutableList<T>.Builder` (spec 011 FR-054) over
 * `ImmutableListBuilderCore`; `ImmutableArrayBuilder` is its sibling (W4-66).
 * It keeps its own version, bumped by every mutating call as .NET 10 bumps it
 * (a no-op included, except `remove` of an absent element), so a running
 * enumeration throws .NET's builder text, and its indexer checks with .NET's
 * default `ArgumentOutOfRangeException` text, not `List`'s. `toImmutableList`
 * copies, so a builder never shares storage with an immutable version (W4-11).
 */
export class ImmutableListBuilder<T> extends ImmutableListBuilderCore<T> {
  declare _version: number;
  declare readonly $typeArguments$ImmutableListBuilder: readonly RuntimeType[] | undefined;

  /** `ToBuilder()` / `CreateBuilder<T>()`: a copy of `source`, whose type arguments it keeps when `source` is an `ImmutableList`. */
  constructor(source?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null) {
    super(source);

    const inherited =
      source instanceof ImmutableList ? source.$typeArguments$ImmutableList : undefined;

    Object.defineProperty(this, "_version", { value: 0, writable: true });

    Object.defineProperty(this, "$typeArguments$ImmutableListBuilder", {
      value: typeArguments ?? inherited,
    });
  }

  static get $runtimeType(): CollectionType<ImmutableListBuilder<unknown>> {
    ImmutableListBuilder$rt ??= {
      kind: "collection",
      collection: "ImmutableListBuilder",
      name: "ImmutableList.Builder",
      fullName: "System.Collections.Immutable.ImmutableList.Builder",
      namespace: "System.Collections.Immutable",
      ctor: ImmutableListBuilder,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "IList", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
        { collection: "IReadOnlyList", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
      ],
      test: (value: unknown): value is ImmutableListBuilder<unknown> =>
        value instanceof ImmutableListBuilder,
      closeFor: (instance) => closeCollectionFor(ImmutableListBuilder.$runtimeType, instance),
    };

    return ImmutableListBuilder$rt;
  }

  /** .NET's ``System.Collections.Immutable.ImmutableList`1+Builder[T]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableList`1+Builder",
      ["T"],
      this.$typeArguments$ImmutableListBuilder,
    );
  }

  [Symbol.iterator](): Iterator<T> {
    return new BuilderIterator(this, this._list[Symbol.iterator]());
  }

  protected checkItemIndex(index: number): void {
    if (index >>> 0 >= this.count) {
      throwArgumentOutOfRange("index");
    }
  }

  protected modified(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/** `builder.ToImmutable()`: a copy, never sharing the builder's storage. */
export function toImmutableList<T>(builder: ImmutableListBuilder<T>): ImmutableList<T> {
  return ImmutableList.of(builder._list.toArray(), builder.$typeArguments$ImmutableListBuilder);
}
