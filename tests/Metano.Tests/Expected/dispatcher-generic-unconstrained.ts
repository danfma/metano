import { isInt32 } from "metano-runtime";

export class Bag<T> {
  protected static readonly byBagItem = /* @__PURE__ */ Symbol("Bag(T item)");
  protected static readonly byBagItemCount = /* @__PURE__ */ Symbol("Bag(T item, int count)");
  readonly log: string = "";

  constructor(overload: typeof Bag.byBagItem, item: T);
  constructor(overload: typeof Bag.byBagItemCount, item: T, count: number);
  constructor(item: T, count: number);
  constructor(item: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Bag.byBagItem) {
      this.log = "item";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Bag.byBagItemCount) {
      this.log = "count";

      return;
    }

    if (args.length === 2 && isInt32(args[1])) {
      this.log = "count";

      return;
    }

    if (args.length === 1) {
      this.log = "item";

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromItem<T>(item: T): Bag<T> {
    return new Bag(Bag.byBagItem, item);
  }

  static fromItemCount<T>(item: T, count: number): Bag<T> {
    return new Bag(Bag.byBagItemCount, item, count);
  }
}
