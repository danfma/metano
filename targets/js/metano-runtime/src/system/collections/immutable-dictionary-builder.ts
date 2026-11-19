import type { RuntimeType } from "../reflection/runtime-type.ts";
import { BuilderIterator } from "./builder-iterator.ts";
import { collectionTypeName } from "./collection-type-name.ts";
import { defaultEqualityComparerFor, type IEqualityComparer } from "./default-comparer.ts";
import { Dictionary } from "./dictionary.ts";
import type { KeyCollection, ValueCollection } from "./dictionary-views.ts";
import { ImmutableDictionary } from "./immutable-dictionary.ts";
import type { IDictionary } from "./interfaces.ts";

/**
 * Mirrors `ImmutableDictionary<TKey, TValue>.Builder` (spec 011 FR-054): a
 * small mutable class over its own `Dictionary` (`instanceof Dictionary` is
 * `false`). It keeps its own version, so any mutation invalidates a running
 * enumeration as .NET's builder does (a removal included, unlike
 * `Dictionary`). `toImmutableDictionary` copies, so a builder never shares
 * storage with an immutable version (W4-11).
 */
export class ImmutableDictionaryBuilder<K, V> implements IDictionary<K, V> {
  declare readonly _dictionary: Dictionary<K, V>;
  declare _version: number;
  declare readonly _valueComparer: IEqualityComparer<V> | null;
  declare readonly _typeArguments: readonly RuntimeType[] | undefined;

  /**
   * `ToBuilder()` / `CreateBuilder<K, V>([keyComparer[, valueComparer]])`: a
   * copy of `source`, whose comparers and type arguments it keeps when `source`
   * is an `ImmutableDictionary` and no comparer is given.
   */
  constructor(
    source?: Iterable<readonly [K, V]> | null,
    keyComparer?: IEqualityComparer<K> | null,
    valueComparer?: IEqualityComparer<V> | null,
    typeArguments?: readonly RuntimeType[] | null,
  ) {
    // `instanceof` cannot narrow the type arguments: a source passed as this builder's type holds them.
    const immutable =
      source instanceof ImmutableDictionary ? (source as ImmutableDictionary<K, V>) : undefined;
    const dictionary = new Dictionary<K, V>(
      null,
      keyComparer ?? immutable?._storage.comparer ?? null,
    );

    for (const [key, value] of source ?? []) {
      dictionary.set(key, value);
    }

    Object.defineProperty(this, "_dictionary", { value: dictionary });
    Object.defineProperty(this, "_version", { value: 0, writable: true });
    Object.defineProperty(this, "_valueComparer", {
      value: valueComparer ?? immutable?._valueComparer ?? null,
    });
    Object.defineProperty(this, "_typeArguments", {
      value: typeArguments ?? immutable?.$typeArguments$ImmutableDictionary,
    });
  }

  get count(): number {
    return this._dictionary.count;
  }

  get isReadOnly(): boolean {
    return false;
  }

  get keyComparer(): IEqualityComparer<K> {
    return this._dictionary._store.comparer ?? defaultEqualityComparerFor<K>();
  }

  get valueComparer(): IEqualityComparer<V> {
    return this._valueComparer ?? defaultEqualityComparerFor<V>();
  }

  get keys(): KeyCollection<K> {
    return this._dictionary.keys;
  }

  get values(): ValueCollection<V> {
    return this._dictionary.values;
  }

  get(key: K): V {
    return this._dictionary.get(key);
  }

  set(key: K, value: V): void {
    this._dictionary.set(key, value);
    this.changed(true);
  }

  update(key: K, f: (current: V) => V): V {
    const value = f(this.get(key));

    this.set(key, value);

    return value;
  }

  updateReturningOld(key: K, f: (current: V) => V): V {
    const current = this.get(key);

    this.set(key, f(current));

    return current;
  }

  add(key: K, value: V): void {
    this._dictionary.add(key, value);
    this.changed(true);
  }

  remove(key: K): boolean {
    return this.changed(this._dictionary.remove(key));
  }

  clear(): void {
    this._dictionary.clear();
    this.changed(true);
  }

  containsKey(key: K): boolean {
    return this._dictionary.containsKey(key);
  }

  containsValue(value: V): boolean {
    return this._dictionary.containsValue(value);
  }

  tryGetValue<D>(
    key: K,
    missingValue: D,
  ): readonly [found: true, value: V] | readonly [found: false, value: D] {
    return this._dictionary.tryGetValue(key, missingValue);
  }

  getValueOrDefault<D>(key: K, defaultValue: D): V | D {
    return this._dictionary.getValueOrDefault(key, defaultValue);
  }

  toJSON(): Record<string, V> {
    return this._dictionary.toJSON();
  }

  /** .NET's ``System.Collections.Immutable.ImmutableDictionary`2+Builder[TKey,TValue]`` text. */
  toString(): string {
    return collectionTypeName(
      "System.Collections.Immutable.ImmutableDictionary`2+Builder",
      ["TKey", "TValue"],
      this._typeArguments,
    );
  }

  [Symbol.iterator](): Iterator<readonly [K, V]> {
    return new BuilderIterator(this, this._dictionary[Symbol.iterator]());
  }

  /** Bumps the version when `changed`; returns `changed`. */
  private changed(changed: boolean): boolean {
    if (changed && (typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__)) {
      this._version++;
    }

    return changed;
  }
}

/** `builder.ToImmutable()`: a copy, never sharing the builder's storage. */
export function toImmutableDictionary<K, V>(
  builder: ImmutableDictionaryBuilder<K, V>,
): ImmutableDictionary<K, V> {
  return ImmutableDictionary.from(
    [...builder._dictionary],
    builder._dictionary._store.comparer,
    builder._valueComparer,
    builder._typeArguments,
  );
}
