import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import { defaultComparer, elementEquals, type IComparer } from "./default-comparer.ts";
import { KeyCollection, ValueCollection } from "./dictionary-views.ts";
import type { IDictionary, IReadOnlyDictionary } from "./interfaces.ts";
import { RedBlackTree, type RedBlackNode, successor } from "./red-black-tree.ts";
import { checkedKey, compareFunctionOf, defineField } from "./sorted-support.ts";
import { checkCopyTo, throwDuplicatePair } from "./sorted-throw-helpers.ts";
import { throwArgumentNull, throwKeyNotFound, throwStackModified } from "./throw-helpers.ts";

let SortedDictionary$rt: CollectionType<SortedDictionary<unknown, unknown>> | undefined;

/**
 * Mirrors `System.Collections.Generic.SortedDictionary<TKey, TValue>` (spec 011
 * FR-054): a red-black tree of entries ordered by the key comparer, which
 * alone decides key equality. Enumeration (and `keys` / `values`, live views)
 * is in key order.
 *
 * Every `add`, `set`, `remove` and `clear` bumps the version, a no-op included
 * (`remove` of an absent key, `set` of an existing one), as .NET does; unlike
 * `Dictionary`, `remove` and `clear` invalidate a running enumeration.
 */
export class SortedDictionary<K, V> implements IDictionary<K, V>, IReadOnlyDictionary<K, V> {
  declare readonly _tree: RedBlackTree<K, V>;
  declare _version: number;
  declare readonly _comparer: IComparer<K> | null;
  declare _keys: KeyCollection<K> | undefined;
  declare _values: ValueCollection<V> | undefined;
  declare readonly $typeArguments$SortedDictionary: readonly RuntimeType[] | undefined;

  constructor(
    entries?: Iterable<readonly [K, V]> | null,
    comparer?: IComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ) {
    const ownComparer = comparer ?? null;

    defineField(this, "_tree", new RedBlackTree<K, V>(compareFunctionOf(ownComparer)), false);
    defineField(this, "_version", 0, true);
    defineField(this, "_comparer", ownComparer, false);
    defineField(this, "_keys", undefined, true);
    defineField(this, "_values", undefined, true);
    defineField(this, "$typeArguments$SortedDictionary", typeArguments ?? undefined, false);

    if (entries !== null && entries !== undefined) {
      for (const [key, value] of entries) {
        this.add(key, value);
      }
    }
  }

  /** `new SortedDictionary<K, V>(dictionary[, comparer])`: a copy; `ArgumentNullException` (`dictionary`) for `null`. */
  static from<K, V>(
    source: Iterable<readonly [K, V]> | null | undefined,
    comparer?: IComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): SortedDictionary<K, V> {
    if (source === null || source === undefined) {
      throwArgumentNull("dictionary");
    }

    return new SortedDictionary(source, comparer, typeArguments);
  }

  /** The `{ { k, v } }` initializer: `add` semantics, a duplicate key throws. */
  static of<K, V>(
    entries: ReadonlyArray<readonly [K, V]>,
    comparer?: IComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): SortedDictionary<K, V> {
    return new SortedDictionary(entries, comparer, typeArguments);
  }

  /** The `{ [k] = v }` initializer (and JSON): indexer semantics, the last value wins. */
  static assign<K, V>(
    entries: ReadonlyArray<readonly [K, V]>,
    comparer?: IComparer<K> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): SortedDictionary<K, V> {
    const dictionary = new SortedDictionary<K, V>(null, comparer, typeArguments);

    for (const [key, value] of entries) {
      dictionary.set(key, value);
    }

    return dictionary;
  }

  static get $runtimeType(): CollectionType<SortedDictionary<unknown, unknown>> {
    return (SortedDictionary$rt ??= {
      kind: "collection",
      collection: "SortedDictionary",
      name: "SortedDictionary",
      fullName: "System.Collections.Generic.SortedDictionary",
      namespace: "System.Collections.Generic",
      ctor: SortedDictionary,
      typeArguments: [],
      variance: ["invariant", "invariant"],
      interfaces: [
        { collection: "IDictionary", arguments: [0, 1] },
        { collection: "IReadOnlyDictionary", arguments: [0, 1] },
        { collection: "ICollection", arguments: null },
        { collection: "IReadOnlyCollection", arguments: null },
      ],
      test: (value: unknown): value is SortedDictionary<unknown, unknown> =>
        value instanceof SortedDictionary,
    });
  }

