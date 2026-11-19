/**
 * `System.Collections.Generic.Stack<T>` (spec 011 FR-001, FR-006;
 * contracts/runtime-collections.md § `Stack<T>`). Stored bottom to top;
 * enumerated, copied and serialized top first, as C# does.
 */
import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { copyInto } from "./copy-to.ts";
import { elementEquals } from "./default-comparer.ts";
import type { IReadOnlyCollection } from "./interfaces.ts";
import {
  throwArgumentNull,
  throwEmptyStack,
  throwNegative,
  throwEnumeratorModified,
} from "./throw-helpers.ts";

/*
 * Element reads cast `as T`: every read is guarded by the `length === 0` check or by the
 * enumerator's position, which stays within `0..length - 1`.
 */

let Stack$rt: CollectionType<Stack<unknown>> | undefined;

export class Stack<T> implements IReadOnlyCollection<T> {
  /** Bottom to top. */
  declare readonly _items: T[];
  declare _version: number;
  declare readonly $typeArguments$Stack: readonly RuntimeType[] | undefined;

  /** Pushes `items` in order, so the last one is the top. */
  constructor(items?: Iterable<T> | null, typeArguments?: readonly RuntimeType[] | null) {
    Object.defineProperty(this, "_items", {
      value: items === null || items === undefined ? [] : Array.from(items),
    });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "$typeArguments$Stack", { value: typeArguments ?? undefined });
  }

  /** `new Stack<T>(collection)`: `new Stack([1, 2, 3])` enumerates `3, 2, 1`. */
  static from<T>(items: Iterable<T>, typeArguments?: readonly RuntimeType[] | null): Stack<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("collection");
    }

    return new Stack<T>(items, typeArguments);
  }

  static withCapacity<T>(
    capacity: number,
    typeArguments?: readonly RuntimeType[] | null,
  ): Stack<T> {
    if (capacity < 0) {
      throwNegative("capacity");
    }

    return new Stack<T>(null, typeArguments);
  }

  /** The runtime type descriptor of the open `Stack<T>`: a data literal, built on first read. */
  static get $runtimeType(): CollectionType<Stack<unknown>> {
    Stack$rt ??= {
      kind: "collection",
      collection: "Stack",
      name: "Stack",
      fullName: "System.Collections.Generic.Stack",
      namespace: "System.Collections.Generic",
      ctor: Stack,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [{ collection: "IReadOnlyCollection", arguments: [0] }],
      test: (value: unknown): value is Stack<unknown> => value instanceof Stack,
      closeFor: (instance) => closeCollectionFor(Stack.$runtimeType, instance),
    };

    return Stack$rt;
  }

  get count(): number {
    return this._items.length;
  }

  push(item: T): void {
    this._items.push(item);
    this._bump();
  }

  pop(): T {
    const items = this._items;

    if (items.length === 0) {
      throwEmptyStack();
    }

    const item = items.pop() as T;

    this._bump();

    return item;
  }

  peek(): T {
    const items = this._items;

    if (items.length === 0) {
      throwEmptyStack();
    }

    return items[items.length - 1] as T;
  }

  /** `TryPop(out item)`: `missingValue` is what C# stores when the stack is empty. */
  tryPop<D>(missingValue: D): readonly [found: true, value: T] | readonly [found: false, value: D] {
    return this._items.length === 0 ? [false, missingValue] : [true, this.pop()];
  }

  tryPeek<D>(
    missingValue: D,
  ): readonly [found: true, value: T] | readonly [found: false, value: D] {
    return this._items.length === 0 ? [false, missingValue] : [true, this.peek()];
  }

  contains(item: T): boolean {
    return this._items.some((stacked) => elementEquals(stacked, item));
  }

  clear(): void {
    this._items.length = 0;
    this._bump();
  }

  /** Copies top first, as .NET does. */
  copyTo(array: T[], arrayIndex?: number): void {
    copyInto(this.toArray(), array, arrayIndex ?? 0);
  }

  /** A detached copy, top first (FR-055). */
  toArray(): T[] {
    return this._items.slice().reverse();
  }

  /** Top first, as System.Text.Json writes a stack. */
  toJSON(): T[] {
    return this.toArray();
  }

  toString(): string {
    return collectionTypeName(
      "System.Collections.Generic.Stack`1",
      ["T"],
      this.$typeArguments$Stack,
    );
  }

  /** Top first. */
  [Symbol.iterator](): IterableIterator<T> {
    return new StackIterator(this);
  }

  private _bump(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/** `Stack<T>`'s enumerator: top first, with .NET's own modification message. */
class StackIterator<T> implements IterableIterator<T> {
  private readonly _stack: Stack<T>;
  private readonly _expectedVersion: number;
  private _position: number;

  constructor(stack: Stack<T>) {
    this._stack = stack;
    this._expectedVersion =
      typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__ ? stack._version : 0;
    this._position = stack._items.length;
  }

  next(): IteratorResult<T> {
    const stack = this._stack;

    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      stack._version !== this._expectedVersion
    ) {
      throwEnumeratorModified();
    }

    if (this._position <= 0) {
      return { done: true, value: undefined };
    }

    return { done: false, value: stack._items[--this._position] as T };
  }

  [Symbol.iterator](): IterableIterator<T> {
    return this;
  }
}
