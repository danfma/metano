import { HashCode } from "../hash-code.ts";

/**
 * A HashSet implementation that uses equals()/hashCode() for value-based
 * equality when available, falling back to === for primitives.
 *
 * Inspired by Kotlin/JS and Fable's approach to bridging .NET equality
 * semantics to JavaScript.
 */
export class HashSet<T> implements Iterable<T> {
  private _buckets = new Map<number, T[]>();
  private _size = 0;

  constructor(items?: Iterable<T>) {
    if (items) {
      for (const item of items) {
        this.add(item);
      }
    }
  }

  get size(): number {
    return this._size;
  }

  add(item: T): this {
    if (this._isPrimitive(item)) {
      return this._addPrimitive(item);
    }
    const hash = this._getHash(item);
    const bucket = this._buckets.get(hash);
    if (!bucket) {
      this._buckets.set(hash, [item]);
      this._size++;
      return this;
    }
    if (!bucket.some((existing) => this._areEqual(existing, item))) {
      bucket.push(item);
      this._size++;
    }
    return this;
  }

  has(item: T): boolean {
    if (this._isPrimitive(item)) {
      return this._hasPrimitive(item);
    }
    const hash = this._getHash(item);
    const bucket = this._buckets.get(hash);
    return bucket?.some((existing) => this._areEqual(existing, item)) ?? false;
  }

  delete(item: T): boolean {
    if (this._isPrimitive(item)) {
      return this._deletePrimitive(item);
    }
    const hash = this._getHash(item);
    const bucket = this._buckets.get(hash);
    if (!bucket) return false;
    const index = bucket.findIndex((existing) => this._areEqual(existing, item));
    if (index === -1) return false;
    bucket.splice(index, 1);
    if (bucket.length === 0) this._buckets.delete(hash);
    this._size--;
    return true;
  }

  clear(): void {
    this._buckets.clear();
    this._size = 0;
  }

  forEach(callback: (item: T) => void): void {
    for (const item of this) {
      callback(item);
    }
  }

  *values(): IterableIterator<T> {
    if (this._primitiveSet) {
      yield* this._primitiveSet;
    }
    for (const bucket of this._buckets.values()) {
      yield* bucket;
    }
  }

  *[Symbol.iterator](): Iterator<T> {
    yield* this.values();
  }

  toArray(): T[] {
    return [...this];
  }

  // ─── Primitive fast path ─────────────────────────────────
  // Primitives use a dedicated bucket (hash = NaN) with a native Set
  // for O(1) lookup via ===.

  private _primitiveSet?: Set<T>;

  private _isPrimitive(item: T): boolean {
    const t = typeof item;
    return (
      t === "string" || t === "number" || t === "boolean" || item === null || item === undefined
    );
  }

  private _getPrimitiveSet(): Set<T> {
    return (this._primitiveSet ??= new Set<T>());
  }

  private _addPrimitive(item: T): this {
    const set = this._getPrimitiveSet();
    const before = set.size;
    set.add(item);
    if (set.size > before) this._size++;
    return this;
  }

  private _hasPrimitive(item: T): boolean {
    return this._primitiveSet?.has(item) ?? false;
  }

  private _deletePrimitive(item: T): boolean {
    if (!this._primitiveSet) return false;
    const deleted = this._primitiveSet.delete(item);
    if (deleted) this._size--;
    return deleted;
  }

  // ─── Equality helpers ────────────────────────────────────

  private _getHash(item: T): number {
    if (
      typeof item === "object" &&
      item !== null &&
      "hashCode" in item &&
      typeof (item as any).hashCode === "function"
    ) {
      return (item as any).hashCode();
    }
    return HashCode.combine(item);
  }

  private _areEqual(a: T, b: T): boolean {
    if (a === b) return true;
    if (
      typeof a === "object" &&
      a !== null &&
      "equals" in a &&
      typeof (a as any).equals === "function"
    ) {
      return (a as any).equals(b);
    }
    return false;
  }
}