  get count(): number {
    return this._tree.count;
  }

  get isReadOnly(): false {
    return false;
  }

  /** The argument comparer, or `defaultComparer` (`Comparer<K>.Default`). */
  get comparer(): IComparer<K> {
    return this._comparer ?? (defaultComparer as IComparer<K>);
  }

  /** A live view of the keys, in key order. */
  get keys(): KeyCollection<K> {
    return (this._keys ??= new KeyCollection<K>(this));
  }

  /** A live view of the values, in key order. */
  get values(): ValueCollection<V> {
    return (this._values ??= new ValueCollection<V>(this));
  }

  get(key: K): V {
    const node = this._tree.find(checkedKey(key));

    if (node === null) {
      throwKeyNotFound(key);
    }

    return node.value;
  }

  set(key: K, value: V): void {
    const { node, inserted } = this._tree.insert(checkedKey(key), value);

    if (!inserted) {
      node.value = value;
    }

    this.bumpVersion();
  }

  /** `dict[k] op= …`: read (`KeyNotFoundException` on a miss), apply, write; returns the new value. */
  update(key: K, f: (current: V) => V): V {
    const value = f(this.get(key));

    this.set(key, value);

    return value;
  }

  /** `dict[k]++` in expression position: as `update`, returning the old value. */
  updateReturningOld(key: K, f: (current: V) => V): V {
    const current = this.get(key);

    this.set(key, f(current));

    return current;
  }

  add(key: K, value: V): void {
    const checked = checkedKey(key);

    this.bumpVersion();

    if (!this._tree.insert(checked, value).inserted) {
      throwDuplicatePair(key, value);
    }
  }

  remove(key: K): boolean {
    const checked = checkedKey(key);

    this.bumpVersion();

    return this._tree.delete(checked);
  }

  clear(): void {
    this.bumpVersion();
    this._tree.clear();
  }

  containsKey(key: K): boolean {
    return this._tree.find(checkedKey(key)) !== null;
  }

  containsValue(value: V): boolean {
    for (let node = this._tree.first(); node !== null; node = successor(node)) {
      if (elementEquals(node.value, value)) {
        return true;
      }
    }

    return false;
  }

  /** `TryGetValue`: the value, or the caller's `default(V)` on a miss (D30). */
  tryGetValue<D>(
    key: K,
    missingValue: D,
  ): readonly [found: true, value: V] | readonly [found: false, value: D] {
    const node = this._tree.find(checkedKey(key));

    return node === null ? [false, missingValue] : [true, node.value];
  }

  /** `GetValueOrDefault`: the value, or the caller's default on a miss (D30). */
  getValueOrDefault<D>(key: K, defaultValue: D): V | D {
    const node = this._tree.find(checkedKey(key));

    return node === null ? defaultValue : node.value;
  }

  copyTo(array: (readonly [K, V])[], arrayIndex = 0): void {
    checkCopyTo(array, arrayIndex, this.count);

    let index = arrayIndex;

    for (const entry of this) {
      array[index++] = entry;
    }
  }

  /** `String(key)` → value, in key order. */
  toJSON(): Record<string, V> {
    const json: Record<string, V> = {};

    for (let node = this._tree.first(); node !== null; node = successor(node)) {
      json[String(node.key)] = node.value;
    }

    return json;
  }

  [Symbol.iterator](): Iterator<readonly [K, V]> {
    return new SortedDictionaryIterator(this);
  }

  private bumpVersion(): void {
    if (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) {
      this._version++;
    }
  }
}

/** The in-order walk of the entries, checked against the version on every step. */
class SortedDictionaryIterator<K, V> implements Iterator<readonly [K, V]> {
  private readonly dictionary: SortedDictionary<K, V>;
  private readonly version: number;
  private node: RedBlackNode<K, V> | null;

  constructor(dictionary: SortedDictionary<K, V>) {
    this.dictionary = dictionary;
    this.version = dictionary._version;
    this.node = dictionary._tree.first();
  }

  next(): IteratorResult<readonly [K, V]> {
    if (
      (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__) &&
      this.dictionary._version !== this.version
    ) {
      throwStackModified();
    }

    const current = this.node;

    if (current === null) {
      return { done: true, value: undefined };
    }

    this.node = successor(current);

    return { done: false, value: [current.key, current.value] };
  }
}
