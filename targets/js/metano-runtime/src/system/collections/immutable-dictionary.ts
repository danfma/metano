import type { CollectionType, RuntimeType } from "../reflection/runtime-type.ts";
import {
  closeCollectionFor,
  emptyCollectionFor,
  sameCollectionTypeArguments,
} from "./close-collection.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import {
  defaultEqualityComparerFor,
  elementEquals,
  type IEqualityComparer,
} from "./default-comparer.ts";
import { KeyCollection, ValueCollection } from "./dictionary-views.ts";
import type { IImmutableDictionary } from "./immutable-interfaces.ts";
import { ImmutableHashStorage, normalizedComparer } from "./immutable-storage.ts";
import type { IReadOnlyDictionary } from "./interfaces.ts";
import {
  requireKey,
  throwArgumentNull,
  throwImmutableDuplicateKey,
  throwKeyNotFound,
} from "./throw-helpers.ts";

let ImmutableDictionary$rt: CollectionType<ImmutableDictionary<unknown, unknown>> | undefined;
let empty: ImmutableDictionary<unknown, unknown> | undefined;

/**
 * Mirrors `System.Collections.Immutable.ImmutableDictionary<TKey, TValue>`
 * (spec 011 FR-054): copy-on-write over `immutable-storage.ts`'s append-only
 * keyed store with W4-11's tip-append sharing: an insertion of a new key on
 * the newest version appends in place; every other change copies, and an
 * existing key's value is never overwritten in a shared store. Keys by
 * `elementEquals` / `elementHashCode` or the key comparer; values by the value
 * comparer. Calls that change nothing return the receiver where .NET does
 * (probed on .NET 10).
 */
