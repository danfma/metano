/**
 * `Dictionary<TKey, TValue>.KeyCollection` / `.ValueCollection` (spec 011
 * FR-012): live read-only views over any keyed source, so `Dictionary` and the
 * 011h dictionaries share them. This module imports no dictionary class.
 */
import { collectionTypeName } from "./collection-type-name.ts";
import { copyInto } from "./copy-to.ts";
import { elementEquals } from "./default-comparer.ts";
import type { IReadOnlyCollection } from "./interfaces.ts";
import { throwArgumentNull } from "./throw-helpers.ts";
import type { RuntimeType } from "../reflection/runtime-type.ts";

/** What a view reads: the source's count, key lookup and `[key, value]` enumeration. */
export interface KeyValueSource<K, V> extends Iterable<readonly [K, V]> {
  readonly count: number;
  containsKey(key: K): boolean;
  /** The source's type arguments (`[K, V]`), for `toString()`. */
  readonly $typeArguments$Dictionary?: readonly RuntimeType[] | undefined;
}

export class KeyCollection<K> implements IReadOnlyCollection<K> {
  declare readonly _source: KeyValueSource<K, unknown>;

  constructor(source: KeyValueSource<K, unknown>) {
    if (source === null || source === undefined) {
      throwArgumentNull("dictionary");
    }

    Object.defineProperty(this, "_source", { value: source });
  }

  get count(): number {
    return this._source.count;
  }

  contains(key: K): boolean {
    return this._source.containsKey(key);
  }

  copyTo(array: K[], arrayIndex?: number): void {
    copyInto(this.toArray(), array, arrayIndex ?? 0);
  }

  toArray(): K[] {
    return Array.from(this);
  }

  toJSON(): K[] {
    return this.toArray();
  }

  toString(): string {
    return collectionTypeName(
      "System.Collections.Generic.Dictionary`2+KeyCollection",
      ["TKey", "TValue"],
      this._source.$typeArguments$Dictionary,
    );
  }

  [Symbol.iterator](): IterableIterator<K> {
    return new ProjectionIterator(this._source[Symbol.iterator](), 0);
  }
}

export class ValueCollection<V> implements IReadOnlyCollection<V> {
  declare readonly _source: KeyValueSource<unknown, V>;

  constructor(source: KeyValueSource<unknown, V>) {
    if (source === null || source === undefined) {
      throwArgumentNull("dictionary");
    }

    Object.defineProperty(this, "_source", { value: source });
  }

  get count(): number {
    return this._source.count;
  }

  contains(value: V): boolean {
    for (const item of this) {
      if (elementEquals(item, value)) {
        return true;
      }
    }

    return false;
  }

  copyTo(array: V[], arrayIndex?: number): void {
    copyInto(this.toArray(), array, arrayIndex ?? 0);
  }

  toArray(): V[] {
    return Array.from(this);
  }

  toJSON(): V[] {
    return this.toArray();
  }

  toString(): string {
    return collectionTypeName(
      "System.Collections.Generic.Dictionary`2+ValueCollection",
      ["TKey", "TValue"],
      this._source.$typeArguments$Dictionary,
    );
  }

  [Symbol.iterator](): IterableIterator<V> {
    return new ProjectionIterator(this._source[Symbol.iterator](), 1);
  }
}

/** One element of each `[key, value]` pair; the source iterator applies the version rule. */
class ProjectionIterator<E> implements IterableIterator<E> {
  private readonly _entries: Iterator<readonly [unknown, unknown]>;
  private readonly _position: 0 | 1;

  constructor(entries: Iterator<readonly [unknown, unknown]>, position: 0 | 1) {
    this._entries = entries;
    this._position = position;
  }

  next(): IteratorResult<E> {
    const step = this._entries.next();

    return step.done === true
      ? { done: true, value: undefined }
      : { done: false, value: step.value[this._position] as E };
  }

  [Symbol.iterator](): IterableIterator<E> {
    return this;
  }
}
