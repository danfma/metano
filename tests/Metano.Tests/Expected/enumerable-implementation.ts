import { List } from "metano-runtime/system/collections";
import type { IBag } from "./i-bag.js";

export class Bag<T> implements IBag<T> {
  declare private readonly _items: List<T>;

  constructor() {
    this._items = List.of<T>([]);
  }

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
