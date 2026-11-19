/**
 * `System.Collections.Generic.Queue<T>` (spec 011 FR-001, FR-006;
 * contracts/runtime-collections.md § `Queue<T>`): a ring buffer, so `dequeue` is
 * O(1) (2.x lowered it to `shift`, O(n)).
 */
import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { copyInto } from "./copy-to.ts";
import { elementEquals } from "./default-comparer.ts";
import type { IReadOnlyCollection } from "./interfaces.ts";
import {
  throwArgumentNull,
  throwEmptyQueue,
  throwModified,
  throwNegative,
} from "./throw-helpers.ts";

/** The smallest ring a full wrapped queue grows into. */
const MINIMUM_CAPACITY = 4;

let Queue$rt: CollectionType<Queue<unknown>> | undefined;

export class Queue<T> implements IReadOnlyCollection<T> {
  /** The ring: `_size` elements from `_head`, wrapping at `_items.length`. */
  declare _items: (T | undefined)[];
  declare _head: number;
  declare _size: number;
  declare _version: number;
  declare readonly $typeArguments$Queue: readonly RuntimeType[] | undefined;

  constructor(items?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null) {
    const initial = items === null || items === undefined ? [] : Array.from(items);

    Object.defineProperty(this, "_items", { value: initial, writable: true });
    Object.defineProperty(this, "_head", { value: 0, writable: true });
    Object.defineProperty(this, "_size", { value: initial.length, writable: true });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "$typeArguments$Queue", { value: typeArguments ?? undefined });
  }

  /** `new Queue<T>(collection)`: enqueues in order. */
  static from<T>(items: Iterable<T>, typeArguments?: readonly RuntimeType[] | null): Queue<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("collection");
    }

    return new Queue<T>(items, typeArguments);
  }

  static withCapacity<T>(
    capacity: number,
    typeArguments?: readonly RuntimeType[] | null,
  ): Queue<T> {
    if (capacity < 0) {
      throwNegative("capacity");
    }

    return new Queue<T>(null, typeArguments);
  }

  /** The runtime type descriptor of the open `Queue<T>`: a data literal, built on first read. */
  static get $runtimeType(): CollectionType<Queue<unknown>> {
    Queue$rt ??= {
      kind: "collection",
      collection: "Queue",
      name: "Queue",
      fullName: "System.Collections.Generic.Queue",
      namespace: "System.Collections.Generic",
      ctor: Queue,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [{ collection: "IReadOnlyCollection", arguments: [0] }],
      test: (value: unknown): value is Queue<unknown> => value instanceof Queue,
      closeFor: (instance) => closeCollectionFor(Queue.$runtimeType, instance),
    };

    return Queue$rt;
  }

  get count(): number {
    return this._size;
  }

  enqueue(item: T): void {
    if (this._size === this._items.length && this._head === 0) {
      this._items.push(item);
    } else {
      if (this._size === this._items.length) {
        this._grow();
      }

      const items = this._items;

      items[wrap(this._head + this._size, items.length)] = item;
    }

    this._size++;
    this._bump();
  }

  dequeue(): T {
    if (this._size === 0) {
      throwEmptyQueue();
    }

    return this._take();
  }

  peek(): T {
    if (this._size === 0) {
      throwEmptyQueue();
    }

    return this._items[this._head] as T;
  }

  /** `TryDequeue(out item)`: `missingValue` is what C# stores when the queue is empty. */
  tryDequeue<D>(
    missingValue: D,
  ): readonly [found: true, value: T] | readonly [found: false, value: D] {
    return this._size === 0 ? [false, missingValue] : [true, this._take()];
  }

  tryPeek<D>(
    missingValue: D,
  ): readonly [found: true, value: T] | readonly [found: false, value: D] {
    return this._size === 0 ? [false, missingValue] : [true, this._items[this._head] as T];
  }

  contains(item: T): boolean {
    for (const queued of this) {
      if (elementEquals(queued, item)) {
        return true;
      }
    }

    return false;
  }

  clear(): void {
    this._items = [];
    this._head = 0;
    this._size = 0;
    this._bump();
  }

  copyTo(array: T[], arrayIndex?: number): void {
    copyInto(this.toArray(), array, arrayIndex ?? 0);
  }

  /** A detached copy, front to back (FR-055). */
  toArray(): T[] {
    const result: T[] = [];

    for (let offset = 0; offset < this._size; offset++) {
      result.push(this._at(offset));
    }

    return result;
  }

  toJSON(): T[] {
    return this.toArray();
  }

  toString(): string {
    return collectionTypeName(
      "System.Collections.Generic.Queue`1",
      ["T"],
      this.$typeArguments$Queue,
    );
  }

  /** Front to back. */
  [Symbol.iterator](): IterableIterator<T> {
    return new QueueIterator(this);
  }

  /** The element `offset` places behind the head. Internal. */
  _at(offset: number): T {
    const items = this._items;

    return items[wrap(this._head + offset, items.length)] as T;
  }

  private _take(): T {
    const items = this._items;
    const item = items[this._head] as T;

    items[this._head] = undefined;
    this._head = wrap(this._head + 1, items.length);
    this._size--;
    this._bump();

    return item;
  }

  /**
   * Grows a full ring whose head is not at 0: copies it front to back into an array of
   * twice its length (head at 0, the free slots `undefined`, never holes), so interleaved
   * enqueues and dequeues stay amortized O(1). A full ring with its head at 0 grows
   * through the array's own `push` instead.
   */
  private _grow(): void {
    const grown = this.toArray() as (T | undefined)[];
    const capacity = Math.max(MINIMUM_CAPACITY, this._items.length * 2);

    while (grown.length < capacity) {
      grown.push(undefined);
    }

    this._items = grown;
    this._head = 0;
  }

  private _bump(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/** `Queue<T>`'s enumerator: front to back, any change makes the next step throw. */
class QueueIterator<T> implements IterableIterator<T> {
  private readonly _queue: Queue<T>;
  private readonly _expectedVersion: number;
  private _offset = 0;

  constructor(queue: Queue<T>) {
    this._queue = queue;
    this._expectedVersion =
      typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__ ? queue._version : 0;
  }

  next(): IteratorResult<T> {
    const queue = this._queue;

    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      queue._version !== this._expectedVersion
    ) {
      throwModified();
    }

    if (this._offset >= queue._size) {
      return { done: true, value: undefined };
    }

    return { done: false, value: queue._at(this._offset++) };
  }

  [Symbol.iterator](): IterableIterator<T> {
    return this;
  }
}

/** `position` folded back into `0..length - 1` (it never exceeds `2 * length - 1`). */
function wrap(position: number, length: number): number {
  return position >= length ? position - length : position;
}
