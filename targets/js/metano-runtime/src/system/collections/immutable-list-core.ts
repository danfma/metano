import {
  type ComparisonArgument,
  comparisonOf,
  elementEquals,
  type IEqualityComparer,
} from "./default-comparer.ts";
import type { IImmutableList } from "./immutable-interfaces.ts";
import { detachTip, ownsTip } from "./immutable-storage.ts";
import type { IReadOnlyList } from "./interfaces.ts";
import { binarySearch } from "./sorted-array.ts";

import {
  throwArgumentNull,
  throwArgumentOutOfRange,
  throwCompareFailed,
  throwOldValueNotFound,
} from "./throw-helpers.ts";

/**
 * The one implementation behind `ImmutableList<T>` and `ImmutableArray<T>`
 * (spec 011 FR-054, OD-17, W4-66): copy-on-write over one array with W4-11's
 * tip-append sharing (`immutable-storage.ts`). `add` / `addRange` on the newest
 * version of an array append in place (amortized O(1)); every other mutator
 * copies the version's elements (O(n)). A version reads only its first
 * `_count` elements, so it never sees what a later version appended. Every
 * result is built by the subclass's `derive`, so the two classes stay siblings:
 * an `ImmutableArray` never answers `instanceof ImmutableList`. The members
 * whose .NET behaviour differs between the two are abstract here. Internal.
 */
