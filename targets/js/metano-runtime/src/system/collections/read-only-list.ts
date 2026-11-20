import { NullReferenceException } from "../exceptions/null-reference-exception.ts";
import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor, emptyCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { copyInto } from "./copy-to.ts";
import { elementEquals } from "./default-comparer.ts";
import type { IList, IReadOnlyList } from "./interfaces.ts";
import type { List } from "./list.ts";
import { throwArgumentNull, throwOOR, throwReadOnly } from "./throw-helpers.ts";

let ReadOnlyList$rt: CollectionType<ReadOnlyList<unknown>> | undefined;
let empty: ReadOnlyList<unknown> | undefined;

/**
 * Mirrors `System.Collections.ObjectModel.ReadOnlyCollection<T>` (spec 011
 * FR-054): its own class (`instanceof List` is `false`, as `is List<T>` in
 * .NET), a live read-only view over an `IList<T>` or a native array. Reads and
 * `count` follow the source, enumeration is the source's own (a `List` source
 * throws on modification, an array does not), and every mutator throws
 * `NotSupportedException` (`Collection is read-only.`).
 */
export class ReadOnlyList<T> implements IList<T>, IReadOnlyList<T> {
  declare readonly _source: IList<T> | readonly T[];
  declare readonly $typeArguments$ReadOnlyList: readonly RuntimeType[] | undefined;

  /** `new ReadOnlyCollection<T>(list)`: `ArgumentNullException` (`list`) for `null`. */
  constructor(source: IList<T> | readonly T[], typeArguments?: readonly RuntimeType[] | null) {
    if (source === null || source === undefined) {
      throwArgumentNull("list");
    }

    Object.defineProperty(this, "_source", { value: source });
    Object.defineProperty(this, "$typeArguments$ReadOnlyList", {
      value: typeArguments ?? undefined,
    });
  }

  /** `Array.AsReadOnly(array)`: `ArgumentNullException` (`array`) for `null`. */
  static fromArray<T>(
    array: readonly T[],
    typeArguments?: readonly RuntimeType[] | null,
  ): ReadOnlyList<T> {
    if (array === null || array === undefined) {
      throwArgumentNull("array");
    }

    return new ReadOnlyList(array, typeArguments);
  }

  /** `ReadOnlyCollection<T>.Empty`: one shared instance, built on first read. */
  static empty<T>(typeArguments?: readonly RuntimeType[] | null): ReadOnlyList<T> {
    if (typeArguments) {
      return emptyCollectionFor(
        ReadOnlyList.$runtimeType,
        typeArguments,
        () => new ReadOnlyList<T>([], typeArguments),
      );
    }

    empty ??= new ReadOnlyList<unknown>([]);

    // The one empty instance serves every `T`: it holds no element.
    return empty as ReadOnlyList<T>;
  }

  static get $runtimeType(): CollectionType<ReadOnlyList<unknown>> {
    ReadOnlyList$rt ??= {
      kind: "collection",
      collection: "ReadOnlyList",
      name: "ReadOnlyCollection",
      fullName: "System.Collections.ObjectModel.ReadOnlyCollection",
      namespace: "System.Collections.ObjectModel",
      ctor: ReadOnlyList,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "IList", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
        { collection: "IReadOnlyList", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
      ],
      test: (value: unknown): value is ReadOnlyList<unknown> => value instanceof ReadOnlyList,
      closeFor: (instance) => closeCollectionFor(ReadOnlyList.$runtimeType, instance),
    };

    return ReadOnlyList$rt;
  }

  get count(): number {
    const source = this._source;

    return isArraySource(source) ? source.length : source.count;
  }

  get isReadOnly(): boolean {
    return true;
  }

  /** The source's element; an array source is bounds-checked as .NET's array `IList<T>` indexer. */
  get(index: number): T {
    const source = this._source;

    if (!isArraySource(source)) {
      return source.get(index);
    }

    if (index >>> 0 >= source.length) {
      throwOOR();
    }

    // Checked just above: `noUncheckedIndexedAccess` cannot see it.
    return source[index] as T;
  }

  /** `list[^n]`. */
  fromEnd(n: number): T {
    return this.get(this.count - n);
  }

  indexOf(item: T): number {
    const source = this._source;

    if (!isArraySource(source)) {
      return source.indexOf(item);
    }

    return source.findIndex((candidate) => elementEquals(candidate, item));
  }

  contains(item: T): boolean {
    const source = this._source;

    return isArraySource(source) ? this.indexOf(item) >= 0 : source.contains(item);
  }

  copyTo(array: T[], arrayIndex = 0): void {
    const source = this._source;

    if (isArraySource(source)) {
      copyInto(source, array, arrayIndex);
    } else {
      source.copyTo(array, arrayIndex);
    }
  }

  set(_index: number, _value: T): never {
    return throwReadOnly();
  }

  update(_index: number, _f: (current: T) => T): never {
    return throwReadOnly();
  }

  updateReturningOld(_index: number, _f: (current: T) => T): never {
    return throwReadOnly();
  }

  add(_item: T): never {
    return throwReadOnly();
  }

  insert(_index: number, _item: T): never {
    return throwReadOnly();
  }

  remove(_item: T): never {
    return throwReadOnly();
  }

  removeAt(_index: number): never {
    return throwReadOnly();
  }

  clear(): never {
    return throwReadOnly();
  }

  toJSON(): T[] {
    return [...this];
  }

  /** .NET's ``System.Collections.ObjectModel.ReadOnlyCollection`1[T]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.ObjectModel.ReadOnlyCollection`1",
      ["T"],
      this.$typeArguments$ReadOnlyList,
    );
  }

  [Symbol.iterator](): Iterator<T> {
    return this._source[Symbol.iterator]();
  }
}

/** `list.AsReadOnly()`: a member call on `null` throws `NullReferenceException`, as .NET does. */
export function asReadOnly<T>(
  list: List<T>,
  typeArguments?: readonly RuntimeType[] | null,
): ReadOnlyList<T> {
  if (list === null || list === undefined) {
    throw new NullReferenceException();
  }

  return new ReadOnlyList(list, typeArguments);
}

function isArraySource<T>(source: IList<T> | readonly T[]): source is readonly T[] {
  return Array.isArray(source);
}
