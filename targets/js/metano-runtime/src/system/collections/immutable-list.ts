import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import {
  type ComparisonArgument,
  comparisonOf,
  elementEquals,
  type IEqualityComparer,
} from "./default-comparer.ts";
import type { IImmutableList } from "./immutable-interfaces.ts";
import { ownsTip } from "./immutable-storage.ts";
import type { IReadOnlyList } from "./interfaces.ts";
import { binarySearch } from "./sorted-array.ts";
import {
  throwArgumentNull,
  throwArgumentOutOfRange,
  throwCompareFailed,
  throwOldValueNotFound,
} from "./throw-helpers.ts";

let ImmutableList$rt: CollectionType<ImmutableList<unknown>> | undefined;
let empty: ImmutableList<unknown> | undefined;

/**
 * Mirrors `System.Collections.Immutable.ImmutableList<T>` and, as the same
 * class, `ImmutableArray<T>` (spec 011 FR-054, OD-17): copy-on-write over one
 * array with W4-11's tip-append sharing (`immutable-storage.ts`): `add` /
 * `addRange` on the newest version of an array append in place (amortized
 * O(1)); every other mutator copies the version's elements (O(n)). A version
 * reads only its first `_count` elements, so it never sees what a later
 * version appended. Mutators return the receiver where .NET does (probed on
 * .NET 10): `remove` / `removeItems` / `removeAll` that remove nothing,
 * `removeRange(i, 0)`, `reverse` of at most one element; `clear` returns `empty()`.
 */
export class ImmutableList<T> implements IImmutableList<T>, IReadOnlyList<T> {
  declare readonly _items: T[];
  declare readonly _count: number;
  declare readonly $typeArguments$ImmutableList: readonly RuntimeType[] | undefined;

  /** Internal: the factories and mutators build every instance (C# has no public constructor). */
  constructor(items: T[], count: number, typeArguments: readonly RuntimeType[] | undefined) {
    Object.defineProperty(this, "_items", { value: items });
    Object.defineProperty(this, "_count", { value: count });
    Object.defineProperty(this, "$typeArguments$ImmutableList", { value: typeArguments });
  }

  /** `ImmutableList<T>.Empty`, `ImmutableArray<T>.Empty`, `Create()`: one instance, built on first read. */
  static empty<T>(): ImmutableList<T> {
    empty ??= new ImmutableList<unknown>([], 0, undefined);

    // The one empty instance serves every `T`: it holds no element.
    return empty as ImmutableList<T>;
  }

  /** `Create(params T[])`, collection expressions: takes ownership of the array (the compiler passes a fresh literal). */
  static of<T>(items: T[], typeArguments?: readonly RuntimeType[] | null): ImmutableList<T> {
    return new ImmutableList(items, items.length, typeArguments ?? undefined);
  }