export class ImmutableDictionary<K, V>
  implements IImmutableDictionary<K, V>, IReadOnlyDictionary<K, V>
{
  declare readonly _storage: ImmutableHashStorage<K, V>;
  declare readonly _count: number;
  declare readonly _valueComparer: IEqualityComparer<V> | null;
  declare _keys: KeyCollection<K> | undefined;
  declare _values: ValueCollection<V> | undefined;
  declare readonly $typeArguments$ImmutableDictionary: readonly RuntimeType[] | undefined;

  /** Internal: the factories and mutators build every instance (C# has no public constructor). */
  constructor(
    storage: ImmutableHashStorage<K, V>,
    count: number,
    valueComparer: IEqualityComparer<V> | null,
    typeArguments: readonly RuntimeType[] | undefined,
  ) {
    Object.defineProperty(this, "_storage", { value: storage });
    Object.defineProperty(this, "_count", { value: count });
    Object.defineProperty(this, "_valueComparer", { value: valueComparer });
    Object.defineProperty(this, "_keys", { value: undefined, writable: true });
    Object.defineProperty(this, "_values", { value: undefined, writable: true });
    Object.defineProperty(this, "$typeArguments$ImmutableDictionary", { value: typeArguments });
  }

  /** `ImmutableDictionary<K, V>.Empty`: one instance (default comparers), built on first read. */
  static empty<K, V>(typeArguments?: readonly RuntimeType[] | null): ImmutableDictionary<K, V> {
    if (typeArguments) {
      return emptyCollectionFor(
        ImmutableDictionary.$runtimeType,
        typeArguments,
        () => new ImmutableDictionary<K, V>(new ImmutableHashStorage(null), 0, null, typeArguments),
      );
    }

    empty ??= new ImmutableDictionary<unknown, unknown>(
      new ImmutableHashStorage(null),
      0,
      null,
      undefined,
    );

    // The one empty instance serves every `K` / `V`: it holds no entry.
    return empty as ImmutableDictionary<K, V>;
  }

  /** `ImmutableDictionary.Create<K, V>([keyComparer[, valueComparer]])`. */
  static create<K, V>(
    keyComparer?: IEqualityComparer<K> | null,
    valueComparer?: IEqualityComparer<V> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableDictionary<K, V> {
    if (
      typeArguments != null &&
      normalizedComparer(keyComparer) === null &&
      normalizedComparer(valueComparer) === null
    ) {
      return ImmutableDictionary.empty<K, V>(typeArguments);
    }

    return new ImmutableDictionary<K, V>(
      new ImmutableHashStorage(normalizedComparer(keyComparer)),
      0,
      normalizedComparer(valueComparer),
      typeArguments ?? undefined,
    );
  }

  /**
   * `CreateRange`, `ToImmutableDictionary()`: add semantics (a repeated key with
   * a different value throws), or `entries` itself when it is an
   * `ImmutableDictionary` with those comparers.
   */
  static from<K, V>(
    entries: Iterable<readonly [K, V]> | null | undefined,
    keyComparer?: IEqualityComparer<K> | null,
    valueComparer?: IEqualityComparer<V> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableDictionary<K, V> {
    if (entries === null || entries === undefined) {
      throwArgumentNull("items");
    }

    if (
      entries instanceof ImmutableDictionary &&
      sameCollectionTypeArguments(entries.$typeArguments$ImmutableDictionary, typeArguments) &&
      entries._storage.comparer === normalizedComparer(keyComparer) &&
      entries._valueComparer === normalizedComparer(valueComparer)
    ) {
      // `instanceof` cannot narrow the type arguments: an `ImmutableDictionary` passed as entries holds `[K, V]`s.
      return entries as ImmutableDictionary<K, V>;
    }

    return ImmutableDictionary.create<K, V>(keyComparer, valueComparer, typeArguments).addRange(
      entries,
    );
  }

  /** `source.ToImmutableDictionary(keySelector[, keyComparer[, valueComparer]])`: each element is its value. */
  static fromSelectors<S, K>(
    source: Iterable<S> | null | undefined,
    keySelector: (item: S) => K,
    valueSelector?: null,
    keyComparer?: IEqualityComparer<K> | null,
    valueComparer?: IEqualityComparer<S> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableDictionary<K, S>;

  /** `source.ToImmutableDictionary(keySelector, elementSelector[, keyComparer[, valueComparer]])`. */
  static fromSelectors<S, K, V>(
    source: Iterable<S> | null | undefined,
    keySelector: (item: S) => K,
    valueSelector: (item: S) => V,
    keyComparer?: IEqualityComparer<K> | null,
    valueComparer?: IEqualityComparer<V> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableDictionary<K, V>;

  static fromSelectors<S, K, V>(
    source: Iterable<S> | null | undefined,
    keySelector: (item: S) => K,
    valueSelector?: ((item: S) => V) | null,
    keyComparer?: IEqualityComparer<K> | null,
    valueComparer?: IEqualityComparer<S | V> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ): ImmutableDictionary<K, S | V> {
    if (source === null || source === undefined) {
      throwArgumentNull("source");
    }

    if (typeof keySelector !== "function") {
      throwArgumentNull("keySelector");
    }

    const entries: (readonly [K, S | V])[] = [];

    for (const item of source) {
      entries.push([keySelector(item), valueSelector ? valueSelector(item) : item]);
    }

    return ImmutableDictionary.create<K, S | V>(keyComparer, valueComparer, typeArguments).addRange(
      entries,
    );
  }

  static get $runtimeType(): CollectionType<ImmutableDictionary<unknown, unknown>> {
    ImmutableDictionary$rt ??= {
      kind: "collection",
      collection: "ImmutableDictionary",
      name: "ImmutableDictionary",
      fullName: "System.Collections.Immutable.ImmutableDictionary",
      namespace: "System.Collections.Immutable",
      ctor: ImmutableDictionary,
      typeArguments: [],
      variance: ["invariant", "invariant"],
      interfaces: [
        { collection: "IImmutableDictionary", arguments: [0, 1] },
        { collection: "IReadOnlyDictionary", arguments: [0, 1] },
        { collection: "IDictionary", arguments: [0, 1] },
        { collection: "ICollection", arguments: null },
        { collection: "IReadOnlyCollection", arguments: null },
      ],
      test: (value: unknown): value is ImmutableDictionary<unknown, unknown> =>
        value instanceof ImmutableDictionary,
      closeFor: (instance) => closeCollectionFor(ImmutableDictionary.$runtimeType, instance),
    };

    return ImmutableDictionary$rt;
  }

  get count(): number {
    return this._count;
  }

  get isEmpty(): boolean {
    return this._count === 0;
  }

  get keyComparer(): IEqualityComparer<K> {
    return this._storage.comparer ?? defaultEqualityComparerFor<K>();
  }

  get valueComparer(): IEqualityComparer<V> {
    return this._valueComparer ?? defaultEqualityComparerFor<V>();
  }

  get keys(): KeyCollection<K> {
    return (this._keys ??= new KeyCollection<K>(this));
  }

  get values(): ValueCollection<V> {
    return (this._values ??= new ValueCollection<V>(this));
  }

  get(key: K): V {
    const position = this.find(requireKey(key));

    if (position < 0) {
      throwKeyNotFound(key);
    }

    return this.entryAt(position)[1];
  }

  containsKey(key: K): boolean {
    return this.find(requireKey(key)) >= 0;
  }

  containsValue(value: V): boolean {
    return this._storage
      .visible(this._count)
      .some(([, candidate]) => this.valuesEqual(candidate, value));
  }

  contains(pair: readonly [K, V]): boolean {
    const position = this.find(requireKey(pair[0]));

    return position >= 0 && this.valuesEqual(this.entryAt(position)[1], pair[1]);
  }

  /** `TryGetValue`: the value, or the caller's `default(V)` on a miss (D30). */
  tryGetValue<D>(
    key: K,
    missingValue: D,
  ): readonly [found: true, value: V] | readonly [found: false, value: D] {
    const position = this.find(requireKey(key));

    return position < 0 ? [false, missingValue] : [true, this.entryAt(position)[1]];
  }

  /** `TryGetKey`: the stored key equal to `equalKey`, or `equalKey` itself on a miss, as .NET's (the `default(K)` argument, D30, is not read). */
  tryGetKey(equalKey: K, _defaultValue?: unknown): readonly [boolean, K] {
    if (equalKey === null || equalKey === undefined) {
      throwArgumentNull("equalKey");
    }

    const position = this.find(equalKey);

    return position < 0 ? [false, equalKey] : [true, this.entryAt(position)[0]];
  }

  getValueOrDefault<D>(key: K, defaultValue: D): V | D {
    return this.tryGetValue(key, defaultValue)[1];
  }

  /** The receiver when `key` holds an equal value; `ArgumentException` when it holds a different one. */
  add(key: K, value: V): ImmutableDictionary<K, V> {
    const position = this.find(requireKey(key));

    if (position >= 0) {
      return this.valuesEqual(this.entryAt(position)[1], value)
        ? this
        : throwImmutableDuplicateKey(key);
    }

    return this.appended(key, value);
  }

  addRange(pairs: Iterable<readonly [K, V]>): ImmutableDictionary<K, V> {
    let result: ImmutableDictionary<K, V> = this;

    for (const [key, value] of argument(pairs, "pairs")) {
      result = result.add(key, value);
    }

    return result;
  }

  /** Insert or replace; the receiver when `key` already holds an equal value. A replacement copies (W4-11). */
  setItem(key: K, value: V): ImmutableDictionary<K, V> {
    const position = this.find(requireKey(key));

    if (position < 0) {
      return this.appended(key, value);
    }

    if (this.valuesEqual(this.entryAt(position)[1], value)) {
      return this;
    }

    const entries = this._storage.visible(this._count);

    entries[position] = [this.entryAt(position)[0], value];

    return this.copyOf(entries);
  }

  /** Last value wins; the receiver when nothing changes. */
  setItems(pairs: Iterable<readonly [K, V]>): ImmutableDictionary<K, V> {
    let result: ImmutableDictionary<K, V> = this;

    for (const [key, value] of argument(pairs, "items")) {
      result = result.setItem(key, value);
    }

    return result;
  }

  remove(key: K): ImmutableDictionary<K, V> {
    const position = this.find(requireKey(key));

    if (position < 0) {
      return this;
    }

    const entries = this._storage.visible(this._count);

    entries.splice(position, 1);

    return this.copyOf(entries);
  }

  removeRange(keys: Iterable<K>): ImmutableDictionary<K, V> {
    let result: ImmutableDictionary<K, V> = this;

    for (const key of argument(keys, "keys")) {
      result = result.remove(key);
    }

    return result;
  }

  /** `Clear`: the empty dictionary with this one's comparers. */
  clear(): ImmutableDictionary<K, V> {
    if (this._storage.comparer === null && this._valueComparer === null) {
      return ImmutableDictionary.empty(this.$typeArguments$ImmutableDictionary);
    }

    return ImmutableDictionary.create(
      this._storage.comparer,
      this._valueComparer,
      this.$typeArguments$ImmutableDictionary,
    );
  }

  /** The same entries under new comparers; the receiver when both are the same. */
  withComparers(
    keyComparer: IEqualityComparer<K> | null,
    valueComparer?: IEqualityComparer<V> | null,
  ): ImmutableDictionary<K, V> {
    const keys = normalizedComparer(keyComparer);
    const values =
      valueComparer === undefined ? this._valueComparer : normalizedComparer(valueComparer);

    if (keys === this._storage.comparer && values === this._valueComparer) {
      return this;
    }

    if (keys === this._storage.comparer) {
      return new ImmutableDictionary(
        this._storage,
        this._count,
        values,
        this.$typeArguments$ImmutableDictionary,
      );
    }

    return ImmutableDictionary.create<K, V>(
      keys,
      values,
      this.$typeArguments$ImmutableDictionary,
    ).addRange(this._storage.visible(this._count));
  }

  /** `String(key)` → value. */
  toJSON(): Record<string, V> {
    const json: Record<string, V> = {};

    for (const [key, value] of this._storage.visible(this._count)) {
      json[String(key)] = value;
    }

    return json;
  }

  /** .NET's ``System.Collections.Immutable.ImmutableDictionary`2[TKey,TValue]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableDictionary`2",
      ["TKey", "TValue"],
      this.$typeArguments$ImmutableDictionary,
    );
  }

  [Symbol.iterator](): Iterator<readonly [K, V]> {
    return new ImmutableDictionaryIterator(this._storage.entries, this._count);
  }

  private find(key: K): number {
    return this._storage.find(key, this._count);
  }

  private entryAt(position: number): readonly [K, V] {
    return this._storage.entryAt(position);
  }

  private valuesEqual(a: V, b: V): boolean {
    return this._valueComparer === null ? elementEquals(a, b) : this._valueComparer.equals(a, b);
  }

  /** W4-11: a new key appends in place when this version owns the store's tip, else after a copy. */
  private appended(key: K, value: V): ImmutableDictionary<K, V> {
    return new ImmutableDictionary(
      this._storage.append(this._count, key, value),
      this._count + 1,
      this._valueComparer,
      this.$typeArguments$ImmutableDictionary,
    );
  }

  private copyOf(entries: readonly (readonly [K, V])[]): ImmutableDictionary<K, V> {
    if (entries.length === 0) {
      return this.clear();
    }

    return new ImmutableDictionary(
      ImmutableHashStorage.of(this._storage.comparer, entries),
      entries.length,
      this._valueComparer,
      this.$typeArguments$ImmutableDictionary,
    );
  }
}

/** An index walk over a version's first `count` entries (nothing can change them). */
class ImmutableDictionaryIterator<K, V> implements Iterator<readonly [K, V]> {
  private readonly _entries: ReadonlyArray<readonly [K, V]>;
  private readonly _count: number;
  private _index: number;

  constructor(entries: ReadonlyArray<readonly [K, V]>, count: number) {
    this._entries = entries;
    this._count = count;
    this._index = 0;
  }

  next(): IteratorResult<readonly [K, V]> {
    if (this._index >= this._count) {
      return { done: true, value: undefined };
    }

    // A position below the version's count: `noUncheckedIndexedAccess` cannot see the bound.
    return { done: false, value: this._entries[this._index++] as readonly [K, V] };
  }
}

function argument<T>(value: Iterable<T> | null | undefined, paramName: string): Iterable<T> {
  if (value === null || value === undefined) {
    throwArgumentNull(paramName);
  }

  return value;
}
