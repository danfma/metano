import type { ComparisonArgument } from "./default-comparer.ts";
import type { IList } from "./interfaces.ts";
import { List } from "./list.ts";
import { throwArgumentOutOfRange } from "./throw-helpers.ts";

/**
 * The one implementation behind `ImmutableList<T>.Builder` and
 * `ImmutableArray<T>.Builder` (spec 011 FR-054, W4-66): a small mutable class
 * over its own `List` (composition: `instanceof List` is `false`, as
 * `builder is List<T>` in .NET). Every mutating call reports itself through
 * `modified()`, a no-op included, except `remove` of an absent element (as
 * .NET 10's list builder); each subclass decides what that means for its
 * enumeration and how its indexer reports a bad index. `removeAt` / `insert`
 * check with .NET's default `ArgumentOutOfRangeException` text, not `List`'s.
 * Internal.
 */
export abstract class ImmutableListBuilderCore<T> implements IList<T> {
  declare readonly _list: List<T>;

  /** Defines `_list`, a copy of `source`; each subclass then defines its own fields. */
  constructor(source: Iterable<T> | null | undefined) {
    Object.defineProperty(this, "_list", { value: List.from(source ?? []) });
  }

  abstract toString(): string;

  abstract [Symbol.iterator](): Iterator<T>;

  /** The indexer's bounds check, with the subclass's .NET exception. */
  protected abstract checkItemIndex(index: number): void;

  /** Records a mutating call (a no-op included). */
  protected abstract modified(): void;

  get count(): number {
    return this._list.count;
  }

  get isReadOnly(): boolean {
    return false;
  }

  get(index: number): T {
    this.checkItemIndex(index);

    return this._list.get(index);
  }

  set(index: number, value: T): void {
    this.checkItemIndex(index);
    this._list.set(index, value);
    this.modified();
  }

  update(index: number, f: (current: T) => T): T {
    const value = f(this.get(index));

    this.set(index, value);

    return value;
  }

  updateReturningOld(index: number, f: (current: T) => T): T {
    const current = this.get(index);

    this.set(index, f(current));

    return current;
  }

  fromEnd(n: number): T {
    return this.get(this.count - n);
  }

  add(item: T): void {
    this._list.add(item);
    this.modified();
  }

  addRange(items: Iterable<T>): void {
    this._list.addRange(items);
    this.modified();
  }

  insert(index: number, item: T): void {
    this.checkInsertIndex(index);
    this._list.insert(index, item);
    this.modified();
  }

  insertRange(index: number, items: Iterable<T>): void {
    this.checkInsertIndex(index);
    this._list.insertRange(index, items);
    this.modified();
  }

  /** The one mutating call that records nothing when it changes nothing (.NET 10). */
  remove(item: T): boolean {
    const removed = this._list.remove(item);

    if (removed) {
      this.modified();
    }

    return removed;
  }

  removeAt(index: number): void {
    if (index >>> 0 >= this.count) {
      throwArgumentOutOfRange("index");
    }

    this._list.removeAt(index);
    this.modified();
  }

  removeAll(match: (item: T) => boolean): number {
    const removed = this._list.removeAll(match);

    this.modified();

    return removed;
  }

  clear(): void {
    this._list.clear();
    this.modified();
  }

  contains(item: T): boolean {
    return this._list.contains(item);
  }

  indexOf(item: T): number {
    return this._list.indexOf(item);
  }

  lastIndexOf(item: T): number {
    return this._list.lastIndexOf(item);
  }

  sort(comparison?: ComparisonArgument<T>): void {
    this._list.sort(comparison);
    this.modified();
  }

  reverse(): void {
    this._list.reverse();
    this.modified();
  }

  copyTo(array: T[], arrayIndex = 0): void {
    this._list.copyTo(array, arrayIndex);
  }

  toJSON(): T[] {
    return this._list.toArray();
  }

  private checkInsertIndex(index: number): void {
    if (index < 0 || index > this.count) {
      throwArgumentOutOfRange("index");
    }
  }
}