  /** `CreateRange`, `ToImmutableList()`, `ToImmutableArray()`: a copy, or `items` itself when it is an `ImmutableList`. */
  static from<T>(
    items: Iterable<T> | null | undefined,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableList<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    if (items instanceof ImmutableList) {
      // `instanceof` cannot narrow the element type: an `ImmutableList` passed as `Iterable<T>` holds `T`s.
      return items as ImmutableList<T>;
    }

    const copy = [...items];

    return new ImmutableList(copy, copy.length, typeArguments ?? undefined);
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
      ],
      test: (value: unknown): value is ImmutableList<unknown> => value instanceof ImmutableList,
      closeFor: (instance) => closeCollectionFor(ImmutableList.$runtimeType, instance),
    };

    return ImmutableList$rt;
  }

  get count(): number {
    return this._count;
  }

  get isEmpty(): boolean {
    return this._count === 0;
  }

  /** The indexer: `ArgumentOutOfRangeException` (`index`) with .NET's default text, unlike `List`'s. */
  get(index: number): T {
    if (index >>> 0 >= this._count) {
      throwArgumentOutOfRange("index");
    }

    return at(this._items, index);
  }

  /** `list[^n]`. */
  fromEnd(n: number): T {
    return this.get(this._count - n);
  }

  /** `array[a..b]` on an `ImmutableArray<T>`. */
  slice(start: number, end: number = this._count): ImmutableList<T> {
    return this.getRange(start, end - start);
  }

  getRange(index: number, count: number): ImmutableList<T> {
    this.checkRange(index, count);

    return this.derive(this._items.slice(index, index + count));
  }

  indexOf(
    item: T,
    index = 0,
    count: number = this._count - index,
    comparer?: IEqualityComparer<T> | null,
  ): number {
    this.checkRange(index, count);

    const equals = equalityOf(comparer);

    for (let position = index; position < index + count; position++) {
      if (equals(at(this._items, position), item)) {
        return position;
      }
    }

    return -1;
  }

  lastIndexOf(
    item: T,
    index: number = this._count - 1,
    count: number = index + 1,
    comparer?: IEqualityComparer<T> | null,
  ): number {
    if (this._count === 0) {
      return -1;
    }

    this.checkRange(index - count + 1, count);

    const equals = equalityOf(comparer);

    for (let position = index; position > index - count; position--) {
      if (equals(at(this._items, position), item)) {
        return position;
      }
    }

    return -1;
  }

  contains(item: T): boolean {
    return this.indexOf(item) >= 0;
  }

  /** `Find`: the first match, or the caller's `default(T)` (D30). */
  find(match: (item: T) => boolean, defaultValue: T): T {
    const index = this.findIndex(match);

    return index < 0 ? defaultValue : at(this._items, index);
  }

  findIndex(match: (item: T) => boolean): number {
    checkMatch(match);

    for (let index = 0; index < this._count; index++) {
      if (match(at(this._items, index))) {
        return index;
      }
    }

    return -1;
  }

  /** `FindLast`: the last match, or the caller's `default(T)` (D30). */
  findLast(match: (item: T) => boolean, defaultValue: T): T {
    const index = this.findLastIndex(match);

    return index < 0 ? defaultValue : at(this._items, index);
  }

  findLastIndex(match: (item: T) => boolean): number {
    checkMatch(match);

    for (let index = this._count - 1; index >= 0; index--) {
      if (match(at(this._items, index))) {
        return index;
      }
    }

    return -1;
  }

  findAll(match: (item: T) => boolean): ImmutableList<T> {
    checkMatch(match);

    return this.derive(this.visible().filter((item) => match(item)));
  }

  exists(match: (item: T) => boolean): boolean {
    return this.findIndex(match) >= 0;
  }

  trueForAll(match: (item: T) => boolean): boolean {
    checkMatch(match);

    return this.visible().every((item) => match(item));
  }

  forEach(action: (item: T) => void): void {
    if (typeof action !== "function") {
      throwArgumentNull("action");
    }

    for (let index = 0; index < this._count; index++) {
      action(at(this._items, index));
    }
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
    return binarySearch(this._items, 0, this._count, item, comparisonOf(comparison));
  }

  add(item: T): ImmutableList<T> {
    return this.append([item]);
  }

  addRange(items: Iterable<T>): ImmutableList<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    return this.append([...items]);
  }

  insert(index: number, item: T): ImmutableList<T> {
    return this.insertRange(index, [item]);
  }

  insertRange(index: number, items: Iterable<T>): ImmutableList<T> {
    if (index < 0 || index > this._count) {
      throwArgumentOutOfRange("index");
    }

    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    const copy = this.visible();

    copy.splice(index, 0, ...items);

    return this.derive(copy);
  }

  remove(item: T, comparer?: IEqualityComparer<T> | null): ImmutableList<T> {
    const index = this.indexOf(item, 0, this._count, comparer);

    return index < 0 ? this : this.removeAt(index);
  }

  removeAll(match: (item: T) => boolean): ImmutableList<T> {
    checkMatch(match);

    const kept = this.visible().filter((item) => !match(item));

    return kept.length === this._count ? this : this.derive(kept);
  }

  removeAt(index: number): ImmutableList<T> {
    if (index >>> 0 >= this._count) {
      throwArgumentOutOfRange("index");
    }

    return this.removeRange(index, 1);
  }

  removeRange(index: number, count: number): ImmutableList<T> {
    this.checkRange(index, count);

    if (count === 0) {
      return this;
    }

    const copy = this.visible();

    copy.splice(index, count);

    return this.derive(copy);
  }

  /** `RemoveRange(IEnumerable<T>[, comparer])`: one occurrence per item; the receiver when nothing is removed. */
  removeItems(items: Iterable<T>, comparer?: IEqualityComparer<T> | null): ImmutableList<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("items");
    }

    const equals = equalityOf(comparer);
    const copy = this.visible();
    let removed = false;

    for (const item of items) {
      const index = copy.findIndex((candidate) => equals(candidate, item));

      if (index >= 0) {
        copy.splice(index, 1);
        removed = true;
      }
    }

    return removed ? this.derive(copy) : this;
  }

  /** `Replace`: `ArgumentException` (`Cannot find the old value`, `oldValue`) when absent. */
  replace(oldValue: T, newValue: T, comparer?: IEqualityComparer<T> | null): ImmutableList<T> {
    const index = this.indexOf(oldValue, 0, this._count, comparer);

    if (index < 0) {
      throwOldValueNotFound();
    }

    return this.setItem(index, newValue);
  }

  setItem(index: number, value: T): ImmutableList<T> {
    if (index >>> 0 >= this._count) {
      throwArgumentOutOfRange("index");
    }

    const copy = this.visible();

    copy[index] = value;

    return this.derive(copy);
  }

  clear(): ImmutableList<T> {
    return ImmutableList.empty();
  }

  /** Stable, `Comparer<T>.Default` without an argument; a comparer failure throws `InvalidOperationException`, as .NET. */
  sort(comparison?: ComparisonArgument<T>): ImmutableList<T> {
    const compare = comparisonOf(comparison);
    const copy = this.visible();

    try {
      copy.sort(compare);
    } catch (error) {
      throwCompareFailed(error);
    }

    return this.derive(copy);
  }

  reverse(): ImmutableList<T> {
    return this._count <= 1 ? this : this.derive(this.visible().reverse());
  }

  /** `CopyTo`: a short array or a negative index throws `ArgumentOutOfRangeException` (`arrayIndex`), as .NET's. */
  copyTo(array: T[], arrayIndex = 0): void {
    if (array === null || array === undefined) {
      throwArgumentNull("array");
    }

    if (arrayIndex < 0 || array.length - arrayIndex < this._count) {
      throwArgumentOutOfRange("arrayIndex");
    }

    for (let index = 0; index < this._count; index++) {
      array[arrayIndex + index] = at(this._items, index);
    }
  }

  /** A detached copy. */
  toArray(): T[] {
    return this.visible();
  }

  toJSON(): T[] {
    return this.visible();
  }

  /** .NET's ``System.Collections.Immutable.ImmutableList`1[T]`` text (an `ImmutableArray<T>` prints the same here). */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableList`1",
      ["T"],
      this.$typeArguments$ImmutableList,
    );
  }

  [Symbol.iterator](): Iterator<T> {
    return new ImmutableListIterator(this._items, this._count);
  }

  /** The version's elements as a fresh array. */
  private visible(): T[] {
    return this._items.slice(0, this._count);
  }

  /** A new version over `items`, keeping this version's type arguments. */
  private derive(items: T[]): ImmutableList<T> {
    return new ImmutableList(items, items.length, this.$typeArguments$ImmutableList);
  }

  /** W4-11: append in place when this version owns the array's tip, else copy first. */
  private append(items: readonly T[]): ImmutableList<T> {
    const store = ownsTip(this._items, this._count) ? this._items : this.visible();

    for (const item of items) {
      store.push(item);
    }

    return new ImmutableList(store, this._count + items.length, this.$typeArguments$ImmutableList);
  }

  /** `index` / `count` inside `0..count`, with .NET's parameter names. */
  private checkRange(index: number, count: number): void {
    if (index < 0) {
      throwArgumentOutOfRange("index");
    }

    if (count < 0 || index + count > this._count) {
      throwArgumentOutOfRange("count");
    }
  }
}

/** An index walk over a version's first `count` elements (no version check: nothing can change them). */
class ImmutableListIterator<T> implements Iterator<T> {
  private readonly _items: readonly T[];
  private readonly _count: number;
  private _index: number;

  constructor(items: readonly T[], count: number) {
    this._items = items;
    this._count = count;
    this._index = 0;
  }

  next(): IteratorResult<T> {
    if (this._index >= this._count) {
      return { done: true, value: undefined };
    }

    return { done: false, value: at(this._items, this._index++) };
  }
}

function equalityOf<T>(comparer: IEqualityComparer<T> | null | undefined): (a: T, b: T) => boolean {
  return comparer === null || comparer === undefined
    ? elementEquals
    : (a, b) => comparer.equals(a, b);
}

function checkMatch(match: unknown): void {
  if (typeof match !== "function") {
    throwArgumentNull("match");
  }
}

/**
 * `items[index]` for an index the caller has checked (or that a walk below the
 * version's count produced): `noUncheckedIndexedAccess` cannot see the check.
 */
function at<T>(items: readonly T[], index: number): T {
  return items[index] as T;
}