export abstract class ImmutableListCore<T, Self extends ImmutableListCore<T, Self>>
  implements IImmutableList<T>, IReadOnlyList<T>
{
  declare readonly _items: T[];
  declare readonly _count: number;

  /** Defines `_items` and `_count`; each subclass then defines its own `$typeArguments$<Class>` slot. */
  constructor(items: T[], count: number) {
    Object.defineProperty(this, "_items", { value: items });
    Object.defineProperty(this, "_count", { value: count });
  }

  get count(): number {
    return this._count;
  }

  get length(): number {
    return this._count;
  }

  get isEmpty(): boolean {
    return this._count === 0;
  }

  /** The indexer, with the subclass's .NET exception. */
  abstract get(index: number): T;

  abstract indexOf(
    item: T,
    index?: number,
    count?: number,
    comparer?: IEqualityComparer<T> | null,
  ): number;

  abstract getRange(index: number, count: number): Self;

  abstract lastIndexOf(
    item: T,
    index?: number,
    count?: number,
    comparer?: IEqualityComparer<T> | null,
  ): number;

  abstract insertRange(index: number, items: Iterable<T>): Self;

  abstract removeAt(index: number): Self;

  abstract removeRange(index: number, count: number): Self;

  abstract binarySearch(item: T, comparison?: ComparisonArgument<T>): number;

  abstract addRange(items: Iterable<T>): Self;

  abstract clear(): Self;

  abstract sort(comparison?: ComparisonArgument<T>): Self;

  abstract copyTo(array: T[], arrayIndex?: number): void;

  abstract toString(): string;

  /** A new version over `items`, of which it owns the first `count`, keeping this version's type arguments. */
  protected abstract derive(items: T[], count: number): Self;

  /** This version, typed as its class: the result of a mutator that changes nothing. */
  protected abstract self(): Self;

  /** `list[^n]`. */
  fromEnd(n: number): T {
    return this.get(this._count - n);
  }

  /** `array[a..b]` on an `ImmutableArray<T>`. */
  slice(start: number, end: number = this._count): Self {
    return this.getRange(start, end - start);
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

  findAll(match: (item: T) => boolean): Self {
    checkMatch(match);

    return this.copied(this.visible().filter((item) => match(item)));
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

  add(item: T): Self {
    return this.append([item]);
  }

  insert(index: number, item: T): Self {
    return this.insertRange(index, [item]);
  }

  remove(item: T, comparer?: IEqualityComparer<T> | null): Self {
    const index = this.indexOf(item, 0, this._count, comparer);

    return index < 0 ? this.self() : this.removeAt(index);
  }

  removeAll(match: (item: T) => boolean): Self {
    checkMatch(match);

    const kept = this.visible().filter((item) => !match(item));

    return kept.length === this._count ? this.self() : this.copied(kept);
  }

  /** `RemoveRange(IEnumerable<T>[, comparer])`: one occurrence per item; the receiver when nothing is removed. */
  removeItems(items: Iterable<T>, comparer?: IEqualityComparer<T> | null): Self {
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

    return removed ? this.copied(copy) : this.self();
  }

  /** `Replace`: `ArgumentException` (`Cannot find the old value`, `oldValue`) when absent. */
  replace(oldValue: T, newValue: T, comparer?: IEqualityComparer<T> | null): Self {
    const index = this.indexOf(oldValue, 0, this._count, comparer);

    if (index < 0) {
      throwOldValueNotFound();
    }

    return this.setItem(index, newValue);
  }

  setItem(index: number, value: T): Self {
    if (index >>> 0 >= this._count) {
      throwArgumentOutOfRange("index");
    }

    const copy = this.visible();

    copy[index] = value;

    return this.copied(copy);
  }

  reverse(): Self {
    return this._count <= 1 ? this.self() : this.copied(this.visible().reverse());
  }

  /** A detached copy. */
  toArray(): T[] {
    return this.visible();
  }

  /**
   * Read-only view over `_items`: zero-copy when this version owns the full array
   * (detaching tip-append ownership so later `add` calls copy rather than mutating
   * the view); falls back to a shallow copy when an older version shares a grown
   * store (values are immutable, though array identity is not stable across calls).
   */
  asReadOnlyJsArray(): readonly T[] {
    if (this._items.length !== this._count) {
      return this.visible();
    }

    if (this._count > 0) {
      detachTip(this._items);
    }

    return this._items;
  }

  toJSON(): T[] {
    return this.visible();
  }

  [Symbol.iterator](): Iterator<T> {
    return new ImmutableListIterator(this._items, this._count);
  }

  /** The first `count` elements from `index` that equal `item`, `index` checked under `indexName`. */
  protected search(
    item: T,
    index: number,
    count: number,
    comparer: IEqualityComparer<T> | null | undefined,
    indexName: string,
  ): number {
    this.checkRange(index, count, indexName);

    const equals = equalityOf(comparer);

    for (let position = index; position < index + count; position++) {
      if (equals(at(this._items, position), item)) {
        return position;
      }
    }

    return -1;
  }

  /** The `count` elements from `index`, after the caller's checks. */
  protected range(index: number, count: number): Self {
    return this.copied(this._items.slice(index, index + count));
  }

  /** The last of `count` elements ending at `index` that equals `item`, after the caller's checks. */
  protected searchBackward(
    item: T,
    index: number,
    count: number,
    comparer: IEqualityComparer<T> | null | undefined,
  ): number {
    const equals = equalityOf(comparer);

    for (let position = index; position > index - count; position--) {
      if (equals(at(this._items, position), item)) {
        return position;
      }
    }

    return -1;
  }

  /** `items` inserted at `index`, after the caller's checks. */
  protected inserted(index: number, items: readonly T[]): Self {
    const copy = this.visible();

    copy.splice(index, 0, ...items);

    return this.copied(copy);
  }

  /** The version without `count` elements from `index` (itself for none), after the caller's checks. */
  protected without(index: number, count: number): Self {
    if (count === 0) {
      return this.self();
    }

    const copy = this.visible();

    copy.splice(index, count);

    return this.copied(copy);
  }

  /** .NET's `Array.BinarySearch` order over the version's elements. */
  protected searchSorted(item: T, comparison: ComparisonArgument<T> | undefined): number {
    return binarySearch(this._items, 0, this._count, item, comparisonOf(comparison));
  }

  /** The version's elements sorted into a new array; a comparer failure throws `InvalidOperationException`, as `Array.Sort`. */
  protected sortedCopy(comparison: ComparisonArgument<T> | undefined): T[] {
    const compare = comparisonOf(comparison);
    const copy = this.visible();

    try {
      copy.sort(compare);
    } catch (error) {
      throwCompareFailed(error);
    }

    return copy;
  }

  /** A copy of the version's visible elements into `array` from `arrayIndex`, after the caller's checks. */
  protected copyInto(array: T[], arrayIndex: number): void {
    for (let index = 0; index < this._count; index++) {
      array[arrayIndex + index] = at(this._items, index);
    }
  }

  /** The version's elements as a fresh array. */
  protected visible(): T[] {
    return this._items.slice(0, this._count);
  }

  /** W4-11: append in place when this version owns the array's tip, else copy first. */
  protected append(items: readonly T[]): Self {
    const store = ownsTip(this._items, this._count) ? this._items : this.visible();

    for (const item of items) {
      store.push(item);
    }

    return this.derive(store, this._count + items.length);
  }

  /** `index` / `count` inside `0..count`, with .NET's parameter names. */
  protected checkRange(index: number, count: number, indexName = "index"): void {
    if (index < 0) {
      throwArgumentOutOfRange(indexName);
    }

    if (count < 0 || index + count > this._count) {
      throwArgumentOutOfRange("count");
    }
  }

  /** A new version that owns all of `items`. */
  private copied(items: T[]): Self {
    return this.derive(items, items.length);
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
export function at<T>(items: readonly T[], index: number): T {
  return items[index] as T;
}
