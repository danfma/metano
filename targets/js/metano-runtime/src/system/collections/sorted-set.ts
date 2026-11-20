import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { closeCollectionFor } from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { copyIntoSorted } from "./copy-to.ts";
import { comparisonOf, defaultComparerFor, type IComparer } from "./default-comparer.ts";
import type { IReadOnlySet, ISet } from "./interfaces.ts";
import { predecessor, RedBlackTree, type RedBlackNode, successor } from "./red-black-tree.ts";
import * as setAlgebra from "./set-algebra.ts";
import {
  throwArgumentNull,
  throwEnumeratorModified,
  throwArgumentOutOfRange,
  throwViewBoundsInverted,
} from "./throw-helpers.ts";

/** A `SortedSet`'s range: the whole tree, or a view's inclusive `[lower, upper]`. */
type ViewBounds<T> =
  | { readonly bounded: false }
  | { readonly bounded: true; readonly lower: T; readonly upper: T };

/** The bounds of every set that is not a view (an object literal: no module-load call). */
const UNBOUNDED: ViewBounds<never> = { bounded: false };

let SortedSet$rt: CollectionType<SortedSet<unknown>> | undefined;

/**
 * Mirrors `System.Collections.Generic.SortedSet<T>` (spec 011 FR-054): a
 * red-black tree ordered by the comparer, which alone decides equality
 * (`compare(a, b) === 0`). `getViewBetween` returns a live bounded
 * `SortedSet` over the same tree and version (`.NET`'s `TreeSubSet`).
 *
 * Every `add`, `remove` and `clear` bumps the version, a no-op included, as
 * .NET does; the set operations bump only when they change the set, except
 * `intersectWith`, which rebuilds a non-empty set. A live enumerator then
 * throws the sorted classes' `Collection was modified after the enumerator
 * was instantiated.`
 */
export class SortedSet<T> implements ISet<T>, IReadOnlySet<T> {
  declare readonly _tree: RedBlackTree<T, undefined>;
  /** The set whose `_version` counts the shared tree's changes: itself, or a view's root. */
  declare readonly _owner: SortedSet<T>;
  declare _version: number;
  declare readonly _comparer: IComparer<T> | null;
  /** A view's inclusive range; the whole tree for a set. */
  declare readonly _bounds: ViewBounds<T>;
  declare readonly $typeArguments$SortedSet: readonly RuntimeType[] | undefined;

  constructor(
    items?: Iterable<T> | null,
    comparer?: IComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ) {
    const ownComparer = comparer ?? null;

    Object.defineProperty(this, "_tree", {
      value: new RedBlackTree<T, undefined>(comparisonOf(ownComparer)),
      writable: true,
    });
    Object.defineProperty(this, "_owner", { value: this, writable: true });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "_comparer", { value: ownComparer });
    Object.defineProperty(this, "_bounds", { value: UNBOUNDED, writable: true });
    Object.defineProperty(this, "$typeArguments$SortedSet", { value: typeArguments ?? undefined });

