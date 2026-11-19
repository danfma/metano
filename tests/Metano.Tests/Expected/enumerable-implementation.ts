import { List } from "metano-runtime/system/collections";
import type { IBag } from "./i-bag";

export class Bag<T> implements IBag<T> {
  private readonly _items: List<T> = List.of([]);

  constructor() { }

  get size(): number {
    return this._items.count;
  }

  add(item: T): void {
    this._items.add(item);
  }

  getEnumerator(): Iterator<T> {
    return this._items[Symbol.iterator]();
  }

  [Symbol.iterator](): Iterator<T> {
    return this.getEnumerator();
  }
}
