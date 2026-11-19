/**
 * `System.Collections.Generic.Queue<T>` (spec 011 FR-001, FR-006;
 * contracts/runtime-collections.md § `Queue<T>`): an array and a head index. `enqueue`
 * pushes, `dequeue` reads at the head and advances it; the consumed prefix is dropped once
 * it is at least half of the array, so both stay amortized O(1), interleaved too (2.x
 * lowered `Dequeue` to `shift`, O(n)).
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

/*
 * Element reads cast `items[i] as T`: every read is of a slot from `_head` to the end of
 * the array, which hold the queued elements (the consumed slots before `_head` hold
 * `undefined` and are never read).
 */

/** A consumed prefix shorter than this is never compacted (it is cheaper to keep it). */
const MINIMUM_COMPACTION = 32;

let Queue$rt: CollectionType<Queue<unknown>> | undefined;

export class Queue<T> implements IReadOnlyCollection<T> {
  /** The queued elements are `_items[_head..]`; the slots before `_head` are consumed. */
  declare readonly _items: (T | undefined)[];
  declare _head: number;
  declare _version: number;
  declare readonly $typeArguments$Queue: readonly RuntimeType[] | undefined;

  constructor(items?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null) {
    const initial = items === null || items === undefined ? [] : Array.from(items);

    Object.defineProperty(this, "_items", { value: initial });
    Object.defineProperty(this, "_head", { value: 0, writable: true });
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
    return this._items.length - this._head;
  }

  enqueue(item: T): void {
    this._items.push(item);
    this._bump();
  }

  dequeue(): T {
    if (this._head === this._items.length) {
      throwEmptyQueue();
    }

    return this._take();
  }

  peek(): T {
    if (this._head === this._items.length) {
      throwEmptyQueue();
    }

    return this._items[this._head] as T;
  }

  /** `TryDequeue(out item)`: `missingValue` is what C# stores when the queue is empty. */
  tryDequeue<D>(
    missingValue: D,
  ): readonly [found: true, value: T] | readonly [found: false, value: D] {
    return this._head === this._items.length ? [false, missingValue] : [true, this._take()];
  }

  tryPeek<D>(
    missingValue: D,
  ): readonly [found: true, value: T] | readonly [found: false, value: D] {
    return this._head === this._items.length
      ? [false, missingValue]
      : [true, this._items[this._head] as T];
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
    this._items.length = 0;
    this._head = 0;
    this._bump();
  }

  copyTo(array: T[], arrayIndex?: number): void {
    copyInto(this.toArray(), array, arrayIndex ?? 0);
  }

  /** A detached copy, front to back (FR-055). */
  toArray(): T[] {
    return this._items.slice(this._head) as T[];
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
    return this._items[this._head + offset] as T;
  }

  private _take(): T {
    const items = this._items;
    const head = this._head;
    const item = items[head] as T;

    items[head] = undefined;
    this._head = head + 1;
    this._compact();
    this._bump();

    return item;
  }

  /** Drops the consumed prefix once it is at least half of the array (amortized O(1)). */
  private _compact(): void {
    const items = this._items;
    const head = this._head;

    if (head === items.length) {
      items.length = 0;
      this._head = 0;
    } else if (head >= MINIMUM_COMPACTION && head * 2 >= items.length) {
      items.splice(0, head);
      this._head = 0;
    }
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

    if (this._offset >= queue.count) {
      return { done: true, value: undefined };
    }

    return { done: false, value: queue._at(this._offset++) };
  }

  [Symbol.iterator](): IterableIterator<T> {
    return this;
  }
}