    if (items !== null && items !== undefined) {
      for (const item of items) {
        this._tree.insert(item, undefined);
      }
    }
  }

  /** `new SortedSet<T>(collection[, comparer])`: a copy; `ArgumentNullException` (`collection`) for `null`. */
  static from<T>(
    items: Iterable<T> | null | undefined,
    comparer?: IComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): SortedSet<T> {
    if (items === null || items === undefined) {
      throwArgumentNull("collection");
    }

    return new SortedSet(items, comparer, typeArguments);
  }

  /** Collection initializers and expressions: duplicates are ignored. */
  static of<T>(
    items: T[],
    comparer?: IComparer<T> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): SortedSet<T> {
    return new SortedSet(items, comparer, typeArguments);
  }

  static get $runtimeType(): CollectionType<SortedSet<unknown>> {
    return (SortedSet$rt ??= {
      kind: "collection",
      collection: "SortedSet",
      name: "SortedSet",
      fullName: "System.Collections.Generic.SortedSet",
      namespace: "System.Collections.Generic",
      ctor: SortedSet,
      typeArguments: [],
      variance: ["invariant"],
      interfaces: [
        { collection: "ISet", arguments: [0] },
        { collection: "ICollection", arguments: [0] },
        { collection: "IReadOnlySet", arguments: [0] },
        { collection: "IReadOnlyCollection", arguments: [0] },
      ],
      test: (value: unknown): value is SortedSet<unknown> => value instanceof SortedSet,
      closeFor: (instance) => closeCollectionFor(SortedSet.$runtimeType, instance),
    });
  }

  get count(): number {
    if (!this._bounds.bounded) {
      return this._tree.count;
    }

    let count = 0;

    for (let node = this.firstNode(); node !== null; node = this.nextNode(node)) {
      count++;
    }

    return count;
  }

  get isReadOnly(): false {
    return false;
  }

  /** The argument comparer, or `defaultComparer` (`Comparer<T>.Default`). */
  get comparer(): IComparer<T> {
    return this._comparer ?? defaultComparerFor<T>();
  }

  /** `Min`: the smallest element, or the caller's `default(T)` when empty (D30). */
  minOr<D>(defaultValue: D): T | D {
    const node = this.firstNode();

    return node === null ? defaultValue : node.key;
  }

  /** `Max`: the largest element, or the caller's `default(T)` when empty (D30). */
  maxOr<D>(defaultValue: D): T | D {
    const node = this.lastNode();

    return node === null ? defaultValue : node.key;
  }

  add(item: T): boolean {
    if (!this.inRange(item)) {
      throwArgumentOutOfRange("item");
    }

    this.bumpVersion();

    return this._tree.insert(item, undefined).inserted;
  }

  /** An empty tree returns before the version bump, as .NET's `DoRemove` does. */
  remove(item: T): boolean {
    if (!this.inRange(item) || this._tree.count === 0) {
      return false;
    }

    this.bumpVersion();

    return this._tree.delete(item);
  }

  contains(item: T): boolean {
    return this.inRange(item) && this._tree.find(item) !== null;
  }

  /** A set always bumps the version; a view over an empty range changes nothing, as .NET's `TreeSubSet`. */
  clear(): void {
    if (!this._bounds.bounded) {
      this.bumpVersion();
      this._tree.clear();

      return;
    }

    const items = this.toArray();

    if (items.length === 0) {
      return;
    }

    this.bumpVersion();

    for (const item of items) {
      this._tree.delete(item);
    }
  }

  /** The number of elements removed; bumps the version only when one is. */
  removeWhere(match: (item: T) => boolean): number {
    if (typeof match !== "function") {
      throwArgumentNull("match");
    }

    const matches = this.toArray().filter((item) => match(item));

    for (const item of matches) {
      this.remove(item);
    }

    return matches.length;
  }

  /** `TryGetValue`: the stored element equal to `equalValue`, else the caller's `default(T)` (D30). */
  tryGetValue(equalValue: T, defaultValue: T): readonly [boolean, T] {
    const node = this.inRange(equalValue) ? this._tree.find(equalValue) : null;

    return node === null ? [false, defaultValue] : [true, node.key];
  }

  /** The elements in descending order; the enumeration is version-checked. */
  reverse(): Iterable<T> {
    return { [Symbol.iterator]: () => new SortedSetIterator(this, this.lastNode(), false) };
  }

  /** A live view of the elements in `[lowerValue, upperValue]`, sharing this set's tree and version. */
  getViewBetween(lowerValue: T, upperValue: T): SortedSet<T> {
    const bounds = this._bounds;

    if (bounds.bounded && this.compareItems(bounds.lower, lowerValue) > 0) {
      throwArgumentOutOfRange("lowerValue");
    }

    if (bounds.bounded && this.compareItems(bounds.upper, upperValue) < 0) {
      throwArgumentOutOfRange("upperValue");
    }

    if (this.compareItems(lowerValue, upperValue) > 0) {
      throwViewBoundsInverted();
    }

    const view = new SortedSet<T>(null, this._comparer, this.$typeArguments$SortedSet);

    view.assignView(this._tree, this._owner, lowerValue, upperValue);

    return view;
  }

  unionWith(other: Iterable<T>): void {
    for (const item of argumentOther(other)) {
      if (!this.contains(item)) {
        this.add(item);
      }
    }
  }

  /** Bumps whenever a non-empty set intersects another sequence (.NET rebuilds it); with itself it changes nothing. */
  intersectWith(other: Iterable<T>): void {
    const items = argumentOther(other);

    if (items === this || this.count === 0) {
      return;
    }

    const kept = this.distinct(items);
    const removed = this.toArray().filter((item) => !kept.contains(item));

    this.bumpVersion();

    for (const item of removed) {
      this._tree.delete(item);
    }
  }

  exceptWith(other: Iterable<T>): void {
    const items = argumentOther(other);

    if (items === this) {
      this.clear();

      return;
    }

    for (const item of items) {
      if (this.contains(item)) {
        this.remove(item);
      }
    }
  }

  symmetricExceptWith(other: Iterable<T>): void {
    const items = argumentOther(other);

    if (items === this) {
      this.clear();

      return;
    }

    for (const item of this.distinct(items)) {
      if (this.contains(item)) {
        this.remove(item);
      } else {
        this.add(item);
      }
    }
  }

  isSubsetOf(other: Iterable<T>): boolean {
    return setAlgebra.isSubsetOf(this, other, (items) => this.distinct(items));
  }

  isSupersetOf(other: Iterable<T>): boolean {
    return setAlgebra.isSupersetOf(this, other);
  }

  isProperSubsetOf(other: Iterable<T>): boolean {
    return setAlgebra.isProperSubsetOf(this, other, (items) => this.distinct(items));
  }

  isProperSupersetOf(other: Iterable<T>): boolean {
    return setAlgebra.isProperSupersetOf(this, other, (items) => this.distinct(items));
  }

  overlaps(other: Iterable<T>): boolean {
    return setAlgebra.overlaps(this, other);
  }

  setEquals(other: Iterable<T>): boolean {
    return setAlgebra.setEquals(this, other, (items) => this.distinct(items));
  }

  copyTo(array: T[], arrayIndex = 0): void {
    copyIntoSorted(this.toArray(), array, arrayIndex);
  }

  /** A detached copy in ascending order. */
  toArray(): T[] {
    const items: T[] = [];

    for (let node = this.firstNode(); node !== null; node = this.nextNode(node)) {
      items.push(node.key);
    }

    return items;
  }

  toJSON(): T[] {
    return this.toArray();
  }

  /** .NET's ``System.Collections.Generic.SortedSet`1[T]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Generic.SortedSet`1",
      ["T"],
      this.$typeArguments$SortedSet,
    );
  }

  [Symbol.iterator](): Iterator<T> {
    return new SortedSetIterator(this, this.firstNode(), true);
  }

  /** @internal The first node in this set's range. */
  firstNode(): RedBlackNode<T, undefined> | null {
    const bounds = this._bounds;
    const node = bounds.bounded ? this._tree.ceiling(bounds.lower) : this._tree.first();

    return node !== null && this.belowUpper(node.key) ? node : null;
  }

  /** @internal The last node in this set's range. */
  lastNode(): RedBlackNode<T, undefined> | null {
    const bounds = this._bounds;
    const node = bounds.bounded ? this._tree.floor(bounds.upper) : this._tree.last();

    return node !== null && this.aboveLower(node.key) ? node : null;
  }

  /** @internal The in-order successor of `node` inside this set's range. */
  nextNode(node: RedBlackNode<T, undefined>): RedBlackNode<T, undefined> | null {
    const next = successor(node);

    return next !== null && this.belowUpper(next.key) ? next : null;
  }

  /** @internal The in-order predecessor of `node` inside this set's range. */
  previousNode(node: RedBlackNode<T, undefined>): RedBlackNode<T, undefined> | null {
    const previous = predecessor(node);

    return previous !== null && this.aboveLower(previous.key) ? previous : null;
  }

  /** Turns a set fresh from the constructor into a view of `tree` (one hidden class for both). */
  private assignView(
    tree: RedBlackTree<T, undefined>,
    owner: SortedSet<T>,
    lower: T,
    upper: T,
  ): void {
    Object.defineProperty(this, "_tree", { value: tree });
    Object.defineProperty(this, "_owner", { value: owner });
    Object.defineProperty(this, "_bounds", { value: { bounded: true, lower, upper } });
  }

  private bumpVersion(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._owner._version++;
    }
  }

  private compareItems(x: T, y: T): number {
    return this._tree.compare(x, y);
  }

  private inRange(item: T): boolean {
    return this.aboveLower(item) && this.belowUpper(item);
  }

  private aboveLower(item: T): boolean {
    const bounds = this._bounds;

    return !bounds.bounded || this.compareItems(item, bounds.lower) >= 0;
  }

  private belowUpper(item: T): boolean {
    const bounds = this._bounds;

    return !bounds.bounded || this.compareItems(item, bounds.upper) <= 0;
  }

  /** `other` as a set with this set's comparer, so it is deduplicated by this set's rule. */
  private distinct(items: Iterable<T>): SortedSet<T> {
    // `instanceof` cannot narrow the element type: a `SortedSet` passed as `Iterable<T>` holds `T`s.
    return items instanceof SortedSet && items._comparer === this._comparer
      ? (items as SortedSet<T>)
      : new SortedSet(items, this._comparer);
  }
}

/** The in-order (or reverse) walk of a set's range, checked against the owner's version on every step. */
class SortedSetIterator<T> implements Iterator<T> {
  private readonly _set: SortedSet<T>;
  private readonly _expectedVersion: number;
  private readonly _forward: boolean;
  private _node: RedBlackNode<T, undefined> | null;

  constructor(set: SortedSet<T>, start: RedBlackNode<T, undefined> | null, forward: boolean) {
    this._set = set;
    this._expectedVersion =
      typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__ ? set._owner._version : 0;
    this._forward = forward;
    this._node = start;
  }

  next(): IteratorResult<T> {
    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      this._set._owner._version !== this._expectedVersion
    ) {
      throwEnumeratorModified();
    }

    const current = this._node;

    if (current === null) {
      return { done: true, value: undefined };
    }

    this._node = this._forward ? this._set.nextNode(current) : this._set.previousNode(current);

    return { done: false, value: current.key };
  }
}

function argumentOther<T>(other: Iterable<T> | null | undefined): Iterable<T> {
  if (other === null || other === undefined) {
    throwArgumentNull("other");
  }

  return other;
}
