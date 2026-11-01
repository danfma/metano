import type { IBag } from "./i-bag";

export class Bag<T> implements IBag<T> {
  private readonly _items: T[] = [];

  constructor() { }

  get size(): number {
    return this._items.length;
  }

  add(item: T): void {
    this._items.push(item);
  }

  getEnumerator(): Iterator<T> {
    return this._items[Symbol.iterator]();
  }

  [Symbol.iterator](): Iterator<T> {
    return this.getEnumerator();
  }
}
