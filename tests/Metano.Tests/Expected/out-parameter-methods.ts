import { intDiv, tryGetValue } from "metano-runtime";
import type { ILookup } from "./i-lookup";
import type { Item } from "./item";

export class Catalog implements ILookup {
  private readonly _items: Map<string, Item> = new Map<string, Item>();

  constructor() { }

  tryFind(key: string): readonly [found: true, item: Item] | readonly [found: false, item: Item | null] {
    let item: Item | null;
    const [$found0, found] = tryGetValue(this._items, key, null);

    if ($found0) {
      item = found;

      return [true, item] as readonly [found: true, item: Item] | readonly [found: false, item: Item | null];
    }

    item = null;

    return [false, item] as readonly [found: true, item: Item] | readonly [found: false, item: Item | null];
  }

  tryName(key: string): readonly [found: true, name: string] | readonly [found: false, name: string | null] {
    let name: string | null;
    const [found, item] = this.tryFind(key);
    name = item == null ? null : item.name;

    return [found, name] as readonly [found: true, name: string] | readonly [found: false, name: string | null];
  }

  static split(value: number): readonly [tens: number, units: number] {
    let tens: number;
    let units: number;
    units = value % 10;

    if (value < 10) {
      tens = 0;

      return [tens, units];
    }

    tens = intDiv(value - units, 10);

    return [tens, units];
  }

  static tryHalf(value: number): readonly [found: boolean, half: number] {
    let half: number;
    half = intDiv(value, 2);

    return [value % 2 === 0, half];
  }

  static remainder(a: number, b: number): readonly [result: number, quotient: number] {
    let quotient: number;

    return [a - (quotient = intDiv(a, b)) * b, quotient];
  }

  use(key: string): string {
    const [$found0, item] = this.tryFind(key);

    if ($found0) {
      return item.name;
    }

    const [tens] = Catalog.split(42);
    const [$found1, name] = this.tryName(key);

    return $found1 ? name + tens : "none";
  }
}
