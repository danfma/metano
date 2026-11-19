import type { RuntimeType } from "../reflection/runtime-type.ts";
import { BuilderIterator } from "./builder-iterator.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import type { ComparisonArgument } from "./default-comparer.ts";
import { ImmutableList } from "./immutable-list.ts";
import type { IList } from "./interfaces.ts";
import { List } from "./list.ts";
import { throwArgumentOutOfRange } from "./throw-helpers.ts";

/**
 * Mirrors `ImmutableList<T>.Builder` (and `ImmutableArray<T>.Builder`, OD-17;
 * spec 011 FR-054): a small mutable class over its own `List` (composition:
 * `instanceof List` is `false`, as `builder is List<T>` in .NET). It keeps its
 * own version, so every successful mutation invalidates a running enumeration
 * with .NET's builder text, and its own bounds checks, which use .NET's
 * default `ArgumentOutOfRangeException` text, not `List`'s. `toImmutableList`
 * copies, so a builder never shares storage with an immutable version (W4-11).
 */
export class ImmutableListBuilder<T> implements IList<T> {
  declare readonly _list: List<T>;
  declare _version: number;
  declare readonly _typeArguments: readonly RuntimeType[] | undefined;

  /** `ToBuilder()` / `CreateBuilder<T>()`: a copy of `source`, whose type arguments it keeps when `source` is an `ImmutableList`. */
  constructor(source?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null) {
    const inherited =
      source instanceof ImmutableList ? source.$typeArguments$ImmutableList : undefined;

    Object.defineProperty(this, "_list", { value: List.from(source ?? []) });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "_typeArguments", { value: typeArguments ?? inherited });
  }

  get count(): number {
    return this._list.count;
  }

  get isReadOnly(): boolean {
    return false;
  }

  get(index: number): T {
    this.checkIndex(index);

    return this._list.get(index);
  }

  set(index: number, value: T): void {
    this.checkIndex(index);
    this._list.set(index, value);
    this.bump();
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
    this.bump();
  }

  addRange(items: Iterable<T>): void {
    this._list.addRange(items);
    this.bump();
  }

  insert(index: number, item: T): void {
    if (index < 0 || index > this.count) {
      throwArgumentOutOfRange("index");
    }

    this._list.insert(index, item);
    this.bump();
  }

  insertRange(index: number, items: Iterable<T>): void {
    if (index < 0 || index > this.count) {
      throwArgumentOutOfRange("index");
    }

    this._list.insertRange(index, items);
    this.bump();
  }

  remove(item: T): boolean {
    const removed = this._list.remove(item);

    if (removed) {
      this.bump();
    }

    return removed;
  }

  removeAt(index: number): void {
    this.checkIndex(index);
    this._list.removeAt(index);
    this.bump();
  }

  removeAll(match: (item: T) => boolean): number {
    const removed = this._list.removeAll(match);

    if (removed > 0) {
      this.bump();
    }

    return removed;
  }

  clear(): void {
    this._list.clear();
    this.bump();
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
    this.bump();
  }

  reverse(): void {
    this._list.reverse();
    this.bump();
  }

  copyTo(array: T[], arrayIndex = 0): void {
    this._list.copyTo(array, arrayIndex);
  }

  toJSON(): T[] {
    return this._list.toArray();
  }

  /** .NET's ``System.Collections.Immutable.ImmutableList`1+Builder[T]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableList`1+Builder",
      ["T"],
      this._typeArguments,
    );
  }

  [Symbol.iterator](): Iterator<T> {
    return new BuilderIterator(this, this._list[Symbol.iterator]());
  }

  private checkIndex(index: number): void {
    if (index >>> 0 >= this.count) {
      throwArgumentOutOfRange("index");
    }
  }

  private bump(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/** `builder.ToImmutable()`: a copy, never sharing the builder's storage. */
export function toImmutableList<T>(builder: ImmutableListBuilder<T>): ImmutableList<T> {
  return ImmutableList.of(builder._list.toArray(), builder._typeArguments);
}
