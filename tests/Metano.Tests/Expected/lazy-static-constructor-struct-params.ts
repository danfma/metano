import { HashCode, typeInitializer } from "metano-runtime";

export class Bag {
  static readonly #$init = /* @__PURE__ */ typeInitializer("Bag", () => {
  }) as () => void;

  w: number = 0;
  count: number = 0;

  constructor(...values: number[]) {
    Bag.#$init();
    this.w = 7;
    this.count = values.length;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.w === other.w && this.count === other.count;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.w);
    hc.add(this.count);

    return hc.toHashCode();
  }
}
